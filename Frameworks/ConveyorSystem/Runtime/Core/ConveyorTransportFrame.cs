using System;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    /// <summary>Borrowed until the next publication/disposal. Use CopyTo for an owned snapshot.</summary>
    public readonly struct ConveyorTransportFrame
    {
        private readonly ConveyorFrameBuffer _owner;
        private readonly long _version;
        public int Count { get { Validate(); return _owner.Count; } }
        public long Version => _version;
        public ConveyorFrameItem this[int index]
        {
            get
            {
                Validate();
                if ((uint)index >= (uint)_owner.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _owner.Items[index];
            }
        }

        internal ConveyorTransportFrame(ConveyorFrameBuffer owner)
        {
            _owner = owner;
            _version = owner.Version;
        }

        public void CopyTo(ConveyorFrameItem[] destination, int offset = 0)
        {
            Validate();
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (offset < 0 || destination.Length - offset < _owner.Count) throw new ArgumentOutOfRangeException(nameof(offset));
            Array.Copy(_owner.Items, 0, destination, offset, _owner.Count);
        }

        private void Validate()
        {
            if (_owner == null || _owner.Disposed || _owner.Version != _version)
                throw new InvalidOperationException("The borrowed conveyor frame has expired. Copy data before the next publication.");
        }
    }

    internal sealed class ConveyorFrameBuffer
    {
        internal readonly ConveyorFrameItem[] Items;
        internal long Version;
        internal int Count;
        internal bool Disposed;
        internal ConveyorFrameBuffer(int capacity) { Items = new ConveyorFrameItem[capacity]; }
    }
}
