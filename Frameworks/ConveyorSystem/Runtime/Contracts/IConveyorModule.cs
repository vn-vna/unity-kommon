using System;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public interface IConveyorModuleFactory { IConveyorModule Create(); }

    public interface IConveyorModule : IDisposable
    {
        ConveyorTransportCapabilities RequiredCapabilities { get; }
        void HandleRegistered(ConveyorModuleContext context);
        void CollectTransfers(ConveyorTransferRequests requests);
    }

    public interface IConveyorTransferPolicy
    {
        ConveyorOverflowDisposition EvaluateFullDestination(in ConveyorTransferContext context);
    }

    public readonly struct ConveyorTransferContext
    {
        public ConveyorPayloadHandle Handle { get; }
        public string SourceId { get; }
        public string DestinationId { get; }

        public ConveyorTransferContext(ConveyorPayloadHandle handle, string sourceId, string destinationId)
        {
            Handle = handle;
            SourceId = sourceId;
            DestinationId = destinationId;
        }
    }

    public sealed class ConveyorModuleContext
    {
        public ConveyorRuntime Runtime { get; }
        internal ConveyorModuleContext(ConveyorRuntime runtime) { Runtime = runtime; }

        public int AddGate(int channel, double normalizedDistance)
        {
            RequireLifecycle();
            if (!ConveyorValidation.IsFinite(normalizedDistance) || normalizedDistance < 0 || normalizedDistance >= 1)
                throw new ArgumentOutOfRangeException(nameof(normalizedDistance));
            return ((IConveyorGateBackend)Runtime.Backend).AddGate(channel, normalizedDistance * Runtime.Path.Length);
        }

        public void RemoveGate(int gateId)
        {
            RequireLifecycle();
            ((IConveyorGateBackend)Runtime.Backend).RemoveGate(gateId);
        }

        public bool TryGetWaiting(int gateId, out ConveyorPayloadHandle handle)
            => ((IConveyorGateBackend)Runtime.Backend).TryGetWaiting(gateId, out handle);

        private void RequireLifecycle()
        {
            if (!Runtime.ModuleLifecycle) throw new InvalidOperationException("Gate registration is restricted to module lifecycle boundaries.");
        }
    }

    /// <summary>Borrowed collection sink, valid only inside the module's CollectTransfers call.</summary>
    public sealed class ConveyorTransferRequests
    {
        internal ConveyorRuntime Source;
        internal ConveyorWorld World;
        internal bool Active;

        public bool TryAdd(ConveyorPayloadHandle handle, int sourceGate, string destinationId,
            int destinationChannel, double normalizedExit, IConveyorTransferPolicy policy)
        {
            if (!Active) throw new InvalidOperationException("Transfer collection has ended.");
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (string.IsNullOrWhiteSpace(destinationId)) throw new ArgumentException("Destination ID is required.");
            if (!ConveyorValidation.IsFinite(normalizedExit) || normalizedExit < 0 || normalizedExit >= 1)
                throw new ArgumentOutOfRangeException(nameof(normalizedExit));
            return World.CollectTransfer(Source, handle, sourceGate, destinationId, destinationChannel, normalizedExit, policy);
        }
    }
}
