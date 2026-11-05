#if !PLA_CREATIVE_BUILD
using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>
    /// Compute-shader backend using source, destination, and movement-resolution
    /// integer buffers. CPU state is synchronized lazily for the compatibility API.
    /// </summary>
    public sealed class GpuBackend : ISandBackend, ISandViewBackend, IRegionalSandBackend
    {
        private const string ClearKernelName = "ClearEngine";
        private const string ResolveKernelName = "ResolveMovement";
        private const string ResolveLateralKernelName = "ResolveLateralMixing";
        private const string ResolveCascadeKernelName = "ResolveCascade";
        private const string BuildProjectionKernelName =
            "BuildReconciliationProjection";
        private const string DetectRiskKernelName =
            "DetectReconciliationRisk";
        private const string ReconcileKernelName = "ReconcileIntents";
        private const string CommitKernelName = "CommitMovement";
        private const string ExtractViewKernelName = "ExtractView";
        private const string ClearCountKernelName = "ClearCount";
        private const string CountCellsKernelName = "CountCells";

        private static readonly int CurrentId = Shader.PropertyToID("_Current");
        private static readonly int NextId = Shader.PropertyToID("_Next");
        private static readonly int EngineId = Shader.PropertyToID("_Engine");
        private static readonly int InputIntentsId = Shader.PropertyToID(
            "_InputIntents"
        );
        private static readonly int OutputIntentsId = Shader.PropertyToID(
            "_OutputIntents"
        );
        private static readonly int ViewId = Shader.PropertyToID("_View");
        private static readonly int CountId = Shader.PropertyToID("_Count");
        private static readonly int CellCountId = Shader.PropertyToID("_CellCount");
        private static readonly int WidthId = Shader.PropertyToID("_Width");
        private static readonly int HeightId = Shader.PropertyToID("_Height");
        private static readonly int DisableGravityId = Shader.PropertyToID("_DisableGravity");
        private static readonly int ConstantFallSpeedId = Shader.PropertyToID("_ConstantFallSpeed");
        private static readonly int GravityPerStepId = Shader.PropertyToID("_GravityPerStep");
        private static readonly int MaxFallSpeedId = Shader.PropertyToID("_MaxFallSpeed");
        private static readonly int SlideChanceId = Shader.PropertyToID("_SlideChance");
        private static readonly int PreventTemporaryEdgeVacanciesId =
            Shader.PropertyToID(
                "_PreventTemporaryEdgeVacancies"
            );
        private static readonly int LateralMixChanceId = Shader.PropertyToID(
            "_LateralMixChance"
        );
        private static readonly int MixingSeedId = Shader.PropertyToID("_MixingSeed");
        private static readonly int SimulationSeedId = Shader.PropertyToID(
            "_SimulationSeed"
        );
        private static readonly int GravityXId = Shader.PropertyToID("_GravityX");
        private static readonly int GravityYId = Shader.PropertyToID("_GravityY");
        private static readonly int PreserveExistingMovesId =
            Shader.PropertyToID("_PreserveExistingMoves");
        private static readonly int ReconciliationStatesId = Shader.PropertyToID(
            "_ReconciliationStates"
        );
        private static readonly int ReconciliationQueueId = Shader.PropertyToID(
            "_ReconciliationQueue"
        );
        private static readonly int ViewWordCountId = Shader.PropertyToID("_ViewWordCount");
        private static readonly int CountTypeId = Shader.PropertyToID("_CountType");

        private readonly ComputeShader _shader;
        private readonly int _clearKernel;
        private readonly int _resolveKernel;
        private readonly int _resolveLateralKernel;
        private readonly int _resolveCascadeKernel;
        private readonly int _buildProjectionKernel;
        private readonly int _detectRiskKernel;
        private readonly int _reconcileKernel;
        private readonly int _commitKernel;
        private readonly int _extractViewKernel;
        private readonly int _clearCountKernel;
        private readonly int _countCellsKernel;
        private readonly uint _threadGroupSizeX;
        private readonly Action<AsyncGPUReadbackRequest> _viewReadbackCompleted;

        private ComputeBuffer _currentBuffer;
        private ComputeBuffer _nextBuffer;
        private ComputeBuffer _engineBuffer;
        private ComputeBuffer _reconciliationQueueBuffer;
        private readonly int _compactKernel;
        private ComputeBuffer _gpuViewBuffer;
        private ComputeBuffer _countBuffer;
        private NativeArray<int> _cpuCurrent;
        private NativeArray<SandViewCell> _viewBuffer;
        private int _width;
        private int _height;
        private int _particleCount;
        private int _sandCount;
        private int _persistentGravityCount;
        public bool HasPersistentGravity => _persistentGravityCount > 0;
        private double _lastTickMs;
        private bool _cpuCurrentValid;
        private bool _gpuCurrentValid;
        private bool _disposed;
        private long _stateVersion;
        private long _viewVersion = -1;
        private uint _simulationTick;
        private long _requestedViewVersion = -1;
        private AsyncGPUReadbackRequest _viewReadbackRequest;
        private int _viewReadbackRequestHash;
        private bool _viewReadbackPending;
        private bool _viewReadbackReady;
        private string _viewReadbackError;
        private RegionalSandState _regionalState;
        private NativeArray<int> _regionalNext;
        private NativeArray<int> _regionalIntents;
        private NativeArray<int> _regionalClaims;

        public int Width => _width;
        public int Height => _height;
        public int CellCount => SandAlgorithmCore.CalculateCellCount(_width, _height);
        public NativeArray<SandViewCell> ViewBuffer => _viewBuffer;
        public NativeArray<int> CurrentGrid
        {
            get
            {
                EnsureCpuCurrent();
                return _cpuCurrent;
            }
        }

        public int ParticleCount => _particleCount;
        public double TickMs => _lastTickMs;

        public GpuBackend(int width, int height, ComputeShader shader)
        {
            if (!IsSupported(shader))
            {
                throw new PlatformNotSupportedException(
                    "The sand compute shader is unavailable or unsupported."
                );
            }

            _shader = shader;
            _compactKernel=shader.FindKernel("CompactEligible");
            _viewReadbackCompleted = HandleViewReadbackCompleted;
            _clearKernel = shader.FindKernel(ClearKernelName);
            _resolveKernel = shader.FindKernel(ResolveKernelName);
            _resolveLateralKernel = shader.FindKernel(ResolveLateralKernelName);
            _resolveCascadeKernel = shader.FindKernel(ResolveCascadeKernelName);
            _buildProjectionKernel = shader.FindKernel(
                BuildProjectionKernelName
            );
            _detectRiskKernel = shader.FindKernel(DetectRiskKernelName);
            _reconcileKernel = shader.FindKernel(ReconcileKernelName);
            _commitKernel = shader.FindKernel(CommitKernelName);
            _extractViewKernel = shader.FindKernel(ExtractViewKernelName);
            _clearCountKernel = shader.FindKernel(ClearCountKernelName);
            _countCellsKernel = shader.FindKernel(CountCellsKernelName);
            shader.GetKernelThreadGroupSizes(
                _clearKernel,
                out uint threadGroupSizeX,
                out _,
                out _
            );
            _threadGroupSizeX = threadGroupSizeX;
            Allocate(width, height);
        }

        public static bool IsSupported(ComputeShader shader)
        {
            if (shader == null
                || !SystemInfo.supportsComputeShaders
                || !SystemInfo.supportsAsyncGPUReadback)
            {
                return false;
            }

            if (!shader.HasKernel(ClearKernelName)
                || !shader.HasKernel("CompactEligible")
                || !shader.HasKernel(ResolveKernelName)
                || !shader.HasKernel(ResolveLateralKernelName)
                || !shader.HasKernel(ResolveCascadeKernelName)
                || !shader.HasKernel(BuildProjectionKernelName)
                || !shader.HasKernel(DetectRiskKernelName)
                || !shader.HasKernel(ReconcileKernelName)
                || !shader.HasKernel(CommitKernelName)
                || !shader.HasKernel(ExtractViewKernelName)
                || !shader.HasKernel(ClearCountKernelName)
                || !shader.HasKernel(CountCellsKernelName))
            {
                return false;
            }

            int clearKernel = shader.FindKernel(ClearKernelName);
            int resolveKernel = shader.FindKernel(ResolveKernelName);
            int resolveLateralKernel = shader.FindKernel(ResolveLateralKernelName);
            int resolveCascadeKernel = shader.FindKernel(ResolveCascadeKernelName);
            int buildProjectionKernel = shader.FindKernel(
                BuildProjectionKernelName
            );
            int detectRiskKernel = shader.FindKernel(DetectRiskKernelName);
            int reconcileKernel = shader.FindKernel(ReconcileKernelName);
            int commitKernel = shader.FindKernel(CommitKernelName);
            int extractViewKernel = shader.FindKernel(ExtractViewKernelName);
            int clearCountKernel = shader.FindKernel(ClearCountKernelName);
            int countCellsKernel = shader.FindKernel(CountCellsKernelName);
            return shader.IsSupported(clearKernel)
                && shader.IsSupported(resolveKernel)
                && shader.IsSupported(resolveLateralKernel)
                && shader.IsSupported(resolveCascadeKernel)
                && shader.IsSupported(buildProjectionKernel)
                && shader.IsSupported(detectRiskKernel)
                && shader.IsSupported(reconcileKernel)
                && shader.IsSupported(commitKernel)
                && shader.IsSupported(extractViewKernel)
                && shader.IsSupported(clearCountKernel)
                && shader.IsSupported(countCellsKernel);
        }

        public void Step(StepParams stepParams)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            if (_sandCount == 0)
            {
                _simulationTick++;
                _lastTickMs = (Stopwatch.GetTimestamp() - startTimestamp)
                    * 1000d
                    / Stopwatch.Frequency;
                return;
            }

            if (stepParams.GravityField.IsCreated || HasPersistentGravity)
            {
                StepRegional(stepParams, startTimestamp);
                return;
            }

            EnsureGpuCurrent();
            int simulationSeed = unchecked((int)_simulationTick);
            SandStepSettings settings = SandAlgorithmCore.NormalizeStep(
                stepParams,
                _width,
                _height,
                simulationSeed
            );

            SetShaderParameters(settings);
            if(stepParams.EnableVerticalSettle){_shader.SetBuffer(_compactKernel,"_Current",_currentBuffer);_shader.Dispatch(_compactKernel,CalculateThreadGroupCount(SandAlgorithmCore.GetGravityLineCount(settings)),1,1);}
            BindResolveBuffers();
            int threadGroupCount = CalculateThreadGroupCount(CellCount);
            _shader.Dispatch(_clearKernel, threadGroupCount, 1, 1);
            _shader.Dispatch(_resolveKernel, threadGroupCount, 1, 1);

            ComputeBuffer inputIntents = _engineBuffer;
            ComputeBuffer outputIntents = _nextBuffer;
            int mixingPassCount = SandAlgorithmCore.GetLateralMixPassCount(
                settings
            );
            for (int pass = 0; pass < mixingPassCount; pass++)
            {
                BindLateralMixingBuffers(inputIntents, outputIntents);
                _shader.Dispatch(_resolveLateralKernel, threadGroupCount, 1, 1);
                Swap(ref inputIntents, ref outputIntents);
            }

            if (stepParams.EnableVerticalSettle)
            {
                DispatchCascade(
                    ref inputIntents,
                    ref outputIntents,
                    settings,
                    settings.enableLateralMixing
                );
                BindReconciliationBuffers(inputIntents, outputIntents);
                _shader.Dispatch(
                    _buildProjectionKernel,
                    threadGroupCount,
                    1,
                    1
                );
                int lineCount = SandAlgorithmCore.GetGravityLineCount(settings);
                _shader.Dispatch(
                    _detectRiskKernel,
                    CalculateThreadGroupCount(lineCount),
                    1,
                    1
                );
                _shader.Dispatch(_reconcileKernel, 1, 1, 1);
            }

            BindCommitBuffers(inputIntents, outputIntents);
            _shader.Dispatch(_commitKernel, threadGroupCount, 1, 1);
            RotateResolvedBuffers(inputIntents, outputIntents);
            CompleteStep(startTimestamp);
        }

        private void DispatchCascade(
            ref ComputeBuffer inputIntents,
            ref ComputeBuffer outputIntents,
            SandStepSettings settings,
            bool preserveExistingMoves
        )
        {
            _shader.SetInt(
                PreserveExistingMovesId,
                preserveExistingMoves ? 1 : 0
            );
            BindCascadeBuffers(inputIntents, outputIntents);
            int lineCount = SandAlgorithmCore.GetGravityLineCount(settings);
            _shader.Dispatch(
                _resolveCascadeKernel,
                CalculateThreadGroupCount(lineCount),
                1,
                1
            );
            Swap(ref inputIntents, ref outputIntents);
        }

        private void StepRegional(StepParams stepParams, long startTimestamp)
        {
            // The compute kernels assume one global gravity. Keep mixed fields
            // on the shared Burst CPU solver until a field-aware GPU kernel exists.
            EnsureCpuCurrent();
            _regionalState ??= new RegionalSandState();
            if (!_regionalNext.IsCreated) _regionalNext = new NativeArray<int>(CellCount, Allocator.Persistent);
            if (!_regionalIntents.IsCreated) _regionalIntents = new NativeArray<int>(CellCount, Allocator.Persistent);
            if (!_regionalClaims.IsCreated) _regionalClaims = new NativeArray<int>(CellCount, Allocator.Persistent);
            new RegionalSandStepJob
            {
                current = _cpuCurrent, next = _regionalNext, intents = _regionalIntents, claims = _regionalClaims,
                gravity = _regionalState.Synchronize(stepParams.GravityField.Cells, CellCount),
                settings = SandAlgorithmCore.NormalizeStep(stepParams, _width, _height, unchecked((int)_simulationTick))
            }.Run();
            _persistentGravityCount = _regionalClaims[0];
            NativeArray<int> previous = _cpuCurrent;
            _cpuCurrent = _regionalNext;
            _regionalNext = previous;
            _cpuCurrentValid = true;
            _gpuCurrentValid = false;
            _stateVersion++;
            _simulationTick++;
            _lastTickMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private void CompleteStep(long startTimestamp)
        {
            _cpuCurrentValid = false;
            _gpuCurrentValid = true;
            _stateVersion++;
            _simulationTick++;
            _lastTickMs = (Stopwatch.GetTimestamp() - startTimestamp)
                * 1000d
                / Stopwatch.Frequency;
        }

        public void SetCell(int index, int value)
        {
            EnsureCpuCurrent();
            int currentCell = _cpuCurrent[index];
            int nextCell = SandAlgorithmCore.CanonicalizeCell(value);
            UpdateSandCount(currentCell, nextCell);
            _cpuCurrent[index] = nextCell;
            _gpuCurrentValid = false;
            _stateVersion++;
        }

        public int GetCell(int index)
        {
            EnsureCpuCurrent();
            return _cpuCurrent[index];
        }

        public bool RefreshViewBuffer()
        {
            if (_viewReadbackError != null)
            {
                string error = _viewReadbackError;
                _viewReadbackError = null;
                throw new InvalidOperationException(error);
            }

            bool viewChanged = _viewReadbackReady;
            _viewReadbackReady = false;
            if (!_viewReadbackPending && _viewVersion != _stateVersion)
            {
                RequestViewReadback();
            }

            return viewChanged;
        }

        private void RequestViewReadback()
        {
            EnsureGpuCurrent();
            int viewWordCount = CalculateViewWordCount();
            _shader.SetInt(CellCountId, CellCount);
            _shader.SetInt(ViewWordCountId, viewWordCount);
            _shader.SetBuffer(_extractViewKernel, CurrentId, _currentBuffer);
            _shader.SetBuffer(_extractViewKernel, ViewId, _gpuViewBuffer);
            _shader.Dispatch(
                _extractViewKernel,
                CalculateThreadGroupCount(viewWordCount),
                1,
                1
            );

            _requestedViewVersion = _stateVersion;
            _viewReadbackPending = true;
            try
            {
                _viewReadbackRequest = AsyncGPUReadback.Request(
                    _gpuViewBuffer,
                    _viewReadbackCompleted
                );
                _viewReadbackRequestHash = _viewReadbackRequest.GetHashCode();
            }
            catch
            {
                _viewReadbackPending = false;
                throw;
            }
        }

        private void HandleViewReadbackCompleted(AsyncGPUReadbackRequest request)
        {
            if (!_viewReadbackPending
                || request.GetHashCode() != _viewReadbackRequestHash)
            {
                return;
            }

            _viewReadbackPending = false;
            if (_disposed)
            {
                return;
            }

            if (request.hasError)
            {
                _viewReadbackError = "Failed to read the sand view buffer from the GPU.";
                return;
            }

            try
            {
                NativeArray<SandViewCell> readbackData =
                    request.GetData<SandViewCell>();
                NativeArray<SandViewCell>.Copy(
                    readbackData,
                    0,
                    _viewBuffer,
                    0,
                    CellCount
                );
                _viewVersion = _requestedViewVersion;
                _viewReadbackReady = true;
            }
            catch (Exception exception)
            {
                _viewReadbackError = "Failed to publish the sand GPU view: "
                    + exception.Message;
            }
        }

        public void Resize(int newWidth, int newHeight)
        {
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);
            if (newWidth == _width && newHeight == _height)
            {
                return;
            }

            EnsureCpuCurrent();
            int oldWidth = _width;
            int oldHeight = _height;
            int newCellCount = SandAlgorithmCore.CalculateCellCount(newWidth, newHeight);
            ComputeBuffer newCurrentBuffer = null;
            ComputeBuffer newNextBuffer = null;
            ComputeBuffer newEngineBuffer = null;
            ComputeBuffer newReconciliationQueueBuffer = null;
            ComputeBuffer newGpuViewBuffer = null;
            ComputeBuffer newCountBuffer = null;
            NativeArray<int> newCpuCurrent = default;
            NativeArray<SandViewCell> newViewBuffer = default;
            int newSandCount = 0;
            int newPersistentCount = 0;
            try
            {
                newCurrentBuffer = new ComputeBuffer(newCellCount, sizeof(int));
                newNextBuffer = new ComputeBuffer(newCellCount, sizeof(int));
                newEngineBuffer = new ComputeBuffer(newCellCount, sizeof(int));
                newReconciliationQueueBuffer = new ComputeBuffer(
                    newCellCount,
                    sizeof(int)
                );
                newGpuViewBuffer = new ComputeBuffer(
                    CalculateViewWordCount(newCellCount),
                    sizeof(uint)
                );
                newCountBuffer = new ComputeBuffer(1, sizeof(int));
                newCpuCurrent = new NativeArray<int>(
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
                        int cell = _cpuCurrent[
                            sourceY * oldWidth + sourceX
                        ];
                        newCpuCurrent[y * newWidth + x] = cell;
                        if ((cell & SandAlgorithmCore.TypeMask)
                            == (int)ElementType.Sand)
                        {
                            newSandCount++;
                            if (SandCell.HasPersistentGravity(cell)) newPersistentCount++;
                        }
                    }
                }

                newCurrentBuffer.SetData(newCpuCurrent);
            }
            catch
            {
                newCurrentBuffer?.Dispose();
                newNextBuffer?.Dispose();
                newEngineBuffer?.Dispose();
                newReconciliationQueueBuffer?.Dispose();
                newGpuViewBuffer?.Dispose();
                newCountBuffer?.Dispose();
                if (newCpuCurrent.IsCreated)
                {
                    newCpuCurrent.Dispose();
                }

                if (newViewBuffer.IsCreated)
                {
                    newViewBuffer.Dispose();
                }

                throw;
            }

            DisposeBuffers();
            _currentBuffer = newCurrentBuffer;
            _nextBuffer = newNextBuffer;
            _engineBuffer = newEngineBuffer;
            _reconciliationQueueBuffer = newReconciliationQueueBuffer;
            _gpuViewBuffer = newGpuViewBuffer;
            _countBuffer = newCountBuffer;
            _cpuCurrent = newCpuCurrent;
            _viewBuffer = newViewBuffer;
            _width = newWidth;
            _height = newHeight;
            _sandCount = newSandCount;
            _persistentGravityCount = newPersistentCount;
            _cpuCurrentValid = true;
            _gpuCurrentValid = true;
            _viewVersion = -1;
            _requestedViewVersion = -1;
            _viewReadbackReady = false;
            _viewReadbackError = null;
            _stateVersion++;
        }

        public void ClearGrid()
        {
            for (int index = 0; index < _cpuCurrent.Length; index++)
            {
                _cpuCurrent[index] = 0;
            }

            _sandCount = 0;
            _persistentGravityCount = 0;
            _cpuCurrentValid = true;
            _gpuCurrentValid = false;
            _stateVersion++;
        }

        public int CountParticles()
        {
            _particleCount = CountCells(false);
            return _particleCount;
        }

        public int CountSand()
        {
            _sandCount = CountCells(true);
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

            _disposed = true;
            DisposeBuffers();
        }

        private void Allocate(int width, int height)
        {
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
            int cellCount = SandAlgorithmCore.CalculateCellCount(_width, _height);
            try
            {
                _currentBuffer = new ComputeBuffer(cellCount, sizeof(int));
                _nextBuffer = new ComputeBuffer(cellCount, sizeof(int));
                _engineBuffer = new ComputeBuffer(cellCount, sizeof(int));
                _reconciliationQueueBuffer = new ComputeBuffer(
                    cellCount,
                    sizeof(int)
                );
                _gpuViewBuffer = new ComputeBuffer(
                    CalculateViewWordCount(cellCount),
                    sizeof(uint)
                );
                _countBuffer = new ComputeBuffer(1, sizeof(int));
                _cpuCurrent = new NativeArray<int>(cellCount, Allocator.Persistent);
                _viewBuffer = new NativeArray<SandViewCell>(
                    cellCount,
                    Allocator.Persistent
                );
                _currentBuffer.SetData(_cpuCurrent);
                _cpuCurrentValid = true;
                _gpuCurrentValid = true;
                _viewVersion = -1;
                _requestedViewVersion = -1;
                _viewReadbackReady = false;
                _viewReadbackError = null;
            }
            catch
            {
                DisposeBuffers();
                throw;
            }
        }

        private void SetShaderParameters(SandStepSettings settings)
        {
            _shader.SetInt(CellCountId, CellCount);
            _shader.SetInt(WidthId, settings.width);
            _shader.SetInt(HeightId, settings.height);
            _shader.SetInt(DisableGravityId, settings.disableGravity ? 1 : 0);
            _shader.SetInt(ConstantFallSpeedId, settings.constantFallSpeed);
            _shader.SetInt(GravityPerStepId, settings.gravityPerStep);
            _shader.SetInt(MaxFallSpeedId, settings.maxFallSpeed);
            _shader.SetInt(SlideChanceId, settings.slideChance);
            _shader.SetInt("_EnableVerticalSettle", settings.enableVerticalSettle ? 1 : 0);
            _shader.SetInt(
                PreventTemporaryEdgeVacanciesId,
                settings.preventTemporaryEdgeVacancies ? 1 : 0
            );
            _shader.SetInt(LateralMixChanceId, settings.lateralMixChance);
            _shader.SetInt(MixingSeedId, settings.mixingSeed);
            _shader.SetInt(SimulationSeedId, settings.simulationSeed);
            _shader.SetInt(GravityXId, settings.gravityX);
            _shader.SetInt(GravityYId, settings.gravityY);
            _shader.SetInt(ViewWordCountId, CalculateViewWordCount());
        }

        private void BindResolveBuffers()
        {
            _shader.SetBuffer(_clearKernel, EngineId, _engineBuffer);
            _shader.SetBuffer(_resolveKernel, CurrentId, _currentBuffer);
            _shader.SetBuffer(_resolveKernel, EngineId, _engineBuffer);
        }

        private void BindLateralMixingBuffers(
            ComputeBuffer inputIntents,
            ComputeBuffer outputIntents
        )
        {
            _shader.SetBuffer(_resolveLateralKernel, CurrentId, _currentBuffer);
            _shader.SetBuffer(
                _resolveLateralKernel,
                InputIntentsId,
                inputIntents
            );
            _shader.SetBuffer(
                _resolveLateralKernel,
                OutputIntentsId,
                outputIntents
            );
        }

        private void BindCascadeBuffers(
            ComputeBuffer inputIntents,
            ComputeBuffer outputIntents
        )
        {
            _shader.SetBuffer(_resolveCascadeKernel, CurrentId, _currentBuffer);
            _shader.SetBuffer(
                _resolveCascadeKernel,
                InputIntentsId,
                inputIntents
            );
            _shader.SetBuffer(
                _resolveCascadeKernel,
                OutputIntentsId,
                outputIntents
            );
        }

        private void BindCommitBuffers(
            ComputeBuffer finalIntents,
            ComputeBuffer destination
        )
        {
            _shader.SetBuffer(_commitKernel, CurrentId, _currentBuffer);
            _shader.SetBuffer(_commitKernel, NextId, destination);
            _shader.SetBuffer(_commitKernel, InputIntentsId, finalIntents);
        }

        private void BindReconciliationBuffers(
            ComputeBuffer finalIntents,
            ComputeBuffer projectedStates
        )
        {
            _shader.SetBuffer(
                _buildProjectionKernel,
                CurrentId,
                _currentBuffer
            );
            _shader.SetBuffer(
                _buildProjectionKernel,
                InputIntentsId,
                finalIntents
            );
            _shader.SetBuffer(
                _buildProjectionKernel,
                ReconciliationStatesId,
                projectedStates
            );
            _shader.SetBuffer(
                _detectRiskKernel,
                CurrentId,
                _currentBuffer
            );
            _shader.SetBuffer(
                _detectRiskKernel,
                ReconciliationStatesId,
                projectedStates
            );
            _shader.SetBuffer(
                _detectRiskKernel,
                ReconciliationQueueId,
                _reconciliationQueueBuffer
            );
            _shader.SetBuffer(_reconcileKernel, CurrentId, _currentBuffer);
            _shader.SetBuffer(
                _reconcileKernel,
                InputIntentsId,
                finalIntents
            );
            _shader.SetBuffer(
                _reconcileKernel,
                ReconciliationStatesId,
                projectedStates
            );
            _shader.SetBuffer(
                _reconcileKernel,
                ReconciliationQueueId,
                _reconciliationQueueBuffer
            );
        }

        private int CountCells(bool sandOnly)
        {
            EnsureGpuCurrent();
            _shader.SetInt(CellCountId, CellCount);
            _shader.SetInt(CountTypeId, sandOnly ? 1 : 0);
            _shader.SetBuffer(_clearCountKernel, CountId, _countBuffer);
            _shader.SetBuffer(_countCellsKernel, CurrentId, _currentBuffer);
            _shader.SetBuffer(_countCellsKernel, CountId, _countBuffer);
            _shader.Dispatch(_clearCountKernel, 1, 1, 1);
            _shader.Dispatch(
                _countCellsKernel,
                CalculateThreadGroupCount(CellCount),
                1,
                1
            );

            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(_countBuffer);
            request.WaitForCompletion();
            if (request.hasError)
            {
                throw new InvalidOperationException(
                    "Failed to read the sand particle count from the GPU."
                );
            }

            return request.GetData<int>()[0];
        }

        private int CalculateThreadGroupCount(int elementCount)
        {
            return (int)(((uint)elementCount + _threadGroupSizeX - 1)
                / _threadGroupSizeX);
        }

        private int CalculateViewWordCount()
        {
            return CalculateViewWordCount(CellCount);
        }

        private static int CalculateViewWordCount(int cellCount)
        {
            return (int)(((long)cellCount + 1L) / 2L);
        }

        private void EnsureCpuCurrent()
        {
            ThrowIfDisposed();
            if (_cpuCurrentValid)
            {
                return;
            }

            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(_currentBuffer);
            request.WaitForCompletion();
            if (request.hasError)
            {
                throw new InvalidOperationException(
                    "Failed to read the current sand grid from the GPU."
                );
            }

            NativeArray<int> readbackData = request.GetData<int>();
            NativeArray<int>.Copy(readbackData, _cpuCurrent);
            _cpuCurrentValid = true;
        }

        private void EnsureGpuCurrent()
        {
            ThrowIfDisposed();
            if (_gpuCurrentValid)
            {
                return;
            }

            _currentBuffer.SetData(_cpuCurrent);
            _gpuCurrentValid = true;
        }

        private void RotateResolvedBuffers(
            ComputeBuffer finalIntents,
            ComputeBuffer destination
        )
        {
            ComputeBuffer previousCurrent = _currentBuffer;
            _currentBuffer = destination;
            _engineBuffer = finalIntents;
            _nextBuffer = previousCurrent;
        }

        private static void Swap(
            ref ComputeBuffer first,
            ref ComputeBuffer second
        )
        {
            ComputeBuffer swap = first;
            first = second;
            second = swap;
        }

        private void DisposeBuffers()
        {
            WaitForPendingViewReadback();
            _regionalState?.Dispose();
            _regionalState = null;
            if (_regionalNext.IsCreated) _regionalNext.Dispose();
            if (_regionalIntents.IsCreated) _regionalIntents.Dispose();
            if (_regionalClaims.IsCreated) _regionalClaims.Dispose();
            _currentBuffer?.Dispose();
            _nextBuffer?.Dispose();
            _engineBuffer?.Dispose();
            _reconciliationQueueBuffer?.Dispose();
            _gpuViewBuffer?.Dispose();
            _countBuffer?.Dispose();
            _currentBuffer = null;
            _nextBuffer = null;
            _engineBuffer = null;
            _reconciliationQueueBuffer = null;
            _gpuViewBuffer = null;
            _countBuffer = null;

            if (_cpuCurrent.IsCreated)
            {
                _cpuCurrent.Dispose();
            }

            if (_viewBuffer.IsCreated)
            {
                _viewBuffer.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GpuBackend));
            }
        }

        private void WaitForPendingViewReadback()
        {
            if (!_viewReadbackPending)
            {
                return;
            }

            _viewReadbackRequest.WaitForCompletion();
            if (_viewReadbackPending)
            {
                HandleViewReadbackCompleted(_viewReadbackRequest);
            }
        }
    }
}
#endif
