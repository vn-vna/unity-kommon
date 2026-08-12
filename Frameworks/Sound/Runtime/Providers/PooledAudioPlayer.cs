using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Poolable AudioSource wrapper. DISABLE = STOP is the source of truth:
    /// enabling the object starts sound, disabling it stops it.
    /// Pause keeps the object ENABLED (AudioSource.Pause) so a paused sound is
    /// never recycled as free.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class PooledAudioPlayer : MonoBehaviour
    {
        [SerializeField]
        private AudioSource _source;

        private bool _inUse;
        private bool _isPaused;
        private int _generation;
        private int _instanceId;
        private SoundDefinition _current;

        public bool IsFree => !_inUse;
        public bool IsPaused => _isPaused;
        public int Generation => _generation;
        public int InstanceId => _instanceId;
        public AudioSource Source => _source;
        public SoundDefinition Current => _current;

        public bool IsPlaying => _inUse && !_isPaused && _source != null && _source.isPlaying;

        /// <summary>Assigns the pool instance id and wires the required AudioSource.</summary>
        public void Initialize(int instanceId)
        {
            _instanceId = instanceId;
            if (_source == null)
            {
                _source = GetComponent<AudioSource>();
            }
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
            }

            _source.playOnAwake = false;
            _source.loop = false;
            gameObject.SetActive(false);
        }

        /// <summary>Copies clip / volume / pitch / loop / priority / spatialBlend; bumps generation.</summary>
        public void Configure(SoundDefinition def, float volumeScale)
        {
            _current = def;
            _generation++; // invalidate stale handles

            _source.clip = def.Clip;
            _source.volume = Mathf.Clamp01(def.Volume * volumeScale);
            _source.pitch = def.Pitch;
            _source.loop = def.Loop;
            _source.priority = def.Priority;
            _source.spatialBlend = def.SpatialBlend;
            _source.playOnAwake = false;
            _source.Stop();
        }

        /// <summary>Starts playback: enables the GameObject (disable = stop semantics).</summary>
        public void PlayRequest()
        {
            _inUse = true;
            _isPaused = false;
            gameObject.SetActive(true); // enabling starts (see OnEnable pattern if needed)
            _source.Play();
        }

        /// <summary>Stops playback, marks free, DISABLES the GameObject.</summary>
        public void StopRequest()
        {
            _source.Stop();
            _isPaused = false;
            _inUse = false;
            gameObject.SetActive(false); // DISABLE = STOP
        }

        /// <summary>
        /// Pauses without disabling. The pooled object stays ENABLED so the pool
        /// never recycles it; only StopRequest disables.
        /// </summary>
        public void PauseRequest()
        {
            if (!_inUse || _isPaused) return;
            _source.Pause();
            _isPaused = true;
        }

        /// <summary>Resumes a paused sound. Object remains enabled.</summary>
        public void ResumeRequest()
        {
            if (!_inUse || !_isPaused) return;
            _source.UnPause();
            _isPaused = false;
        }

        private void OnDisable()
        {
            if (_source != null)
            {
                _source.Stop();
            }
            _isPaused = false;
            _inUse = false;
        }
    }
}
