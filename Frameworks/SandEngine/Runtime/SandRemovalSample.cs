#if PLA_CREATIVE_BUILD
using PortableArray = Com.Scheherazade.Common.Compatibility.CreativeArray;
#else
using PortableArray = System.Array;
#endif
using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    public readonly struct SandRemovalSample
    {
        public int CellIndex { get; }
        public int PackedCell { get; }

        public SandRemovalSample(int cellIndex, int packedCell)
        {
            CellIndex = cellIndex;
            PackedCell = packedCell;
        }
    }

    /// <summary>Bounded reservoir sampling in removal order, with caller-owned seed and storage.</summary>
    public sealed class SandRemovalSampleBuffer
    {
        public int Count { get; private set; }
        public int Capacity => _samples.Length;
        public SandRemovalSample this[int index] => _samples[index];

        private SandRemovalSample[] _samples = PortableArray.Empty<SandRemovalSample>();
        private int _sampleLimit;
        private int _observedCount;
        private uint _randomState;

        public void EnsureCapacity(int capacity)
        {
            capacity = Mathf.Max(0, capacity);
            if (_samples.Length >= capacity)
            {
                return;
            }
            Array.Resize(ref _samples, capacity);
        }

        public void Reset(int sampleLimit, uint randomSeed)
        {
            _sampleLimit = Mathf.Clamp(sampleLimit, 0, _samples.Length);
            _observedCount = 0;
            _randomState = randomSeed != 0 ? randomSeed : 0xA341316Cu;
            Count = 0;
        }

        public void Consider(int cellIndex, int packedCell)
        {
            if (_sampleLimit <= 0)
            {
                return;
            }
            _observedCount++;
            var sample = new SandRemovalSample(cellIndex, packedCell);
            if (Count < _sampleLimit)
            {
                _samples[Count++] = sample;
                return;
            }
            int replacementIndex = (int)(NextRandom() % (uint)_observedCount);
            if (replacementIndex < _sampleLimit)
            {
                _samples[replacementIndex] = sample;
            }
        }

        public void Swap(int firstIndex, int secondIndex)
        {
            if (firstIndex == secondIndex)
            {
                return;
            }
            SandRemovalSample first = _samples[firstIndex];
            _samples[firstIndex] = _samples[secondIndex];
            _samples[secondIndex] = first;
        }

        private uint NextRandom()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return _randomState;
        }
    }
}
