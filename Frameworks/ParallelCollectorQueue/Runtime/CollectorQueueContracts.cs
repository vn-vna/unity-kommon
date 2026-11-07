namespace Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue
{
    public enum CollectorLocation
    {
        Reserve,
        Queue,
        Removed
    }

    /// <summary>A value snapshot. QueueIndex is -1 outside a collector queue.</summary>
    public readonly struct CollectorSnapshot
    {
        public CollectorHandle Handle { get; }
        public CollectorDefinition Definition { get; }
        public int RemainingUnits { get; }
        public int CreditedUnits => Definition.RequiredUnits - RemainingUnits;
        public CollectorLocation Location { get; }
        public int QueueIndex { get; }
        public bool IsFilled => Handle.IsValid && RemainingUnits == 0;
        public bool IsHeld => IsFilled && Location == CollectorLocation.Queue;

        internal CollectorSnapshot(CollectorHandle handle, CollectorDefinition definition, int remainingUnits,
            CollectorLocation location, int queueIndex)
        {
            Handle = handle;
            Definition = definition;
            RemainingUnits = remainingUnits;
            Location = location;
            QueueIndex = queueIndex;
        }
    }

    public enum CollectorCollectionStatus
    {
        Accepted,
        InvalidQueue,
        InvalidQuantity,
        Empty,
        StaleCollector,
        Blocked,
        TypeMismatch,
        FilledHeld,
        EventBufferFull
    }

    public enum CollectorQueueEventKind
    {
        CargoCollected,
        CollectorFilled,
        CollectorRemoved,
        CollectorQueued,
        FrontChanged,
        Completed
    }

    /// <summary>
    /// An immutable transition fact, not a live view. Collector may be invalid for an
    /// empty FrontChanged or Completed event. Query the queue for final current state.
    /// Sequence is instance-local and increases only for committed events.
    /// </summary>
    public readonly struct CollectorQueueEvent
    {
        public long Sequence { get; }
        public CollectorQueueEventKind Kind { get; }
        public int QueueIndex { get; }
        public CollectorSnapshot Collector { get; }
        public CollectorHandle PreviousFront { get; }
        public int Units { get; }

        internal CollectorQueueEvent(long sequence, CollectorQueueEventKind kind, int queueIndex,
            CollectorSnapshot collector, CollectorHandle previousFront, int units)
        {
            Sequence = sequence;
            Kind = kind;
            QueueIndex = queueIndex;
            Collector = collector;
            PreviousFront = previousFront;
            Units = units;
        }
    }
}
