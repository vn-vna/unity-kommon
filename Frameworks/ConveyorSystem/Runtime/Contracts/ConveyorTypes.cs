using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [Flags]
    public enum ConveyorTransportCapabilities
    {
        None = 0,
        PosePublication = 1,
        AdmissionReservations = 2,
        LaneHolding = 4,
        Gates = 8
    }

    public enum ConveyorStepDriver { Synchronous, External }
    public enum ConveyorAdmissionStatus { Accepted, Full, Unsupported, Unavailable }
    public enum ConveyorRemovalReason { Removed, Delivered, Vaporized, Shutdown }
    public enum ConveyorShutdownDisposition { RequireEmpty, Discard }
    public enum ConveyorResultKind { Inserted, Removed, Transferred, LaneBlocked, LaneReleased }
    public enum ConveyorBlockReason { None, Full, Unavailable, Unsupported, ResultBackpressure }
    public enum ConveyorOverflowDisposition { Block, Vaporize }

    public readonly struct ConveyorPayloadHandle : IEquatable<ConveyorPayloadHandle>
    {
        public long WorldId { get; }
        public int Slot { get; }
        public uint Generation { get; }
        public bool IsValid => WorldId != 0 && Generation != 0;

        internal ConveyorPayloadHandle(long worldId, int slot, uint generation)
        {
            WorldId = worldId;
            Slot = slot;
            Generation = generation;
        }

        public bool Equals(ConveyorPayloadHandle other) => WorldId == other.WorldId
            && Slot == other.Slot && Generation == other.Generation;
        public override bool Equals(object obj) => obj is ConveyorPayloadHandle other && Equals(other);
        public override int GetHashCode() => unchecked((WorldId.GetHashCode() * 397 ^ Slot) * 397 ^ (int)Generation);
        public static bool operator ==(ConveyorPayloadHandle left, ConveyorPayloadHandle right) => left.Equals(right);
        public static bool operator !=(ConveyorPayloadHandle left, ConveyorPayloadHandle right) => !left.Equals(right);
    }

    /// <summary>Game identity and a provider-resolved profile, not a mandatory slot/body/color layout.</summary>
    public readonly struct ConveyorPayloadDescriptor
    {
        public long ConsumerKey { get; }
        public int ProfileId { get; }

        public ConveyorPayloadDescriptor(long consumerKey, int profileId = 0)
        {
            ConsumerKey = consumerKey;
            ProfileId = profileId;
        }
    }

    public readonly struct ConveyorAdmissionRequest
    {
        public ConveyorPayloadDescriptor Payload { get; }
        public Pose Pose { get; }
        public int Channel { get; }
        public double? RouteDistance { get; }

        public ConveyorAdmissionRequest(ConveyorPayloadDescriptor payload, Pose pose, int channel = 0, double? routeDistance = null)
        {
            Payload = payload;
            Pose = pose;
            Channel = channel;
            RouteDistance = routeDistance;
        }
    }

    /// <summary>Provider-owned token. Only its issuing backend may validate or consume it.</summary>
    public readonly struct ConveyorBackendReservation
    {
        public object Owner { get; }
        public long Token { get; }
        public bool IsValid => Owner != null && Token != 0;

        public ConveyorBackendReservation(object owner, long token)
        {
            Owner = owner;
            Token = token;
        }
    }

    public readonly struct ConveyorIngressReservation
    {
        internal readonly ConveyorWorld World;
        internal readonly int Slot;
        internal readonly long Token;
        public bool IsValid => World != null && Token != 0;

        internal ConveyorIngressReservation(ConveyorWorld world, int slot, long token)
        {
            World = world;
            Slot = slot;
            Token = token;
        }
    }

    public readonly struct ConveyorTransportState
    {
        public ConveyorPayloadHandle Handle { get; }
        public Pose Pose { get; }
        public int Channel { get; }
        public double Distance { get; }
        public int WaitingGate { get; }

        public ConveyorTransportState(ConveyorPayloadHandle handle, Pose pose, int channel, double distance, int waitingGate = -1)
        {
            Handle = handle;
            Pose = pose;
            Channel = channel;
            Distance = distance;
            WaitingGate = waitingGate;
        }
    }

    public readonly struct ConveyorFrameItem
    {
        public ConveyorPayloadHandle Handle { get; }
        public ConveyorPayloadDescriptor Payload { get; }
        public Pose Pose { get; }
        public int Channel { get; }
        public double Distance { get; }

        public ConveyorFrameItem(ConveyorTransportState state, ConveyorPayloadDescriptor payload)
        {
            Handle = state.Handle;
            Payload = payload;
            Pose = state.Pose;
            Channel = state.Channel;
            Distance = state.Distance;
        }
    }

    public readonly struct ConveyorResult
    {
        public ConveyorResultKind Kind { get; }
        public ConveyorPayloadHandle Handle { get; }
        public ConveyorPayloadDescriptor Payload { get; }
        public string SourceId { get; }
        public string DestinationId { get; }
        public int Channel { get; }
        public ConveyorRemovalReason RemovalReason { get; }
        public ConveyorBlockReason BlockReason { get; }

        internal ConveyorResult(ConveyorResultKind kind, ConveyorPayloadHandle handle,
            ConveyorPayloadDescriptor payload, string sourceId, string destinationId = null,
            int channel = -1, ConveyorRemovalReason removalReason = ConveyorRemovalReason.Removed,
            ConveyorBlockReason blockReason = ConveyorBlockReason.None)
        {
            Kind = kind;
            Handle = handle;
            Payload = payload;
            SourceId = sourceId;
            DestinationId = destinationId;
            Channel = channel;
            RemovalReason = removalReason;
            BlockReason = blockReason;
        }
    }

    internal static class ConveyorValidation
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        public static double Wrap(double value, double length) => (value % length + length) % length;

        public static void Positive(double value, string name)
        {
            if (!IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
        }
    }
}
