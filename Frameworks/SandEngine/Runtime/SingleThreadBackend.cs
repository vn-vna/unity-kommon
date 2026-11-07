#if !PLA_CREATIVE_BUILD
using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using SandIntBuffer = Unity.Collections.NativeArray<int>;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>
    /// Deterministic main-thread backend for platforms without worker threads.
    /// </summary>
    public sealed class SingleThreadBackend : ISandBackend, ISandViewBackend, IRegionalSandBackend
    {
        private NativeArray<int> _current;
        private NativeArray<int> _next;
        private NativeArray<int> _engine;
        private NativeArray<int> _reconciliationQueue;
        private NativeArray<SandViewCell> _viewBuffer;
        private int _width;
        private int _height;
        private int _particleCount;
        private int _sandCount;
        private int _persistentGravityCount;
        public bool HasPersistentGravity => _persistentGravityCount > 0;
        private double _lastTickMs;
        private bool _disposed;
        private long _stateVersion;
        private long _viewVersion = -1;
        private uint _simulationTick;
        private RegionalSandState _regionalState;

        public int Width => _width;
        public int Height => _height;
        public int CellCount => SandAlgorithmCore.CalculateCellCount(_width, _height);
        public NativeArray<int> CurrentGrid => _current;
        public NativeArray<SandViewCell> ViewBuffer => _viewBuffer;
        public int ParticleCount => _particleCount;
        public double TickMs => _lastTickMs;

        public SingleThreadBackend(int width, int height)
        {
            Allocate(width, height);
        }

        public void Step(StepParams stepParams)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            if (_sandCount == 0)
            {
                _simulationTick++;
                CompleteStepTiming(startTimestamp);
                return;
            }

            int simulationSeed = unchecked((int)_simulationTick);
            SandStepSettings settings = SandAlgorithmCore.NormalizeStep(
                stepParams,
                _width,
                _height,
                simulationSeed
            );

            if (stepParams.GravityField.IsCreated || HasPersistentGravity)
            {
                _regionalState ??= new RegionalSandState();
                new RegionalSandStepJob
                {
                    current = _current, next = _next, intents = _engine, claims = _reconciliationQueue,
                    gravity = _regionalState.Synchronize(stepParams.GravityField.Cells, CellCount), settings = settings
                }.Run();
                _persistentGravityCount = _reconciliationQueue[0];
                SwapBuffers();
                _stateVersion++;
                _simulationTick++;
                CompleteStepTiming(startTimestamp);
                return;
            }

            SandIntBuffer current = _current;
            if(stepParams.EnableVerticalSettle)
                for(int line=0;line<SandAlgorithmCore.GetGravityLineCount(settings);line++)
                    SandAlgorithmCore.CompactEligibleLine(current,line,settings);

            new SingleThreadSandStepJob
            {
                current = current,
                firstIntents = _engine,
                secondIntents = _next,
                reconciliationQueue = _reconciliationQueue,
                width = settings.width,
                height = settings.height,
                disableGravity = settings.disableGravity,
                constantFallSpeed = settings.constantFallSpeed,
                gravityPerStep = settings.gravityPerStep,
                maxFallSpeed = settings.maxFallSpeed,
                slideChance = settings.slideChance,
                contactSlip = settings.contactSlip,
                preventTemporaryEdgeVacancies =
                    settings.preventTemporaryEdgeVacancies,
                enableLateralMixing = settings.enableLateralMixing,
                lateralMixChance = settings.lateralMixChance,
                lateralMixPasses = settings.lateralMixPasses,
                mixingSeed = settings.mixingSeed,
                simulationSeed = settings.simulationSeed,
                gravityX = settings.gravityX,
                gravityY = settings.gravityY,
                enableVerticalSettle = stepParams.EnableVerticalSettle
            }.Run();

            int mixingPassCount = SandAlgorithmCore.GetLateralMixPassCount(
                settings
            );
            int dependencyPassCount = mixingPassCount
                + (stepParams.EnableVerticalSettle ? 1 : 0);
            bool destinationIsEngine = dependencyPassCount % 2 != 0;
            if (destinationIsEngine)
            {
                RotateResolvedBuffers();
            }
            else
            {
                SwapBuffers();
            }

            _stateVersion++;
            _simulationTick++;

            CompleteStepTiming(startTimestamp);
        }

        public void SetCell(int index, int value)
        {
            int currentCell = _current[index];
            int nextCell = SandAlgorithmCore.CanonicalizeCell(value);
            if (currentCell == nextCell) return;
            UpdateSandCount(currentCell, nextCell);
            _current[index] = nextCell;
            _stateVersion++;
        }

        public int GetCell(int index)
        {
            return _current[index];
        }

        public bool RefreshViewBuffer()
        {
            if (_viewVersion == _stateVersion)
            {
                return false;
            }

            int chunks = RefreshSandViewJob.GetChunkCount(CellCount);
            new RefreshSandViewJob
            {
                source = _current,
                dest = _viewBuffer,
                changedChunks = _reconciliationQueue
            }.Run(chunks);

            bool changed = _viewVersion < 0;
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                changed |= _reconciliationQueue[chunk] != 0;
            }
            _viewVersion = _stateVersion;
            return changed;
        }

        public void Resize(int newWidth, int newHeight)
        {
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);
            if (newWidth == _width && newHeight == _height)
            {
                return;
            }

            int oldWidth = _width;
            int oldHeight = _height;
            int newCellCount = SandAlgorithmCore.CalculateCellCount(newWidth, newHeight);
            NativeArray<int> newCurrent = default;
            NativeArray<int> newNext = default;
            NativeArray<int> newEngine = default;
            NativeArray<int> newReconciliationQueue = default;
            NativeArray<SandViewCell> newViewBuffer = default;
            int newSandCount = 0;
            int newPersistentCount = 0;
            try
            {
                newCurrent = new NativeArray<int>(newCellCount, Allocator.Persistent);
                newNext = new NativeArray<int>(newCellCount, Allocator.Persistent);
                newEngine = new NativeArray<int>(newCellCount, Allocator.Persistent);
                newReconciliationQueue = new NativeArray<int>(
                    newCellCount,
                    Allocator.Persistent
                );
                newViewBuffer = new NativeArray<SandViewCell>(
                    newCellCount,
                    Allocator.Persistent
                );

                for (int y = 0; y < newHeight; y++)
                {
                    int sourceY = y * oldHeight / newHeight;
                    for (int x = 0; x < newWidth; x++)
                    {
                        int sourceX = x * oldWidth / newWidth;
                        int cell = _current[
                            sourceY * oldWidth + sourceX
                        ];
                        newCurrent[y * newWidth + x] = cell;
                        if ((cell & SandAlgorithmCore.TypeMask)
                            == (int)ElementType.Sand)
                        {
                            newSandCount++;
                            if (SandCell.HasPersistentGravity(cell)) newPersistentCount++;
                        }
                    }
                }
            }
            catch
            {
                if (SandBufferLifetime.IsCreated(newCurrent))
                {
                    newCurrent.Dispose();
                }

                if (SandBufferLifetime.IsCreated(newNext))
                {
                    newNext.Dispose();
                }

                if (SandBufferLifetime.IsCreated(newEngine))
                {
                    newEngine.Dispose();
                }

                if (SandBufferLifetime.IsCreated(newReconciliationQueue))
                {
                    newReconciliationQueue.Dispose();
                }

                if (SandBufferLifetime.IsCreated(newViewBuffer))
                {
                    newViewBuffer.Dispose();
                }

                throw;
            }

            DisposeBuffers();
            _current = newCurrent;
            _next = newNext;
            _engine = newEngine;
            _reconciliationQueue = newReconciliationQueue;
            _viewBuffer = newViewBuffer;
            _width = newWidth;
            _height = newHeight;
            _sandCount = newSandCount;
            _persistentGravityCount = newPersistentCount;

            _viewVersion = -1;
            _stateVersion++;
        }

        public void ClearGrid()
        {
            for (int index = 0; index < _current.Length; index++)
            {
                _current[index] = 0;
            }

            _sandCount = 0;
            _persistentGravityCount = 0;
            _stateVersion++;
        }

        public int CountParticles()
        {
            int count = 0;
            for (int index = 0; index < _current.Length; index++)
            {
                if (_current[index] != 0)
                {
                    count++;
                }
            }

            _particleCount = count;
            return _particleCount;
        }

        public int CountSand()
        {
            int count = 0;
            for (int index = 0; index < _current.Length; index++)
            {
                if ((_current[index] & SandAlgorithmCore.TypeMask)
                    == (int)ElementType.Sand)
                {
                    count++;
                }
            }

            _sandCount = count;
            return _sandCount;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _regionalState?.Dispose();
            DisposeBuffers();
        }

        private void Allocate(int width, int height)
        {
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
            int cellCount = SandAlgorithmCore.CalculateCellCount(_width, _height);
            try
            {
                _current = new NativeArray<int>(cellCount, Allocator.Persistent);
                _next = new NativeArray<int>(cellCount, Allocator.Persistent);
                _engine = new NativeArray<int>(cellCount, Allocator.Persistent);
                _reconciliationQueue = new NativeArray<int>(
                    cellCount,
                    Allocator.Persistent
                );
                _viewBuffer = new NativeArray<SandViewCell>(
                    cellCount,
                    Allocator.Persistent
                );
            }
            catch
            {
                DisposeBuffers();
                throw;
            }
        }

        private void CompleteStepTiming(long startTimestamp)
        {
            _lastTickMs = (Stopwatch.GetTimestamp() - startTimestamp)
                * 1000d
                / Stopwatch.Frequency;
        }

        private void UpdateSandCount(int currentCell, int nextCell)
        {
            _persistentGravityCount += (SandCell.HasPersistentGravity(nextCell) ? 1 : 0)
                - (SandCell.HasPersistentGravity(currentCell) ? 1 : 0);
            bool wasSand = (currentCell & SandAlgorithmCore.TypeMask)
                == (int)ElementType.Sand;
            bool isSand = (nextCell & SandAlgorithmCore.TypeMask)
                == (int)ElementType.Sand;
            if (wasSand == isSand) return;

            _sandCount += isSand ? 1 : -1;
        }

        private void SwapBuffers()
        {
            NativeArray<int> swap = _current;
            _current = _next;
            _next = swap;
        }

        private void RotateResolvedBuffers()
        {
            NativeArray<int> previousCurrent = _current;
            _current = _engine;
            _engine = _next;
            _next = previousCurrent;
        }

        private void DisposeBuffers()
        {
            if (SandBufferLifetime.IsCreated(_current))
            {
                _current.Dispose();
            }

            if (SandBufferLifetime.IsCreated(_next))
            {
                _next.Dispose();
            }

            if (SandBufferLifetime.IsCreated(_engine))
            {
                _engine.Dispose();
            }

            if (SandBufferLifetime.IsCreated(_reconciliationQueue))
            {
                _reconciliationQueue.Dispose();
            }

            if (SandBufferLifetime.IsCreated(_viewBuffer))
            {
                _viewBuffer.Dispose();
            }
        }
    }
}
#endif
