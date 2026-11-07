using System;

namespace Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue
{
    /// <summary>Immutable authored demand. Type IDs are consumer-owned, not render colors.</summary>
    public readonly struct CollectorDefinition
    {
        public const int NoLinkedGroup = -1;

        public int Id { get; }
        public int TypeId { get; }
        public int RequiredUnits { get; }
        public int LinkedGroupId { get; }

        public CollectorDefinition(int id, int typeId, int requiredUnits, int linkedGroupId = NoLinkedGroup)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (typeId < 0) throw new ArgumentOutOfRangeException(nameof(typeId));
            if (requiredUnits < 1) throw new ArgumentOutOfRangeException(nameof(requiredUnits));
            if (linkedGroupId < NoLinkedGroup) throw new ArgumentOutOfRangeException(nameof(linkedGroupId));
            Id = id;
            TypeId = typeId;
            RequiredUnits = requiredUnits;
            LinkedGroupId = linkedGroupId;
        }
    }
}
