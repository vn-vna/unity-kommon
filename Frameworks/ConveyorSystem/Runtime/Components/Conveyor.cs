using System;
using UnityEngine;
using Unity.Profiling;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Scheherazade/Conveyor System/Conveyor")]
    public sealed class Conveyor : MonoBehaviour
    {
        #region Properties
        public string Id => id;
        public ConveyorDefinition Definition => definition;
        public ConveyorConfiguration Configuration => configuration;
        public ConveyorRuntime Runtime { get; private set; }
        public bool IsInitialized => Runtime != null && !Runtime.IsDisposed;
        public double LastRenderCpuMilliseconds { get; private set; }
        public int LastRenderFrame { get; private set; } = -1;
        public int? LastRenderBatches => _view is PaletteInstancedPayloadView palette ? palette.LastDrawCalls : (int?)null;
        public long? RenderStagingBytes => _view is PaletteInstancedPayloadView palette ? palette.StagingPayloadBytes : (long?)null;
        #endregion

        #region Serialized Fields
        [SerializeField] private string id = "conveyor-a";
        [SerializeField] private ConveyorDefinition definition;
        [SerializeField] private ConveyorConfiguration configuration;
        #endregion

        #region Private Fields
        private ConveyorBuildResult _build;
        private IConveyorPayloadView _view;
        private static readonly ProfilerMarker RenderMarker = new ProfilerMarker("Conveyor.PrepareAndSubmitInstances");
        #endregion

        #region Unity Callbacks
        private void OnEnable() { RequestSuspension(false); }
        private void OnDisable() { RequestSuspension(true); }

        private void OnDestroy()
        {
            if (Runtime != null && !Runtime.World.IsDisposed && Runtime.World.IsBusy)
            {
                ConveyorRuntime runtime = Runtime;
                // Engine destruction cannot be vetoed. Enforce RequireEmpty through Shutdown before Destroy instead.
                if (!runtime.World.TryEnqueue(world =>
                {
                    if (!runtime.IsDisposed) world.RemoveConveyor(runtime, ConveyorShutdownDisposition.Discard);
                })) throw new InvalidOperationException("Conveyor destruction could not queue its cargo disposition.");
                ReleaseVisuals();
                Runtime = null;
                return;
            }
            Shutdown(ConveyorShutdownDisposition.Discard);
        }
        #endregion

        #region Public Methods
        public void Configure(string conveyorId, ConveyorDefinition route, ConveyorConfiguration settings)
        {
            if (IsInitialized) throw new InvalidOperationException("Shut down the owner before changing its bindings.");
            if (string.IsNullOrWhiteSpace(conveyorId)) throw new ArgumentException("Conveyor ID is required.");
            id = conveyorId;
            definition = route;
            configuration = settings;
        }

        public void Initialize(ConveyorWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (definition == null || configuration == null) throw new ArgumentException("Assign a conveyor definition and configuration.");
            if (IsInitialized && (Runtime.World != world || Runtime.Count != 0))
                throw new InvalidOperationException("Rebuild requires an empty conveyor in its existing world.");
            configuration.Validate();
            ConveyorBuildResult candidate = null;
            IConveyorPayloadView candidateView = null;
            try
            {
                candidate = ConveyorManufacturer.Build(definition.Snapshot(), configuration.PathProvider,
                    configuration.GeometryProvider, transform, configuration.Width, configuration.Channels);
                var viewContext = new ConveyorViewContext(transform, world.Capacity);
                candidateView = configuration.PayloadViewProvider?.Create(viewContext);
                ConveyorRuntime runtime = world.CreateConveyor(id, candidate.Path, configuration.TransportProvider,
                    configuration.Channels, configuration.Width, configuration.GetModuleFactories(), IsInitialized);
                ReleaseVisuals();
                Runtime = runtime;
                _build = candidate;
                _view = candidateView;
                candidate = null;
                candidateView = null;
                _build.Activate();
                world.SetSuspended(runtime, !isActiveAndEnabled);
            }
            catch { candidateView?.Dispose(); candidate?.Dispose(); throw; }
        }

        public void Render()
        {
            LastRenderCpuMilliseconds = 0;
            LastRenderFrame = Time.frameCount;
            if (!isActiveAndEnabled || !IsInitialized) return;
            long start = Stopwatch.GetTimestamp();
            try { using (RenderMarker.Auto()) _view?.Render(Runtime.Frame); }
            finally { LastRenderCpuMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency; }
        }

        public void Shutdown(ConveyorShutdownDisposition disposition = ConveyorShutdownDisposition.RequireEmpty)
        {
            if (Runtime != null && !Runtime.World.IsDisposed && !Runtime.IsDisposed)
                Runtime.World.RemoveConveyor(Runtime, disposition);
            Runtime = null;
            ReleaseVisuals();
        }
        #endregion

        #region Private Methods
        private void ReleaseVisuals()
        {
            _view?.Dispose();
            _view = null;
            _build?.Dispose();
            _build = null;
        }

        private void RequestSuspension(bool suspended)
        {
            if (!IsInitialized || Runtime.World.IsDisposed) return;
            ConveyorRuntime runtime = Runtime;
            if (!runtime.World.IsBusy) runtime.World.SetSuspended(runtime, suspended);
            else if (!runtime.World.TryEnqueue(world =>
            {
                if (!runtime.IsDisposed) world.SetSuspended(runtime, suspended);
            })) throw new InvalidOperationException("Conveyor lifecycle command queue is full.");
        }
        #endregion
    }
}
