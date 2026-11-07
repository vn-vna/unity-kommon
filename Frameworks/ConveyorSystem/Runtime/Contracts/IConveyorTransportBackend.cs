using System;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public readonly struct ConveyorTransportContext
    {
        public ConveyorPath Path { get; }
        public int PayloadCapacity { get; }
        public int Channels { get; }
        public float Width { get; }

        public ConveyorTransportContext(ConveyorPath path, int payloadCapacity, int channels, float width)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            if (payloadCapacity < 1 || channels < 1) throw new ArgumentOutOfRangeException(nameof(payloadCapacity));
            ConveyorValidation.Positive(width, nameof(width));
            PayloadCapacity = payloadCapacity;
            Channels = channels;
            Width = width;
        }
    }

    public interface IConveyorTransportProvider
    {
        IConveyorTransportBackend Create(in ConveyorTransportContext context);
    }

    /// <summary>
    /// Trusted, instance-owned simulation product. Mutations are called exclusively by its world.
    /// A successful reservation guarantees CommitAdmission cannot fail until cancelled or consumed.
    /// Read queries cannot mutate, reenter the world, or retain caller scratch arrays.
    /// </summary>
    public interface IConveyorTransportBackend : IDisposable
    {
        ConveyorTransportCapabilities Capabilities { get; }
        ConveyorStepDriver StepDriver { get; }
        int Count { get; }
        ConveyorAdmissionStatus TryReserve(in ConveyorAdmissionRequest request, out ConveyorBackendReservation reservation);
        bool IsReservationValid(in ConveyorBackendReservation reservation);
        void CancelReservation(in ConveyorBackendReservation reservation);
        void CommitAdmission(in ConveyorBackendReservation reservation, ConveyorPayloadHandle handle);
        void Remove(ConveyorPayloadHandle handle);
        bool TryGetState(ConveyorPayloadHandle handle, out ConveyorTransportState state);
        int CopyStates(ConveyorTransportState[] destination);
        void PrepareStep(double deltaTime);
        void CompleteStep();
        void SetSuspended(bool suspended);
    }

    public interface IConveyorGateBackend
    {
        int AddGate(int channel, double distance);
        void RemoveGate(int gateId);
        bool TryGetWaiting(int gateId, out ConveyorPayloadHandle handle);
    }

    public interface IConveyorLaneControl
    {
        void SetLaneHeld(int channel, bool held);
        bool IsLaneHeld(int channel);
        void SetSpeed(double speed);
    }

    public interface IConveyorSelfTransferValidation
    {
        bool IsDestinationSupported(ConveyorPayloadHandle source, in ConveyorAdmissionRequest destination);
    }
}
