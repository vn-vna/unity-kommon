using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Defines the keyframe timelines behind each <see cref="HapticRhythmTemplate"/>.
    /// All templates are allocation-free (static arrays) and return clones
    /// so callers can mutate them.
    /// </summary>
    public static class HapticRhythmTemplates
    {
        #region Public Methods

        public static string GetName(HapticRhythmTemplate template)
        {
            return template switch
            {
                HapticRhythmTemplate.Custom => "Custom",
                HapticRhythmTemplate.LightTap => "Light Tap",
                HapticRhythmTemplate.MediumTap => "Medium Tap",
                HapticRhythmTemplate.HeavyTap => "Heavy Tap",
                HapticRhythmTemplate.AlertTap => "Alert Tap",
                HapticRhythmTemplate.SelectionTick => "Selection Tick",
                HapticRhythmTemplate.NotificationSuccess => "Notification Success",
                HapticRhythmTemplate.NotificationWarning => "Notification Warning",
                HapticRhythmTemplate.NotificationError => "Notification Error",
                HapticRhythmTemplate.DoubleTap => "Double Tap",
                HapticRhythmTemplate.TriplePulse => "Triple Pulse",
                HapticRhythmTemplate.Burst => "Burst",
                _ => template.ToString()
            };
        }

        public static string GetDescription(HapticRhythmTemplate template)
        {
            return template switch
            {
                HapticRhythmTemplate.LightTap => "UI buttons, subtle feedback",
                HapticRhythmTemplate.MediumTap => "Tile moves, board interactions",
                HapticRhythmTemplate.HeavyTap => "Matches, big events",
                HapticRhythmTemplate.AlertTap => "Wrong move, warnings",
                HapticRhythmTemplate.SelectionTick => "List scroll, snapping",
                HapticRhythmTemplate.NotificationSuccess => "Level complete",
                HapticRhythmTemplate.NotificationWarning => "Caution, retries",
                HapticRhythmTemplate.NotificationError => "Failures",
                HapticRhythmTemplate.DoubleTap => "Confirmation, double-tap",
                HapticRhythmTemplate.TriplePulse => "Combo chains",
                HapticRhythmTemplate.Burst => "Explosion, danger (continuous)",
                _ => "Blank timeline, designer builds it"
            };
        }

        /// <summary>
        /// Primary waveform for single-tap presets; keeps <see cref="Haptics.Cue"/>
        /// and templated rhythms consistent.
        /// </summary>
        public static HapticWaveformType GetWaveform(HapticRhythmTemplate template)
        {
            return template switch
            {
                HapticRhythmTemplate.LightTap => HapticWaveformType.LightImpact,
                HapticRhythmTemplate.MediumTap => HapticWaveformType.MediumImpact,
                HapticRhythmTemplate.HeavyTap => HapticWaveformType.HeavyImpact,
                HapticRhythmTemplate.AlertTap => HapticWaveformType.NotificationWarning,
                HapticRhythmTemplate.SelectionTick => HapticWaveformType.SelectionTick,
                HapticRhythmTemplate.NotificationSuccess => HapticWaveformType.NotificationSuccess,
                HapticRhythmTemplate.NotificationWarning => HapticWaveformType.NotificationWarning,
                HapticRhythmTemplate.NotificationError => HapticWaveformType.NotificationError,
                HapticRhythmTemplate.DoubleTap => HapticWaveformType.LightImpact,
                HapticRhythmTemplate.TriplePulse => HapticWaveformType.MediumImpact,
                HapticRhythmTemplate.Burst => HapticWaveformType.VibrationBurst,
                _ => HapticWaveformType.Custom
            };
        }

        /// <summary>
        /// Returns a fresh copy of the template's keyframes (sorted by time).
        /// </summary>
        public static HapticKeyframe[] CreateKeyframes(HapticRhythmTemplate template)
        {
            switch (template)
            {
                case HapticRhythmTemplate.LightTap:
                    return new[] { new HapticKeyframe(0f, 0.05f, 0.30f, 0.5f, HapticWaveformType.LightImpact) };

                case HapticRhythmTemplate.MediumTap:
                    return new[] { new HapticKeyframe(0f, 0.08f, 0.60f, 0.5f, HapticWaveformType.MediumImpact) };

                case HapticRhythmTemplate.HeavyTap:
                    return new[] { new HapticKeyframe(0f, 0.12f, 1.00f, 0.5f, HapticWaveformType.HeavyImpact) };

                case HapticRhythmTemplate.AlertTap:
                    return new[] { new HapticKeyframe(0f, 0.15f, 0.80f, 0.5f, HapticWaveformType.NotificationWarning) };

                case HapticRhythmTemplate.SelectionTick:
                    return new[] { new HapticKeyframe(0f, 0.03f, 0.40f, 0.5f, HapticWaveformType.SelectionTick) };

                case HapticRhythmTemplate.NotificationSuccess:
                    return new[] { new HapticKeyframe(0f, 0.10f, 0.70f, 0.5f, HapticWaveformType.NotificationSuccess) };

                case HapticRhythmTemplate.NotificationWarning:
                    return new[] { new HapticKeyframe(0f, 0.15f, 0.80f, 0.5f, HapticWaveformType.NotificationWarning) };

                case HapticRhythmTemplate.NotificationError:
                    return new[] { new HapticKeyframe(0f, 0.20f, 0.90f, 0.5f, HapticWaveformType.NotificationError) };

                case HapticRhythmTemplate.DoubleTap:
                    return new[]
                    {
                        new HapticKeyframe(0.00f, 0.05f, 0.30f, 0.5f, HapticWaveformType.LightImpact),
                        new HapticKeyframe(0.12f, 0.05f, 0.30f, 0.5f, HapticWaveformType.LightImpact)
                    };

                case HapticRhythmTemplate.TriplePulse:
                    return new[]
                    {
                        new HapticKeyframe(0.00f, 0.06f, 0.50f, 0.5f, HapticWaveformType.MediumImpact),
                        new HapticKeyframe(0.10f, 0.06f, 0.50f, 0.5f, HapticWaveformType.MediumImpact),
                        new HapticKeyframe(0.20f, 0.06f, 0.50f, 0.5f, HapticWaveformType.MediumImpact)
                    };

                case HapticRhythmTemplate.Burst:
                    return new[] { new HapticKeyframe(0f, 0.30f, 0.80f, 0.5f, HapticWaveformType.VibrationBurst) };

                default:
                    return Array.Empty<HapticKeyframe>();
            }
        }

        /// <summary>
        /// Creates a HapticRhythm pre-populated with the template's keyframes.
        /// The rhythm is a raw instance (not yet added to an asset).
        /// </summary>
        public static HapticRhythm CreateRhythm(
            string id,
            string displayName,
            HapticRhythmTemplate template)
        {
            HapticRhythm rhythm = ScriptableObject.CreateInstance<HapticRhythm>();
            rhythm.Id = id;
            rhythm.DisplayName = string.IsNullOrEmpty(displayName) ? GetName(template) : displayName;
            rhythm.Keyframes = CreateKeyframes(template);
            rhythm.RefreshDuration();
            rhythm.SetTemplateSeed(template);
            return rhythm;
        }

        #endregion
    }
}
