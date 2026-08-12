using System;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
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

        [Tooltip("The timeline; kept sorted by timeSeconds")]
        [SerializeField] private HapticKeyframe[] _keyframes = Array.Empty<HapticKeyframe>();

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
            get => _keyframes ?? Array.Empty<HapticKeyframe>();
            set => _keyframes = value ?? Array.Empty<HapticKeyframe>();
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
        public float ComputeDuration()
        {
            float maxEnd = 0f;
            HapticKeyframe[] kfs = Keyframes;
            for (int i = 0; i < kfs.Length; i++)
            {
                float end = kfs[i].DurationEnd;
                if (end > maxEnd) maxEnd = end;
            }
            return maxEnd > 0f ? maxEnd : Mathf.Max(MinDuration, _durationSeconds);
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
            HapticKeyframe[] kfs = Keyframes;
            for (int i = 1; i < kfs.Length; i++)
            {
                if (kfs[i].TimeSeconds < kfs[i - 1].TimeSeconds)
                {
                    QuickLog.Warning<HapticRhythm>(
                        "Rhythm '{0}': keyframe {1} is out of time order ({2:F2} after {3:F2}).",
                        _id, i, kfs[i].TimeSeconds, kfs[i - 1].TimeSeconds);
                }

                if (kfs[i].TimeSeconds < kfs[i - 1].DurationEnd)
                {
                    QuickLog.Warning<HapticRhythm>(
                        "Rhythm '{0}': keyframe {1} overlaps the previous keyframe.",
                        _id, i);
                }
            }
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
