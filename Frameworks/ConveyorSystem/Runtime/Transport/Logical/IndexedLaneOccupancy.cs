using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    /// <summary>Single-cell occupancy includes both reserved and committed cargo; token ownership stays in the backend.</summary>
    internal sealed class IndexedLaneOccupancy
    {
        private const int MaximumCells = 1048576;
        private readonly bool[] _occupied;
        private readonly Pose[] _pathSamples;
        private readonly ConveyorTransportContext _context;
        private readonly int _slots;
        private readonly double _spacing;

        internal IndexedLaneOccupancy(ConveyorTransportContext context, int slots, double spacing)
        {
            if ((long)slots * context.Channels > MaximumCells)
                throw new ArgumentException("Indexed transport supports at most 1,048,576 cells per conveyor.");
            _context = context;
            _slots = slots;
            _spacing = spacing;
            _occupied = new bool[slots * context.Channels];
            _pathSamples = new Pose[Math.Max(64, slots)];
            for (int index = 0; index < _pathSamples.Length; index++)
                _pathSamples[index] = context.Path.Evaluate(index * context.Path.Length / _pathSamples.Length);
        }

        internal int FindCell(int channel, double worldDistance, double phase)
        {
            double logical = ConveyorValidation.Wrap(worldDistance - phase, _context.Path.Length);
            int index = (int)Math.Floor(logical / _spacing + 0.5) % _slots;
            return index * _context.Channels + channel;
        }

        internal bool TryReserve(int cell)
        {
            if (_occupied[cell]) return false;
            _occupied[cell] = true;
            return true;
        }

        internal void Release(int cell) { _occupied[cell] = false; }
        internal double LogicalDistance(int cell) => (cell / _context.Channels) * _spacing;

        internal Pose EvaluateLane(double distance, int channel)
        {
            double sample = distance * _pathSamples.Length / _context.Path.Length;
            int before = Math.Min((int)sample, _pathSamples.Length - 1);
            int after = (before + 1) % _pathSamples.Length;
            float fraction = (float)(sample - before);
            Quaternion rotation = Quaternion.Slerp(_pathSamples[before].rotation, _pathSamples[after].rotation, fraction);
            Vector3 position = Vector3.Lerp(_pathSamples[before].position, _pathSamples[after].position, fraction);
            position += rotation * Vector3.right * (((channel + 0.5f) / _context.Channels - 0.5f) * _context.Width);
            return new Pose(position, rotation);
        }
    }
}
