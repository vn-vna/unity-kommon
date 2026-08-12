using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// Converts raw module values into colors. Supports grayscale, gradient
    /// ramps and 4-channel packing, plus a shared post-processing chain
    /// (normalize -> contrast -> gamma -> quantize -> invert -> threshold).
    /// </summary>
    public static class ColorMapper
    {
        /// <summary>
        /// Maps sampled values to a color. Allocation-free (spans).
        /// </summary>
        /// <param name="settings">Bake settings (color mode + post chain).</param>
        /// <param name="values">1 value for Grayscale/Gradient, 4 for ChannelPack.</param>
        /// <param name="mins">Per-value min used by normalization (0 when normalize is off).</param>
        /// <param name="maxs">Per-value max used by normalization (1 when normalize is off).</param>
        public static Color Map(
            NoiseBakerSettings settings,
            ReadOnlySpan<float> values,
            ReadOnlySpan<float> mins,
            ReadOnlySpan<float> maxs
        )
        {
            // Single-source modes use channel R's post chain; the post chain
            // lives per channel so there is no separate global post UI.
            ChannelSettings channel0 = settings.channels != null && settings.channels.Length > 0
                ? settings.channels[0]
                : null;

            switch (settings.colorMode)
            {
                case ColorMode.Gradient:
                {
                    float v = channel0 != null
                        ? PostChannel(channel0, values[0], mins[0], maxs[0])
                        : Post(settings, values[0], mins[0], maxs[0]);
                    Color c = settings.gradient != null
                        ? settings.gradient.Evaluate(v)
                        : new Color(v, v, v, 1f);
                    c.a = 1f;
                    return c;
                }

                case ColorMode.ChannelPack:
                {
                    float r = 0f;
                    float g = 0f;
                    float b = 0f;
                    float a = 1f;
                    ChannelSettings[] channels = settings.channels;
                    if (channels != null)
                    {
                        for (int i = 0; i < 4 && i < values.Length; i++)
                        {
                            ChannelSettings channel = channels[i];
                            if (channel == null || !channel.enabled)
                            {
                                continue;
                            }

                            float v = PostChannel(channel, values[i], mins[i], maxs[i]);
                            switch (i)
                            {
                                case 0: r = v; break;
                                case 1: g = v; break;
                                case 2: b = v; break;
                                case 3: a = v; break;
                            }
                        }
                    }

                    return new Color(r, g, b, a);
                }

                default:
                {
                    float v = channel0 != null
                        ? PostChannel(channel0, values[0], mins[0], maxs[0])
                        : Post(settings, values[0], mins[0], maxs[0]);
                    return new Color(v, v, v, 1f);
                }
            }
        }

        /// <summary>Global post chain for Grayscale / Gradient modes.</summary>
        public static float Post(NoiseBakerSettings settings, float raw, float min, float max)
        {
            float v = Normalize(settings.normalize, raw, min, max);
            v = ApplyContrast(v, settings.contrast);
            v = ApplyGamma(v, settings.gamma);
            v = Quantize(v, settings.quantizeSteps);
            v = ApplyInvert(v, settings.invert);
            v = Threshold(v, settings.threshold, settings.thresholdSoftness);
            return Mathf.Clamp01(v);
        }

        /// <summary>Per-channel post chain used by ChannelPack mode.</summary>
        public static float PostChannel(ChannelSettings channel, float raw, float min, float max)
        {
            float v = Normalize(channel.normalize, raw, min, max);
            v = ApplyContrast(v, channel.contrast);
            v = ApplyGamma(v, channel.gamma);
            v = Quantize(v, channel.quantizeSteps);
            v = ApplyInvert(v, channel.invert);
            v = Threshold(v, channel.threshold, channel.thresholdSoftness);
            return Mathf.Clamp01(v);
        }

        private static float Normalize(bool enabled, float raw, float min, float max)
        {
            if (!enabled)
            {
                return raw;
            }

            float span = max - min;
            if (span < 0.000001f)
            {
                return raw;
            }

            return (raw - min) / span;
        }

        private static float ApplyContrast(float v, float contrast)
        {
            return Mathf.Approximately(contrast, 1f)
                ? v
                : (v - 0.5f) * contrast + 0.5f;
        }

        private static float ApplyGamma(float v, float gamma)
        {
            return Mathf.Approximately(gamma, 1f) || gamma <= 0f
                ? v
                : Mathf.Pow(Mathf.Clamp01(v), 1f / gamma);
        }

        private static float Quantize(float v, int steps)
        {
            if (steps <= 1)
            {
                return v;
            }

            return Mathf.RoundToInt(v * (steps - 1)) / (steps - 1f);
        }

        private static float ApplyInvert(float v, bool invert)
        {
            return invert ? 1f - v : v;
        }

        private static float Threshold(float v, float threshold, float softness)
        {
            if (threshold <= 0f)
            {
                return v;
            }

            if (softness <= 0f)
            {
                return v >= threshold ? 1f : 0f;
            }

            float t = Mathf.Clamp01((v - (threshold - softness * 0.5f)) / Mathf.Max(0.000001f, softness));
            return t * t * (3f - 2f * t);
        }
    }
}
