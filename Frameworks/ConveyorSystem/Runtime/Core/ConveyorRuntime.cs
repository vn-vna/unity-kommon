using System;
using System.Collections.Generic;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public sealed class ConveyorRuntime
    {
        #region Properties
        public ConveyorWorld World { get; }
        public string Id { get; }
        public ConveyorPath Path { get; }
        public int Channels { get; }
        public float Width { get; }
        public int Count => IsDisposed ? 0 : Backend.Count;
        public bool IsSuspended { get; internal set; }
        public bool IsDisposed { get; private set; }
        public ConveyorTransportCapabilities Capabilities => Backend.Capabilities;
        public ConveyorTransportFrame Frame => new ConveyorTransportFrame(_frame);
        internal IConveyorTransportBackend Backend { get; }
        internal bool ModuleLifecycle { get; private set; }
        internal readonly ConveyorBlockReason[] Blocks;
        internal readonly bool[] ReportedHolds;
        internal readonly long[] BlockedSince;
        #endregion

        #region Private Fields
        private readonly ConveyorFrameBuffer _frame;
        private readonly ConveyorTransportState[] _scratch;
        private readonly List<IConveyorModule> _modules = new List<IConveyorModule>();
        private readonly ConveyorModuleContext _moduleContext;
        #endregion

        #region Construction
        internal ConveyorRuntime(ConveyorWorld world, string id, ConveyorTransportContext context,
            IConveyorTransportBackend backend)
        {
            World = world;
            Id = id;
            Path = context.Path;
            Channels = context.Channels;
            Width = context.Width;
            Backend = backend;
            _frame = new ConveyorFrameBuffer(context.PayloadCapacity);
            _scratch = new ConveyorTransportState[context.PayloadCapacity];
            Blocks = new ConveyorBlockReason[Channels];
            ReportedHolds = new bool[Channels];
            BlockedSince = new long[Channels];
            _moduleContext = new ConveyorModuleContext(this);
        }
        #endregion

        #region Public Methods
        public ConveyorBlockReason GetBlockReason(int channel)
        {
            if ((uint)channel >= (uint)Channels) throw new ArgumentOutOfRangeException(nameof(channel));
            return Blocks[channel];
        }

        public bool IsLaneHeld(int channel)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(ConveyorRuntime));
            if (Backend is not IConveyorLaneControl lanes) throw new NotSupportedException("Backend has no lane-holding capability.");
            return lanes.IsLaneHeld(channel);
        }

        public long GetBlockedTicks(int channel)
        {
            if ((uint)channel >= (uint)Channels) throw new ArgumentOutOfRangeException(nameof(channel));
            return Blocks[channel] == ConveyorBlockReason.None ? 0 : World.TickIndex - BlockedSince[channel];
        }
        #endregion

        #region Internal Methods
        internal IConveyorModule AttachModule(IConveyorModuleFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            IConveyorModule module = factory.Create() ?? throw new ArgumentException("Module factory returned null.");
            ModuleLifecycle = true;
            try
            {
                if ((Backend.Capabilities & module.RequiredCapabilities) != module.RequiredCapabilities
                    || ((module.RequiredCapabilities & ConveyorTransportCapabilities.Gates) != 0 && Backend is not IConveyorGateBackend)
                    || ((module.RequiredCapabilities & ConveyorTransportCapabilities.LaneHolding) != 0 && Backend is not IConveyorLaneControl))
                    throw new NotSupportedException("Transport backend does not support this module's required capabilities.");
                module.HandleRegistered(_moduleContext);
                _modules.Add(module);
                return module;
            }
            catch { module.Dispose(); throw; }
            finally { ModuleLifecycle = false; }
        }

        internal void DetachModule(IConveyorModule module)
        {
            if (!_modules.Remove(module)) return;
            ModuleLifecycle = true;
            try { module.Dispose(); }
            finally { ModuleLifecycle = false; }
        }

        internal void Collect(ConveyorTransferRequests requests)
        {
            requests.Source = this;
            requests.Active = true;
            try
            {
                for (int index = 0; index < _modules.Count; index++) _modules[index].CollectTransfers(requests);
            }
            finally { requests.Active = false; requests.Source = null; }
        }

        internal void Publish()
        {
            int count = Backend.CopyStates(_scratch);
            if ((uint)count > (uint)_scratch.Length) throw new InvalidOperationException("Backend returned an invalid state count.");
            for (int index = 0; index < count; index++)
            {
                ConveyorTransportState state = _scratch[index];
                if (!World.TryGetPayload(state.Handle, out ConveyorPayloadDescriptor payload, out ConveyorRuntime owner) || owner != this)
                    throw new InvalidOperationException("Backend publication violated world ownership.");
                _frame.Items[index] = new ConveyorFrameItem(state, payload);
            }
            _frame.Count = count;
            _frame.Version++;
        }

        internal void DisposeOwned()
        {
            if (IsDisposed) return;
            ModuleLifecycle = true;
            try
            {
                for (int index = _modules.Count - 1; index >= 0; index--) _modules[index].Dispose();
                _modules.Clear();
            }
            finally
            {
                ModuleLifecycle = false;
                IsDisposed = true;
                _frame.Disposed = true;
                Backend.Dispose();
            }
        }
        #endregion
    }
}
