using System;
using Com.Hapiga.Scheherazade.Common.Haptics;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Zero-alloc address of an active rhythm playback. The generation counter
    /// invalidates stale handles (a handle taken before a timeline was stopped
    /// can never pause or stop a newer playback).
    /// </summary>
    public readonly struct HapticHandle : IEquatable<HapticHandle>
    {
        /// <summary>The null / failed-play handle. IsValid == false.</summary>
        public static readonly HapticHandle Invalid = new HapticHandle(-1, 0);

        private readonly int _id;         // timeline slot id (-1 = invalid)
        private readonly int _generation; // generation counter to invalidate stale handles

        public HapticHandle(int id, int generation)
        {
            _id = id;
            _generation = generation;
        }

        public bool IsValid => _id >= 0 && _generation > 0;
        public int Id => _id;
        public int Generation => _generation;

        public void Stop()
        {
            if (IsValid && HapticManager.Instance != null)
            {
                HapticManager.Instance.Stop(this);
            }
        }

        public void Pause()
        {
            if (IsValid && HapticManager.Instance != null)
            {
                HapticManager.Instance.Pause(this);
            }
        }

        public void Resume()
        {
            if (IsValid && HapticManager.Instance != null)
            {
                HapticManager.Instance.Resume(this);
            }
        }

        public bool Equals(HapticHandle other) =>
            _id == other._id && _generation == other._generation;

        public override bool Equals(object obj) =>
            obj is HapticHandle other && Equals(other);

        public override int GetHashCode() => (_id * 397) ^ _generation;

        public static bool operator ==(HapticHandle a, HapticHandle b) => a.Equals(b);
        public static bool operator !=(HapticHandle a, HapticHandle b) => !a.Equals(b);
    }
}
