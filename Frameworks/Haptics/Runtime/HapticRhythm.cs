using System;
using Com.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Scheherazade.Common.Haptics
{
    public enum HapticRhythmPlatform
    {
        Android = 0,
        Ios = 1
    }

    /// <summary>
    /// Rhythm definition sub-asset stored inside HapticConfiguration.
    /// Keyframes are kept sorted by time; the editor enforces ordering.
    /// </summary>
    public class HapticRhythm : ScriptableObject
    {
        #region Constants

        private const float MinDuration = 0.01f;

        #endregion

        #region Serialized Fields

        [Tooltip("Unique key (required) used for runtime lookups")]
        [SerializeField] private string _id;

        [Tooltip("Editor-friendly display label")]
        [SerializeField] private string _displayName;

        [Tooltip("Computed hint refreshed from the last keyframe end")]
        [SerializeField] private float _durationSeconds;

        [Tooltip("When true, the timeline restarts after the last keyframe")]
        [SerializeField] private bool _loop;

        [Tooltip("Legacy unified timeline retained as a migration fallback")]
        [SerializeField, HideInInspector]
        private HapticKeyframe[] _keyframes = Array.Empty<HapticKeyframe>();

        [Tooltip("Android-specific timeline; kept sorted by timeSeconds")]
        [SerializeField]
        private HapticKeyframe[] _androidKeyframes = Array.Empty<HapticKeyframe>();

        [Tooltip("iOS-specific timeline; kept sorted by timeSeconds")]
        [SerializeField]
        private HapticKeyframe[] _iosKeyframes = Array.Empty<HapticKeyframe>();

        [SerializeField, HideInInspector] private bool _hasAndroidTimeline;
        [SerializeField, HideInInspector] private bool _hasIosTimeline;

        [Tooltip("Seed template this rhythm was created from; cleared when keyframes are edited")]
        [SerializeField] private HapticRhythmTemplate _templateSeed = HapticRhythmTemplate.Custom;

        [Tooltip("Reserved; the puzzle game is mostly 2D, default false")]
        [SerializeField] private bool _scaleIntensityWithDistance;

        #endregion

        #region Properties

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

        public float DurationSeconds
        {
            get => _durationSeconds;
            set => _durationSeconds = Mathf.Max(MinDuration, value);
        }

        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public HapticKeyframe[] Keyframes
        {
            get => GetKeyframes(ActivePlatform);
            set
            {
                HapticKeyframe[] source = value ?? Array.Empty<HapticKeyframe>();
                _keyframes = Clone(source);
                _androidKeyframes = Clone(source);
                _iosKeyframes = Clone(source);
                _hasAndroidTimeline = true;
                _hasIosTimeline = true;
            }
        }

        public HapticKeyframe[] AndroidKeyframes
        {
            get => GetKeyframes(HapticRhythmPlatform.Android);
            set
            {
                _androidKeyframes = value ?? Array.Empty<HapticKeyframe>();
                _hasAndroidTimeline = true;
            }
        }

        public HapticKeyframe[] IosKeyframes
        {
            get => GetKeyframes(HapticRhythmPlatform.Ios);
            set
            {
                _iosKeyframes = value ?? Array.Empty<HapticKeyframe>();
                _hasIosTimeline = true;
            }
        }

        public static HapticRhythmPlatform ActivePlatform
        {
            get
            {
#if UNITY_IOS
                return HapticRhythmPlatform.Ios;
#else
                return HapticRhythmPlatform.Android;
#endif
            }
        }

        public HapticRhythmTemplate TemplateSeed => _templateSeed;

        public bool HasTemplateSeed => _templateSeed != HapticRhythmTemplate.Custom;

        public bool ScaleIntensityWithDistance => _scaleIntensityWithDistance;

        public string DisplayLabel =>
            string.IsNullOrEmpty(_displayName) ? _id : _displayName;

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns the end time of the latest keyframe (or the stored duration hint).
        /// </summary>
        public float ComputeDuration() => ComputeDuration(ActivePlatform);

        public float ComputeDuration(HapticRhythmPlatform platform)
        {
            float maxEnd = 0f;
            HapticKeyframe[] keyframes = GetKeyframes(platform);
            for (int index = 0; index < keyframes.Length; index++)
            {
                float end = keyframes[index].DurationEnd;
                if (end > maxEnd) maxEnd = end;
            }
            return maxEnd > 0f ? maxEnd : Mathf.Max(MinDuration, _durationSeconds);
        }

        public HapticKeyframe[] GetKeyframes(HapticRhythmPlatform platform)
        {
            bool isIos = platform == HapticRhythmPlatform.Ios;
            bool hasPlatformTimeline = isIos
                ? _hasIosTimeline
                : _hasAndroidTimeline;
            if (hasPlatformTimeline)
            {
                return (isIos ? _iosKeyframes : _androidKeyframes)
                    ?? Array.Empty<HapticKeyframe>();
            }

            return _keyframes ?? Array.Empty<HapticKeyframe>();
        }

        public bool MigrateLegacyKeyframes()
        {
            HapticKeyframe[] legacy = _keyframes ?? Array.Empty<HapticKeyframe>();
            bool changed = false;
            if (!_hasAndroidTimeline)
            {
                if (_androidKeyframes == null || _androidKeyframes.Length == 0)
                {
                    _androidKeyframes = Clone(legacy);
                }
                _hasAndroidTimeline = true;
                changed = true;
            }
            if (!_hasIosTimeline)
            {
                if (_iosKeyframes == null || _iosKeyframes.Length == 0)
                {
                    _iosKeyframes = Clone(legacy);
                }
                _hasIosTimeline = true;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Refreshes the stored duration hint from the timeline.
        /// </summary>
        public void RefreshDuration()
        {
            _durationSeconds = ComputeDuration();
        }

        /// <summary>
        /// Logs warnings for overlapping or out-of-order keyframes.
        /// </summary>
        public void Validate()
        {
            ValidateTimeline(GetKeyframes(HapticRhythmPlatform.Android), "Android");
            ValidateTimeline(GetKeyframes(HapticRhythmPlatform.Ios), "iOS");
        }

        private void ValidateTimeline(HapticKeyframe[] keyframes, string platform)
        {
            for (int index = 1; index < keyframes.Length; index++)
            {
                if (keyframes[index].TimeSeconds < keyframes[index - 1].TimeSeconds)
                {
                    QuickLog.Warning<HapticRhythm>(
                        "Rhythm '{0}' ({1}): keyframe {2} is out of time order ({3:F2} after {4:F2}).",
                        _id, platform, index, keyframes[index].TimeSeconds,
                        keyframes[index - 1].TimeSeconds);
                }

                if (keyframes[index].TimeSeconds < keyframes[index - 1].DurationEnd)
                {
                    QuickLog.Warning<HapticRhythm>(
                        "Rhythm '{0}' ({1}): keyframe {2} overlaps the previous keyframe.",
                        _id, platform, index);
                }
            }
        }

        private static HapticKeyframe[] Clone(HapticKeyframe[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<HapticKeyframe>();
            }

            var clone = new HapticKeyframe[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        /// <summary>
        /// Records the template a rhythm was seeded from. Called by the factory
        /// and the editor's 'Add from Template' flow.
        /// </summary>
        public void SetTemplateSeed(HapticRhythmTemplate template)
        {
            _templateSeed = template;
        }

        /// <summary>
        /// Clears the template seed tag. Called by the editor when keyframes change.
        /// </summary>
        public void ClearTemplateSeed()
        {
            _templateSeed = HapticRhythmTemplate.Custom;
        }

        #endregion
    }
}
