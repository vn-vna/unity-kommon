using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Data-driven sound definition. Stored as a sub-asset inside SoundConfiguration
    /// and referenced by serialized <see cref="SoundDefinitionRef"/> fields on game code.
    /// </summary>
    public class SoundDefinition : ScriptableObject
    {
        #region Constants

        private const float MinVolume = 0f;
        private const float MaxVolume = 1f;
        private const float MinPitch = 0.1f;
        private const float MaxPitch = 3f;
        private const float DefaultPriority = 128;
        private const float DefaultSpatialBlend = 0f;

        #endregion

#if UNITY_EDITOR
        [Tooltip("Unique key (required) used for runtime lookups")]
#endif
        [SerializeField]
        private string _id;

#if UNITY_EDITOR
        [Tooltip("Editor-friendly display label")]
#endif
        [SerializeField]
        private string _displayName;

#if UNITY_EDITOR
        [Tooltip("Which bus routes this sound")]
#endif
        [SerializeField]
        private SoundBusType _bus = SoundBusType.Sfx;

#if UNITY_EDITOR
        [Tooltip("The audio asset")]
#endif
        [SerializeField]
        private AudioClip _clip;

#if UNITY_EDITOR
        [Tooltip("Definition volume; combined with volumeScale at play time")]
#endif
        [SerializeField]
        [Range(MinVolume, MaxVolume)]
        private float _volume = MaxVolume;

#if UNITY_EDITOR
        [Tooltip("Playback pitch")]
#endif
        [SerializeField]
        [Range(MinPitch, MaxPitch)]
        private float _pitch = MaxPitch / 3f;

#if UNITY_EDITOR
        [Tooltip("Default false; BGM always true")]
#endif
        [SerializeField]
        private bool _loop;

#if UNITY_EDITOR
        [Tooltip("Unity AudioSource priority")]
#endif
        [SerializeField]
        private int _priority = (int)DefaultPriority;

#if UNITY_EDITOR
        [Tooltip("2D default; the puzzle game is mostly 2D")]
#endif
        [SerializeField]
        [Range(MinVolume, MaxVolume)]
        private float _spatialBlend = DefaultSpatialBlend;

#if UNITY_EDITOR
        [Tooltip("Reserved, default false")]
#endif
        [SerializeField]
        private bool _playOnAwake;

#if UNITY_EDITOR
        [Tooltip("BGM cross-fade support")]
#endif
        [SerializeField]
        [Range(MinVolume, MaxVolume * 5f)]
        private float _fadeInSeconds;

#if UNITY_EDITOR
        [Tooltip("BGM cross-fade support")]
#endif
        [SerializeField]
        [Range(MinVolume, MaxVolume * 5f)]
        private float _fadeOutSeconds;

        public string Id
        {
            get => _id;
            set => _id = value;
        }

        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        public SoundBusType Bus
        {
            get => _bus;
            set => _bus = value;
        }

        public AudioClip Clip
        {
            get => _clip;
            set => _clip = value;
        }

        public float Volume
        {
            get => _volume;
            set => _volume = Mathf.Clamp(value, MinVolume, MaxVolume);
        }

        public float Pitch
        {
            get => _pitch;
            set => _pitch = Mathf.Clamp(value, MinPitch, MaxPitch);
        }

        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public int Priority
        {
            get => _priority;
            set => _priority = value;
        }

        public float SpatialBlend
        {
            get => _spatialBlend;
            set => _spatialBlend = Mathf.Clamp(value, MinVolume, MaxVolume);
        }

        public bool PlayOnAwake
        {
            get => _playOnAwake;
            set => _playOnAwake = value;
        }

        public float FadeInSeconds
        {
            get => _fadeInSeconds;
            set => _fadeInSeconds = value;
        }

        public float FadeOutSeconds
        {
            get => _fadeOutSeconds;
            set => _fadeOutSeconds = value;
        }

        public bool IsSfx => _bus == SoundBusType.Sfx;
        public bool IsBgm => _bus == SoundBusType.Bgm;

        public string DisplayLabel =>
            string.IsNullOrEmpty(_displayName) ? _id : _displayName;
    }
}
