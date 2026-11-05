#if PLA_CREATIVE_BUILD
using System;
using System.Diagnostics;

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Pla
{
    /// <summary>
    /// Sand PLA Backend: synchronous direct-array simulation for playable ads.
    /// Owns buffer lifetime, publication and timing; generated kernels own physics.
    /// No native collections, Burst, worker jobs or compatibility job dispatch.
    /// </summary>
    public sealed class SandPlaBackend : ISandBackend, ISandViewBackend,
        IRegionalSandBackend, IScheduledSandBackend
    {
        private SandPlaBuffer<int> _current;
        private SandPlaBuffer<int> _next;
        private SandPlaBuffer<int> _intents;
        private SandPlaBuffer<int> _reconciliation;
        private SandPlaBuffer<SandViewCell> _view;
        private byte[] _gravity;
        private byte[] _gravitySource;
        private int _sandCount;
        private int _persistentGravityCount;
        private int _particleCount;
        private long _stateVersion;
        private long _viewVersion = -1;
        private uint _simulationTick;
        private bool _disposed;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int CellCount => SandPlaAlgorithmCore.CalculateCellCount(Width, Height);
        public SandPlaBuffer<int> CurrentGrid => _current;
        public SandPlaBuffer<SandViewCell> ViewBuffer => _view;
        public bool HasPersistentGravity => _persistentGravityCount > 0;
        public int ParticleCount => _particleCount;
        public double TickMs { get; private set; }

        public SandPlaBackend(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            try
            {
                _current = new SandPlaBuffer<int>(CellCount);
                _next = new SandPlaBuffer<int>(CellCount);
                _intents = new SandPlaBuffer<int>(CellCount);
                _reconciliation = new SandPlaBuffer<int>(CellCount);
                _view = new SandPlaBuffer<SandViewCell>(CellCount);
            }
            catch { Dispose(); throw; }
        }

        public void Step(StepParams parameters)
        {
            EnsureReady();
            long started = Stopwatch.GetTimestamp();
            if (_sandCount != 0)
            {
                SandStepSettings settings = SandPlaAlgorithmCore.NormalizeStep(
                    parameters, Width, Height, unchecked((int)_simulationTick));
                if (parameters.GravityField.IsCreated || HasPersistentGravity)
                    StepRegional(parameters.GravityField.Cells, settings);
                else
                    StepOrdinary(settings);
                _stateVersion++;
            }
            _simulationTick++;
            TickMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        }

        // Scheduling is an explicit synchronous barrier on this backend.
        public void ScheduleStep(StepParams parameters) => Step(parameters);
        public void CompleteStep() { }

        public int GetCell(int index)
        {
            EnsureReady();
            return _current[index];
        }

        public void SetCell(int index, int value)
        {
            EnsureReady();
            int[] cells = _current.GetArrayForStep();
            int previous = cells[index];
            int next = SandPlaAlgorithmCore.CanonicalizeCell(value);
            if (previous == next) return;
            _sandCount += ((next & 255) == 1 ? 1 : 0) - ((previous & 255) == 1 ? 1 : 0);
            _persistentGravityCount += (SandCell.HasPersistentGravity(next) ? 1 : 0)
                - (SandCell.HasPersistentGravity(previous) ? 1 : 0);
            cells[index] = next;
            _stateVersion++;
        }

        public bool RefreshViewBuffer()
        {
            EnsureReady();
            if (_viewVersion == _stateVersion) return false;
            bool changed = _viewVersion < 0;
            int[] cells = _current.GetArrayForStep();
            SandViewCell[] view = _view.GetArrayForStep();
            for (int index = 0; index < cells.Length; index++)
            {
                byte element = (byte)(cells[index] & 255);
                byte color = (byte)((cells[index] >> 16) & 255);
                if (view[index].elementType == element && view[index].packedColorVariant == color) continue;
                view[index] = new SandViewCell { elementType = element, packedColorVariant = color };
                changed = true;
            }
            _viewVersion = _stateVersion;
            return changed;
        }

        public void Resize(int width, int height)
        {
            EnsureReady();
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (width == Width && height == Height) return;
            int count = SandPlaAlgorithmCore.CalculateCellCount(width, height);
            SandPlaBuffer<int> current = null, next = null, intents = null, reconciliation = null;
            SandPlaBuffer<SandViewCell> view = null;
            int sand = 0, persistent = 0;
            try
            {
                current = new SandPlaBuffer<int>(count);
                next = new SandPlaBuffer<int>(count);
                intents = new SandPlaBuffer<int>(count);
                reconciliation = new SandPlaBuffer<int>(count);
                view = new SandPlaBuffer<SandViewCell>(count);
                int[] source = _current.GetArrayForStep();
                int[] destination = current.GetArrayForStep();
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int cell = source[(y * Height / height) * Width + x * Width / width];
                    destination[y * width + x] = cell;
                    if ((cell & 255) != 1) continue;
                    sand++;
                    if (SandCell.HasPersistentGravity(cell)) persistent++;
                }
            }
            catch
            {
                current?.Dispose(); next?.Dispose(); intents?.Dispose();
                reconciliation?.Dispose(); view?.Dispose();
                throw;
            }
            DisposeBuffers();
            _current = current; _next = next; _intents = intents;
            _reconciliation = reconciliation; _view = view;
            Width = width; Height = height;
            _sandCount = sand; _persistentGravityCount = persistent;
            _gravity = null; _gravitySource = null;
            _viewVersion = -1;
            _stateVersion++;
        }

        public void ClearGrid()
        {
            EnsureReady();
            int[] cells = _current.GetArrayForStep();
            Array.Clear(cells, 0, cells.Length);
            _sandCount = 0;
            _persistentGravityCount = 0;
            _stateVersion++;
        }

        public int CountParticles()
        {
            EnsureReady();
            int count = 0;
            foreach (int cell in _current.GetArrayForStep()) if (cell != 0) count++;
            _particleCount = count;
            return count;
        }

        public int CountSand()
        {
            EnsureReady();
            int count = 0;
            foreach (int cell in _current.GetArrayForStep()) if ((cell & 255) == 1) count++;
            _sandCount = count;
            return count;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeBuffers();
            _gravity = null; _gravitySource = null;
        }

        private void StepRegional(byte[] source, SandStepSettings settings)
        {
            int[] reconciliation = _reconciliation.GetArrayForStep();
            new SandPlaRegionalStep
            {
                current = _current.GetArrayForStep(), next = _next.GetArrayForStep(),
                intents = _intents.GetArrayForStep(), claims = reconciliation,
                settings = settings, gravity = SynchronizeGravity(source)
            }.Execute();
            _persistentGravityCount = reconciliation[0];
            Swap(ref _current, ref _next);
        }

        private void StepOrdinary(SandStepSettings settings)
        {
            int[] current = _current.GetArrayForStep();
            if (settings.enableVerticalSettle)
                for (int line = 0; line < SandPlaAlgorithmCore.GetGravityLineCount(settings); line++)
                    SandPlaAlgorithmCore.CompactEligibleLine(current, line, settings);
            new SandPlaStep
            {
                current = current, firstIntents = _intents.GetArrayForStep(),
                secondIntents = _next.GetArrayForStep(), reconciliationQueue = _reconciliation.GetArrayForStep(),
                width = settings.width, height = settings.height, disableGravity = settings.disableGravity,
                constantFallSpeed = settings.constantFallSpeed, gravityPerStep = settings.gravityPerStep,
                maxFallSpeed = settings.maxFallSpeed, slideChance = settings.slideChance,
                preventTemporaryEdgeVacancies = settings.preventTemporaryEdgeVacancies,
                enableLateralMixing = settings.enableLateralMixing, lateralMixChance = settings.lateralMixChance,
                lateralMixPasses = settings.lateralMixPasses, mixingSeed = settings.mixingSeed,
                simulationSeed = settings.simulationSeed, gravityX = settings.gravityX, gravityY = settings.gravityY,
                enableVerticalSettle = settings.enableVerticalSettle
            }.Execute();
            int passes = SandPlaAlgorithmCore.GetLateralMixPassCount(settings)
                + (settings.enableVerticalSettle ? 1 : 0);
            if (passes % 2 == 0) Swap(ref _current, ref _next);
            else
            {
                var previous = _current;
                _current = _intents;
                _intents = _next;
                _next = previous;
            }
        }

        private byte[] SynchronizeGravity(byte[] source)
        {
            if (source != null && source.Length != CellCount)
                throw new ArgumentException("Regional gravity must cover the complete simulation grid.");
            if (_gravity != null && _gravity.Length == CellCount && ReferenceEquals(source, _gravitySource)) return _gravity;
            _gravity = new byte[CellCount];
            if (source != null) Array.Copy(source, _gravity, source.Length);
            _gravitySource = source;
            return _gravity;
        }

        private void EnsureReady()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SandPlaBackend));
        }

        private void DisposeBuffers()
        {
            _current?.Dispose(); _next?.Dispose(); _intents?.Dispose();
            _reconciliation?.Dispose(); _view?.Dispose();
        }

        private static void Swap(ref SandPlaBuffer<int> first, ref SandPlaBuffer<int> second)
        {
            var previous = first; first = second; second = previous;
        }
    }
}
#endif
