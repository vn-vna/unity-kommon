using System;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Zero-alloc address of an active playing sound. Carries the owning manager
    /// so Stop / Pause / Resume can be called directly from the handle.
    /// The generation counter invalidates stale handles (a handle taken before a
    /// pooled player was recycled can never control the wrong sound).
    /// </summary>
    public readonly struct SoundHandle : IEquatable<SoundHandle>
    {
        /// <summary>The null / failed-play handle. IsValid == false.</summary>
        public static readonly SoundHandle Invalid = new SoundHandle(null, -1, 0);

        private readonly SoundManager _owner; // null = invalid
        private readonly int _id;             // pooled player instance id (-1 = invalid)
        private readonly int _generation;     // generation counter to invalidate stale handles

        public SoundHandle(SoundManager owner, int id, int generation)
        {
            _owner = owner;
            _id = id;
            _generation = generation;
        }

        public bool IsValid => _owner != null && _id >= 0 && _generation > 0;
        public int Id => _id;
        public int Generation => _generation;

        public void Stop()
        {
            if (IsValid) _owner.Stop(this);
        }

        public void Pause()
        {
            if (IsValid) _owner.Pause(this);
        }

        public void Resume()
        {
            if (IsValid) _owner.Resume(this);
        }

        public bool Equals(SoundHandle other) =>
            _id == other._id && _generation == other._generation;

        public override bool Equals(object obj) =>
            obj is SoundHandle other && Equals(other);

        public override int GetHashCode() => (_id * 397) ^ _generation;

        public static bool operator ==(SoundHandle a, SoundHandle b) => a.Equals(b);
        public static bool operator !=(SoundHandle a, SoundHandle b) => !a.Equals(b);
    }
}
