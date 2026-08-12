using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// A single timeline keyframe of a haptic rhythm.
    /// </summary>
    [Serializable]
    public struct HapticKeyframe
    {
        [SerializeField] private float _timeSeconds;
        [SerializeField] private float _durationSeconds;
        [Range(0f, 1f)] [SerializeField] private float _intensity;
        [Range(0f, 1f)] [SerializeField] private float _frequency;
        [SerializeField] private HapticWaveformType _waveform;

        public HapticKeyframe(
            float timeSeconds = 0f,
            float durationSeconds = 0.05f,
            float intensity = 1f,
            float frequency = 0.5f,
            HapticWaveformType waveform = HapticWaveformType.LightImpact)
        {
            _timeSeconds = timeSeconds;
            _durationSeconds = durationSeconds;
            _intensity = intensity;
            _frequency = frequency;
            _waveform = waveform;
        }

        public float TimeSeconds => _timeSeconds;
        public float DurationSeconds => _durationSeconds;
        public float Intensity => _intensity;
        public float Frequency => _frequency;
        public HapticWaveformType Waveform => _waveform;

        public float DurationEnd => _timeSeconds + _durationSeconds;

        public bool IsAt(float t) => Mathf.Approximately(_timeSeconds, t);
    }
}
