using System;
using System.Runtime.CompilerServices;

namespace Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue
{
    /// <summary>
    /// Identifies a collector in one queue instance. Create a fresh queue for a new run;
    /// an old handle cannot collect a front in the replacement instance.
    /// IsValid is structural validity, not a promise that the collector is still queued.
    /// </summary>
    public readonly struct CollectorHandle : IEquatable<CollectorHandle>
    {
        public int Id { get; }
        public bool IsValid => _owner != null;

        private readonly object _owner;

        internal CollectorHandle(object owner, int id)
        {
            _owner = owner;
            Id = id;
        }

        internal bool BelongsTo(object owner) => ReferenceEquals(_owner, owner);

        public bool Equals(CollectorHandle other) => ReferenceEquals(_owner, other._owner) && Id == other.Id;
        public override bool Equals(object obj) => obj is CollectorHandle other && Equals(other);
        public override int GetHashCode() => unchecked(((_owner == null ? 0 : RuntimeHelpers.GetHashCode(_owner)) * 397) ^ Id);
        public static bool operator ==(CollectorHandle left, CollectorHandle right) => left.Equals(right);
        public static bool operator !=(CollectorHandle left, CollectorHandle right) => !left.Equals(right);
    }
}
