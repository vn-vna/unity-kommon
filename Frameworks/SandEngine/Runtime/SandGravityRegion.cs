using System;
using System.Collections.Generic;
using Com.Scheherazade.Common.Frameworks.GridSystem;
#if PLA_CREATIVE_BUILD && UNITY_LUNA
using RectInt = Com.Scheherazade.Common.Compatibility.CreativeRectInt;
#endif
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>Immutable modifier field. Zero leaves grain gravity unchanged; 1 down, 2 up, 3 right, 4 left (Y-down).</summary>
    public readonly struct SandGravityFieldSnapshot
    {
        internal byte[] Cells { get; }
        public bool IsCreated => Cells != null;
        public int Length => Cells?.Length ?? 0;
        public byte this[int index] => Cells[index];
        public GridCoord GetDirection(int index) => Cells[index] == 0 ? GridCoord.zero : SandGravityMap.Decode(Cells[index]);
        internal SandGravityFieldSnapshot(byte[] cells) => Cells = cells;
    }

    /// <summary>Opt-in contract: the backend honors StepParams.GravityField when present.</summary>
    public interface IRegionalSandBackend
    {
        bool HasPersistentGravity { get; }
    }

    /// <summary>An owned, updateable gravity override in engine-local Y-down pixels.</summary>
    public sealed class SandGravityRegion : IDisposable
    {
        public string OwnerId { get; }
        public RectInt Bounds { get; private set; }
        public GridCoord Direction { get; private set; }
        public int Priority { get; private set; }
        public bool IsDisposed => _map == null;

        private SandGravityMap _map;

        internal SandGravityRegion(SandGravityMap map, string ownerId, RectInt bounds, GridCoord direction, int priority)
        {
            _map = map;
            OwnerId = ownerId;
            Bounds = bounds;
            Direction = direction;
            Priority = priority;
        }

        public void Update(RectInt bounds, GridCoord direction, int priority)
        {
            if (_map == null) throw new ObjectDisposedException(nameof(SandGravityRegion));
            _map.Validate(bounds, direction);
            Bounds = bounds;
            Direction = direction;
            Priority = priority;
            _map.Invalidate();
        }

        public void Dispose()
        {
            SandGravityMap map = _map;
            _map = null;
            map?.Remove(this);
        }
    }

    /// <summary>Rebuilds an immutable modifier field only when registrations change.</summary>
    internal sealed class SandGravityMap
    {
        private readonly SandEngine _engine;
        private readonly List<SandGravityRegion> _regions = new();
        private byte[] _cells;
        private bool _dirty = true;
        public int Count => _regions.Count;
        internal int Revision { get; private set; }

        internal SandGravityMap(SandEngine engine) => _engine = engine;

        internal SandGravityRegion Add(string ownerId, RectInt bounds, GridCoord direction, int priority)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("A stable gravity owner ID is required.", nameof(ownerId));
            Validate(bounds, direction);
            foreach (SandGravityRegion region in _regions)
                if (region.OwnerId == ownerId) throw new InvalidOperationException($"Gravity owner '{ownerId}' is already registered.");
            var registration = new SandGravityRegion(this, ownerId, bounds, direction, priority);
            _regions.Add(registration);
            Invalidate();
            return registration;
        }

        internal void Validate(RectInt bounds, GridCoord direction)
        {
            if (bounds.x < 0 || bounds.y < 0 || bounds.width <= 0 || bounds.height <= 0
                || (long)bounds.x + bounds.width > _engine.Width || (long)bounds.y + bounds.height > _engine.Height)
                throw new ArgumentOutOfRangeException(nameof(bounds), "Gravity region must fit inside the pool canvas.");
            if (!IsCardinal(direction)) throw new ArgumentException("Gravity must be one cardinal direction.", nameof(direction));
        }

        internal void Invalidate()
        {
            _dirty = true;
            unchecked { Revision++; }
        }

        /// <summary>Returns stronger overlapping registrations without building a per-cell ownership grid.</summary>
        internal bool CopyOccluders(string ownerId, List<RectInt> destination)
        {
            destination.Clear();
            SandGravityRegion target = _regions.Find(region => region.OwnerId == ownerId);
            if (target == null) return false;
            foreach (SandGravityRegion region in _regions)
            {
                if (ReferenceEquals(region, target)) continue;
                bool stronger = region.Priority > target.Priority
                    || region.Priority == target.Priority && string.CompareOrdinal(region.OwnerId, target.OwnerId) < 0;
                RectInt a = region.Bounds, b = target.Bounds;
                if (stronger && a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin)
                    destination.Add(a);
            }
            return true;
        }
        internal void Remove(SandGravityRegion region)
        {
            if (_regions.Remove(region)) Invalidate();
        }

        internal byte[] GetCells()
        {
            if (_regions.Count == 0) return null;
            if (!_dirty) return _cells;
            var cells = new byte[_engine.Backend.CellCount];
            // Apply weak-to-strong. Equal priority uses the ordinally smaller ID,
            // independent of registration/spawn order or platform string hashing.
            _regions.Sort((a, b) => a.Priority != b.Priority
                ? a.Priority.CompareTo(b.Priority) : string.CompareOrdinal(b.OwnerId, a.OwnerId));
            foreach (SandGravityRegion region in _regions)
            {
                byte code = Encode(region.Direction);
                RectInt bounds = region.Bounds;
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                for (int x = bounds.xMin; x < bounds.xMax; x++) cells[y * _engine.Width + x] = code;
            }
            _cells = cells;
            _dirty = false;
            return cells;
        }

        internal static bool IsCardinal(GridCoord direction) =>
            direction.x == 0 && (direction.y == 1 || direction.y == -1)
            || direction.y == 0 && (direction.x == 1 || direction.x == -1);

        internal static byte Encode(GridCoord direction) => (byte)(direction.x > 0 ? 3 : direction.x < 0 ? 4 : direction.y < 0 ? 2 : 1);
        internal static GridCoord Decode(byte code) => code == 3 ? new GridCoord(1, 0)
            : code == 4 ? new GridCoord(-1, 0) : code == 2 ? new GridCoord(0, -1) : new GridCoord(0, 1);
    }
}
