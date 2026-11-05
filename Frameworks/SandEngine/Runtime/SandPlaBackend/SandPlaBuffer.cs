#if PLA_CREATIVE_BUILD || UNITY_EDITOR
using System;

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Pla
{
    /// <summary>
    /// Reference-owned managed storage. Kernels borrow the array once for a
    /// synchronous step; a borrow must not outlive a resize, swap or disposal.
    /// </summary>
    public class SandPlaBuffer<T> : IDisposable where T : struct
    {
        private T[] _items;
        public bool IsCreated => _items != null;
        public int Length => GetArrayForStep().Length;

        public T this[int index]
        {
            get => GetArrayForStep()[index];
            set => GetArrayForStep()[index] = value;
        }

        public SandPlaBuffer(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            _items = new T[length];
        }

        public SandPlaBuffer(T[] source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _items = new T[source.Length];
            Array.Copy(source, _items, source.Length);
        }

        internal T[] GetArrayForStep()
        {
            if (_items == null) throw new ObjectDisposedException(nameof(SandPlaBuffer<T>));
            return _items;
        }

        public static implicit operator T[](SandPlaBuffer<T> source)
        {
            if (source == null) throw new ObjectDisposedException(nameof(SandPlaBuffer<T>));
            return source.GetArrayForStep();
        }

        public void CopyFrom(T[] source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            T[] items = GetArrayForStep();
            if (source.Length != items.Length) throw new ArgumentException("Buffer lengths must match.", nameof(source));
            Array.Copy(source, items, items.Length);
        }

        public T[] ToArray()
        {
            T[] items = GetArrayForStep();
            var copy = new T[items.Length];
            Array.Copy(items, copy, items.Length);
            return copy;
        }

        public void Dispose() => _items = null;
    }
}
#endif
