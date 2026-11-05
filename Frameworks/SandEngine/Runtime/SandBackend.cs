using System;
using System.Diagnostics;
using Com.Scheherazade.Common.Frameworks.GridSystem;
#if PLA_CREATIVE_BUILD
using SandGridBuffer = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaBuffer<int>;
using SandViewBuffer = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaBuffer<Com.Scheherazade.Common.Frameworks.SandEngine.SandViewCell>;
#else
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using SandGridBuffer = Unity.Collections.NativeArray<int>;
using SandViewBuffer = Unity.Collections.NativeArray<Com.Scheherazade.Common.Frameworks.SandEngine.SandViewCell>;
#endif
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    public static class SandBufferLifetime
    {
#if PLA_CREATIVE_BUILD
        public static bool IsCreated<T>(Pla.SandPlaBuffer<T> buffer) where T : struct
#else
        public static bool IsCreated<T>(NativeArray<T> buffer) where T : struct
#endif
        {
#if PLA_CREATIVE_BUILD
            return buffer != null && buffer.IsCreated;
#else
            return buffer.IsCreated;
#endif
        }
    }

    /// <summary>
    /// The compute contract for a sand grid. Implementations own the grid buffers
    /// and run one simulation step. Physics parameters are passed explicitly per
    /// step, so a backend has no configuration coupling and can be swapped
    /// (Burst jobs, GPU compute, a pure-C# test harness, ...).
    /// </summary>
    public interface ISandBackend : IDisposable
    {
        int Width { get; }
        int Height { get; }
        int CellCount { get; }

        /// <summary>Runs one resolve, optional dependency passes, commit, and swap tick.</summary>
        void Step(StepParams stepParams);

        /// <summary>Two-byte render cells containing element type and palette address.</summary>
        SandViewBuffer ViewBuffer { get; }

        /// <summary>The live 32-bit grid (read-only by convention) used for render extraction.</summary>
        SandGridBuffer CurrentGrid { get; }

        /// <summary>Last counted particle total (refreshed by <see cref="CountParticles"/>).</summary>
        int ParticleCount { get; }

        /// <summary>
        /// CPU wall-clock time of the last step. Asynchronous backends report
        /// dispatch submission time rather than GPU completion time.
        /// Scheduled Jobs report schedule-to-completion latency; overlapping
        /// pools' values must not be summed. Profile StepAll for the total barrier.
        /// </summary>
        double TickMs { get; }

        // Main-thread grid access (DrawStamp, Clear, Reseed).
        void SetCell(int index, int value);
        int GetCell(int index);

        /// <summary>Reallocates every buffer at the new dimensions, downsampling content.</summary>
        void Resize(int newWidth, int newHeight);

        /// <summary>Zeroes the whole current grid.</summary>
        void ClearGrid();

        /// <summary>Counts non-empty cells into <see cref="ParticleCount"/>.</summary>
        int CountParticles();

        /// <summary>Counts only cells whose element type is <see cref="ElementType.Sand"/>.</summary>
        int CountSand();
    }

    /// <summary>
    /// Optional two-phase execution. Complete before publishing step callbacks or
    /// retaining grid buffers. Synchronous Step remains supported by every backend.
    /// </summary>
    public interface IScheduledSandBackend
    {
        void ScheduleStep(StepParams stepParams);
        void CompleteStep();
    }

    /// <summary>Optional optimized view extraction implemented by built-in backends.</summary>
    public interface ISandViewBackend
    {
        /// <summary>Returns whether consumers need to upload the refreshed view.</summary>
        bool RefreshViewBuffer();
    }

    /// <summary>Per-step value snapshot derived from <see cref="ISandEngineSettings"/>.</summary>
    public struct StepParams
    {
        // Immutable engine-owned snapshot; backends copy only when it changes.
        public SandGravityFieldSnapshot GravityField;
        /// <summary>When true, sand uses ConstantFallSpeed rather than acceleration.</summary>
        public bool DisableGravity;
        public int ConstantFallSpeed;
        public int GravityPerStep;
        public int MaxFallSpeed;
        public int SlideChance;          // = (1 - friction) * 65536
        public bool EnableVerticalSettle;
        public int SettleBatchSize;
        public bool PreventTemporaryEdgeVacancies;
        public bool EnableLateralMixing;
        public int LateralMixChance;
        public int LateralMixPasses;
        public int MixingSeed;
        public int UpdateBatchSize;
        public GridCoord GravityDirection;   // Cardinal direction; (0,1) is the default down.
    }

    /// <summary>
    /// Burst job backend with three cell-sized integer buffers: an immutable source
    /// grid, a write-only destination grid, and a movement-resolution grid. A tick
    /// resolves direct and dependent cascade moves before committing cell writes.
    /// </summary>
#if !PLA_CREATIVE_BUILD
    public class JobBackend : ISandBackend, ISandViewBackend, IScheduledSandBackend, IRegionalSandBackend
    {
        // Keep the production policy scheduled until a target-device capture
        // establishes a useful synchronous cutoff. Explicit modes are diagnostic.
        private const int DirectViewRefreshCellLimit = 0;
        private static readonly ProfilerMarker ViewRefreshMarker = new ProfilerMarker("SandRendering.ViewRefresh");
        private static readonly ProfilerMarker DirectViewMarker = new ProfilerMarker("SandRendering.ViewExtract.Synchronous");
        private static readonly ProfilerMarker ScheduledViewMarker = new ProfilerMarker("SandRendering.ViewExtract.Scheduled");

        internal enum ViewRefreshExecution
        {
            Automatic,
            Synchronous,
            Scheduled
        }

        private NativeArray<int> _current;
        private NativeArray<int> _next;
        private NativeArray<int> _engine;
        private NativeArray<int> _reconciliationQueue;
        private NativeArray<int> _countBuffer;
        private NativeArray<SandViewCell> _viewBuffer;

        private int _width;
        private int _height;
        private double _lastTickMs;
        private int _particleCount;
        private int _sandCount;
        private int _persistentGravityCount;
        private bool _regionalStepPending;
        public bool HasPersistentGravity => _persistentGravityCount > 0;
        private bool _disposed;
        private long _stateVersion;
        private long _viewVersion = -1;
        private uint _simulationTick;
        private JobHandle _pendingHandle;
        private NativeArray<int> _pendingIntents;
        private NativeArray<int> _pendingDestination;
        private bool _stepPending;
        private long _stepStartTimestamp;
        private RegionalSandState _regionalState;

        public JobBackend(int width, int height)
        {
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
            try
            {
                _current = new NativeArray<int>(CellCount, Allocator.Persistent);
                _next = new NativeArray<int>(CellCount, Allocator.Persistent);
                _engine = new NativeArray<int>(CellCount, Allocator.Persistent);
                _reconciliationQueue = new NativeArray<int>(CellCount, Allocator.Persistent);
                _countBuffer = new NativeArray<int>(1, Allocator.Persistent);
                _viewBuffer = new NativeArray<SandViewCell>(CellCount, Allocator.Persistent);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public int Width => _width;
        public int Height => _height;
        public int CellCount => SandAlgorithmCore.CalculateCellCount(_width, _height);
        public NativeArray<SandViewCell> ViewBuffer => _viewBuffer;
        public NativeArray<int> CurrentGrid
        {
            get
            {
                CompleteStep();
                return _current;
            }
        }
        public int ParticleCount => _particleCount;
        public double TickMs => _lastTickMs;

        public void Step(StepParams stepParams)
        {
            ScheduleStep(stepParams);
            CompleteStep();
        }

        public void ScheduleStep(StepParams stepParams)
        {
            EnsureReady();
            long startTimestamp = Stopwatch.GetTimestamp();
            if (_sandCount == 0)
            {
                _simulationTick++;
                CompleteStepTiming(startTimestamp);
                return;
            }

            _stepStartTimestamp = startTimestamp;
            _pendingHandle = default;
            try
            {
                ScheduleJobs(stepParams);
                _stepPending = true;
            }
            catch
            {
                // A later scheduling failure must not leave earlier jobs using
                // buffers that initialization/teardown may subsequently release.
                _pendingHandle.Complete();
                throw;
            }
        }

        public void CompleteStep()
        {
            if (!_stepPending) return;
            _pendingHandle.Complete();
            if (_regionalStepPending) _persistentGravityCount = _reconciliationQueue[0];
            _regionalStepPending = false;
            RotateResolvedBuffers(_pendingIntents, _pendingDestination);
            _stepPending = false;
            CompleteStepTiming(_stepStartTimestamp);
        }

        private void ScheduleJobs(StepParams stepParams)
        {
            int batchSize = Math.Max(1, stepParams.UpdateBatchSize);
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
                _pendingHandle = new RegionalSandStepJob
                {
                    current = _current, next = _next, intents = _engine, claims = _reconciliationQueue,
                    gravity = _regionalState.Synchronize(stepParams.GravityField.Cells, CellCount), settings = settings
                }.Schedule();
                _pendingIntents = _engine;
                _pendingDestination = _next;
                _regionalStepPending = true;
                return;
            }

            JobHandle clearHandle = new ClearMovementEngineJob
            {
                engine = _engine
            }.Schedule(CellCount, batchSize);
            _pendingHandle = clearHandle;
            if (stepParams.EnableVerticalSettle)
            {
                clearHandle = new CompactEligibleSandJob { grid = _current, settings = settings }
                    .Schedule(SandAlgorithmCore.GetGravityLineCount(settings), Math.Max(1, stepParams.SettleBatchSize), clearHandle);
                _pendingHandle = clearHandle;
            }

            JobHandle resolveHandle = new ResolveSandMovementJob
            {
                current = _current,
                engine = _engine,
                width = _width,
                height = _height,
                disableGravity = settings.disableGravity,
                enableVerticalSettle = settings.enableVerticalSettle,
                constantFallSpeed = settings.constantFallSpeed,
                gravityPerStep = settings.gravityPerStep,
                maxFallSpeed = settings.maxFallSpeed,
                slideChance = settings.slideChance,
                preventTemporaryEdgeVacancies =
                    settings.preventTemporaryEdgeVacancies,
                enableLateralMixing = settings.enableLateralMixing,
                lateralMixChance = settings.lateralMixChance,
                lateralMixPasses = settings.lateralMixPasses,
                mixingSeed = settings.mixingSeed,
                simulationSeed = settings.simulationSeed,
                gravityX = settings.gravityX,
                gravityY = settings.gravityY
            }.Schedule(CellCount, batchSize, clearHandle);
            _pendingHandle = resolveHandle;

            NativeArray<int> inputIntents = _engine;
            NativeArray<int> outputIntents = _next;
            JobHandle resolvedMovementHandle = resolveHandle;
            int mixingPassCount = SandAlgorithmCore.GetLateralMixPassCount(
                settings
            );
            for (int pass = 0; pass < mixingPassCount; pass++)
            {
                resolvedMovementHandle = new ResolveLateralMixingJob
                {
                    current = _current,
                    inputIntents = inputIntents,
                    outputIntents = outputIntents,
                    width = _width,
                    height = _height,
                    lateralMixChance = settings.lateralMixChance,
                    lateralMixPasses = settings.lateralMixPasses,
                    mixingSeed = settings.mixingSeed,
                    simulationSeed = settings.simulationSeed,
                    preventTemporaryEdgeVacancies =
                        settings.preventTemporaryEdgeVacancies,
                    gravityX = settings.gravityX,
                    gravityY = settings.gravityY
                }.Schedule(CellCount, batchSize, resolvedMovementHandle);
                _pendingHandle = resolvedMovementHandle;
                Swap(ref inputIntents, ref outputIntents);
            }

            if (stepParams.EnableVerticalSettle)
            {
                resolvedMovementHandle = ScheduleCascade(
                    inputIntents,
                    outputIntents,
                    settings,
                    stepParams.SettleBatchSize,
                    resolvedMovementHandle,
                    settings.enableLateralMixing
                );
                _pendingHandle = resolvedMovementHandle;
                Swap(ref inputIntents, ref outputIntents);
                JobHandle projectionHandle = new BuildSandProjectedStatesJob
                {
                    current = _current,
                    intents = inputIntents,
                    projectedStates = outputIntents,
                    width = _width,
                    height = _height,
                    gravityX = settings.gravityX,
                    gravityY = settings.gravityY
                }.Schedule(
                    CellCount,
                    batchSize,
                    resolvedMovementHandle
                );
                _pendingHandle = projectionHandle;
                JobHandle riskHandle = new DetectSandReconciliationRiskJob
                {
                    current = _current,
                    projectedStates = outputIntents,
                    riskByLine = _reconciliationQueue,
                    width = _width,
                    height = _height,
                    gravityX = settings.gravityX,
                    gravityY = settings.gravityY
                }.Schedule(
                    SandAlgorithmCore.GetGravityLineCount(settings),
                    Math.Max(1, stepParams.SettleBatchSize),
                    projectionHandle
                );
                _pendingHandle = riskHandle;
                resolvedMovementHandle = new ReconcileSandIntentsJob
                {
                    stepSettings = settings,
                    current = _current,
                    intents = inputIntents,
                    projectedStates = outputIntents,
                    floodQueue = _reconciliationQueue,
                    width = _width,
                    height = _height,
                    gravityX = settings.gravityX,
                    gravityY = settings.gravityY
                }.Schedule(riskHandle);
                _pendingHandle = resolvedMovementHandle;
            }

            JobHandle commitHandle = new CommitSandMovementJob
            {
                current = _current,
                engine = inputIntents,
                next = outputIntents,
                width = _width,
                height = _height,
                gravityX = settings.gravityX,
                gravityY = settings.gravityY
            }.Schedule(
                CellCount,
                batchSize,
                resolvedMovementHandle
            );
            _pendingHandle = commitHandle;
            _pendingIntents = inputIntents;
            _pendingDestination = outputIntents;
        }

        private JobHandle ScheduleCascade(
            NativeArray<int> inputIntents,
            NativeArray<int> outputIntents,
            SandStepSettings settings,
            int batchSize,
            JobHandle dependency,
            bool preserveExistingMoves
        )
        {
            return new ResolveSandCascadeJob
            {
                current = _current,
                inputIntents = inputIntents,
                outputIntents = outputIntents,
                width = _width,
                height = _height,
                gravityX = settings.gravityX,
                gravityY = settings.gravityY,
                preserveExistingMoves = preserveExistingMoves
            }.Schedule(
                SandAlgorithmCore.GetGravityLineCount(settings),
                Math.Max(1, batchSize),
                dependency
            );
        }

        private void RotateResolvedBuffers(
            NativeArray<int> finalIntents,
            NativeArray<int> destination
        )
        {
            NativeArray<int> previousCurrent = _current;
            _current = destination;
            _engine = finalIntents;
            _next = previousCurrent;
            _stateVersion++;
            _simulationTick++;
        }

        private void CompleteStepTiming(long startTimestamp)
        {
            _lastTickMs = (Stopwatch.GetTimestamp() - startTimestamp)
                * 1000f
                / Stopwatch.Frequency;
        }

        private static void Swap(
            ref NativeArray<int> first,
            ref NativeArray<int> second
        )
        {
            NativeArray<int> swap = first;
            first = second;
            second = swap;
        }

        public void SetCell(int index, int value)
        {
            EnsureReady();
            int currentCell = _current[index];
            int nextCell = SandAlgorithmCore.CanonicalizeCell(value);
            if (currentCell == nextCell) return;
            UpdateSandCount(currentCell, nextCell);
            _current[index] = nextCell;
            _stateVersion++;
        }

        public int GetCell(int index)
        {
            EnsureReady();
            return _current[index];
        }

        public bool RefreshViewBuffer()
        {
            return RefreshViewBuffer(ViewRefreshExecution.Automatic);
        }

        internal bool RefreshViewBuffer(ViewRefreshExecution execution)
        {
            using (ViewRefreshMarker.Auto())
            {
                EnsureReady();
                if (_viewVersion == _stateVersion)
                {
                    return false;
                }

                int chunks = RefreshSandViewJob.GetChunkCount(CellCount);
                var job = new RefreshSandViewJob
                {
                    source = _current,
                    dest = _viewBuffer,
                    changedChunks = _reconciliationQueue
                };
                bool direct = execution == ViewRefreshExecution.Synchronous
                    || execution == ViewRefreshExecution.Automatic && CellCount <= DirectViewRefreshCellLimit;
                using ((direct ? DirectViewMarker : ScheduledViewMarker).Auto())
                {
                    if (direct) job.Run(chunks);
                    else job.Schedule(chunks, 1).Complete();
                }
                bool changed = _viewVersion < 0;
                for (int chunk = 0; chunk < chunks; chunk++)
                {
                    changed |= _reconciliationQueue[chunk] != 0;
                }
                _viewVersion = _stateVersion;
                return changed;
            }
        }

        public void Resize(int newWidth, int newHeight)
        {
            EnsureReady();
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);
            if (newWidth == _width && newHeight == _height)
            {
                return;
            }

            int newCellCount = SandAlgorithmCore.CalculateCellCount(newWidth, newHeight);
            int oldWidth = _width;
            int oldHeight = _height;
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

            _current.Dispose();
            _next.Dispose();
            _engine.Dispose();
            _reconciliationQueue.Dispose();
            _viewBuffer.Dispose();
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
            EnsureReady();
            for (int i = 0; i < CellCount; i++)
            {
                _current[i] = 0;
            }

            _sandCount = 0;
            _persistentGravityCount = 0;
            _stateVersion++;
        }

        public int CountParticles()
        {
            EnsureReady();
            _countBuffer[0] = 0;
            new CountElementsJob
            {
                grid = _current,
                count = _countBuffer
            }.Schedule(CellCount, 1024).Complete();
            _particleCount = _countBuffer[0];
            return _particleCount;
        }

        public int CountSand()
        {
            EnsureReady();
            _countBuffer[0] = 0;
            new CountSandElementsJob
            {
                grid = _current,
                count = _countBuffer
            }.Schedule(CellCount, 1024).Complete();
            _sandCount = _countBuffer[0];
            return _sandCount;
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CompleteStep();
            _disposed = true;
            _regionalState?.Dispose();
            if (SandBufferLifetime.IsCreated(_current)) _current.Dispose();
            if (SandBufferLifetime.IsCreated(_next)) _next.Dispose();
            if (SandBufferLifetime.IsCreated(_engine)) _engine.Dispose();
            if (SandBufferLifetime.IsCreated(_reconciliationQueue)) _reconciliationQueue.Dispose();
            if (SandBufferLifetime.IsCreated(_countBuffer)) _countBuffer.Dispose();
            if (SandBufferLifetime.IsCreated(_viewBuffer)) _viewBuffer.Dispose();
        }

        private void EnsureReady()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JobBackend));
            CompleteStep();
        }
    }
#endif
}
