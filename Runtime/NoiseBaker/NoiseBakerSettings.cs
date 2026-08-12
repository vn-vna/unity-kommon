using System;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>Sub-settings for <see cref="FractalNoise"/>.</summary>
    [Serializable]
    public class FractalSettings
    {
        public NoiseModuleType baseType = NoiseModuleType.Perlin;
        public int octaves = 5;
        public float lacunarity = 2f;
        public float gain = 0.5f;
        public FractalType type = FractalType.Fbm;
        public float ridgeOffset = 0f;
        public bool normalize = true;

        public FractalSettings Clone()
        {
            return (FractalSettings)MemberwiseClone();
        }

        public string GetKey()
        {
            return string.Concat(
                baseType, "|", octaves, "|", lacunarity.ToString("0.###"), "|",
                gain.ToString("0.###"), "|", type, "|", ridgeOffset.ToString("0.###"), "|", normalize
            );
        }
    }

    /// <summary>Sub-settings for <see cref="VoronoiNoise"/>.</summary>
    [Serializable]
    public class VoronoiSettings
    {
        public int cellCount = 16;
        public float jitter = 0.8f;
        public VoronoiDistanceMetric metric = VoronoiDistanceMetric.Euclidean;
        public float exponent = 2f;
        public VoronoiFeature feature = VoronoiFeature.Cell;
        public float borderWidth = 0.05f;
        public float borderSoftness = 0.2f;
        public bool normalizeDistances = true;

        public VoronoiSettings Clone()
        {
            return (VoronoiSettings)MemberwiseClone();
        }

        public string GetKey()
        {
            return string.Concat(
                cellCount, "|", jitter.ToString("0.###"), "|", metric, "|",
                exponent.ToString("0.###"), "|", feature, "|",
                borderWidth.ToString("0.###"), "|", borderSoftness.ToString("0.###"), "|", normalizeDistances
            );
        }
    }

    /// <summary>Per-channel configuration: module, generation params and post chain.</summary>
    [Serializable]
    public class ChannelSettings
    {
        public bool enabled = true;
        public NoiseModuleType moduleType = NoiseModuleType.Perlin;
        public NoiseModuleType baseType = NoiseModuleType.Perlin;
        public float frequency = 4f;
        public int seed = 1337;

        /// <summary>Per-channel fractal parameters (used when moduleType == Fractal).</summary>
        public FractalSettings fractal = new FractalSettings();

        /// <summary>Per-channel voronoi parameters (used when moduleType == Voronoi).</summary>
        public VoronoiSettings voronoi = new VoronoiSettings();

        public bool normalize = true;
        public float contrast = 1f;
        public float gamma = 1f;
        public int quantizeSteps = 0;
        public bool invert = false;
        public float threshold = 0f;
        public float thresholdSoftness = 0.1f;

        public ChannelSettings Clone()
        {
            ChannelSettings copy = (ChannelSettings)MemberwiseClone();
            copy.fractal = fractal?.Clone();
            copy.voronoi = voronoi?.Clone();
            return copy;
        }

        public string GetKey()
        {
            return string.Concat(
                enabled, "|", moduleType, "|", baseType, "|",
                frequency.ToString("0.###"), "|", seed, "|",
                fractal?.GetKey() ?? "-", "|",
                voronoi?.GetKey() ?? "-"
            );
        }
    }

    /// <summary>
    /// Full serializable parameter model for one texture bake.
    /// Shared by the editor window, preset assets and the runtime API.
    /// </summary>
    [Serializable]
    public class NoiseBakerSettings
    {
        // ---- Output ----
        public int width = 512;
        public int height = 512;
        public TextureFormat format = TextureFormat.RGBA32;
        public bool mipmaps = true;
        public FilterMode filterMode = FilterMode.Bilinear;
        public TextureWrapMode wrapMode = TextureWrapMode.Repeat;

        // ---- Domain ----
        public DomainMode domain = DomainMode.None;
        public float frequency = 4f;
        public int seed = 1337;

        // ---- Module ----
        public NoiseModuleType moduleType = NoiseModuleType.Perlin;
        public FractalSettings fractal = new FractalSettings();
        public VoronoiSettings voronoi = new VoronoiSettings();

        // ---- Color ----
        public ColorMode colorMode = ColorMode.Grayscale;
        public Gradient gradient = CreateDefaultGradient();
        public ChannelSettings[] channels = CreateDefaultChannels();

        // ---- Post ----
        public bool normalize = true;
        public float contrast = 1f;
        public float gamma = 1f;
        public int quantizeSteps = 0;
        public bool invert = false;
        public float threshold = 0f;
        public float thresholdSoftness = 0.1f;

        /// <summary>Deep clone. Bake() operates on a clone so the caller's object is never mutated.</summary>
        public NoiseBakerSettings Clone()
        {
            NoiseBakerSettings copy = (NoiseBakerSettings)MemberwiseClone();
            copy.fractal = fractal?.Clone();
            copy.voronoi = voronoi?.Clone();
            if (channels != null)
            {
                copy.channels = new ChannelSettings[channels.Length];
                for (int i = 0; i < channels.Length; i++)
                {
                    copy.channels[i] = channels[i]?.Clone();
                }
            }

            return copy;
        }

        /// <summary>Returns a sanitized clone ready for baking.</summary>
        public NoiseBakerSettings Sanitized()
        {
            NoiseBakerSettings copy = Clone();
            copy.Sanitize();
            return copy;
        }

        /// <summary>Clamps every field and fixes inconsistent combinations in-place.</summary>
        public void Sanitize()
        {
            width = Mathf.Clamp(width, 1, 8192);
            height = Mathf.Clamp(height, 1, 8192);
            frequency = Mathf.Max(0.01f, frequency);
            seed = Mathf.Clamp(seed, int.MinValue, int.MaxValue);
            contrast = Mathf.Clamp(contrast, 0.1f, 5f);
            gamma = Mathf.Clamp(gamma, 0.2f, 5f);
            quantizeSteps = Mathf.Clamp(quantizeSteps, 0, 256);
            threshold = Mathf.Clamp(threshold, 0f, 1f);
            thresholdSoftness = Mathf.Clamp(thresholdSoftness, 0f, 1f);

            if (domain == DomainMode.TorusWrap)
            {
                int rounded = Mathf.RoundToInt(frequency);
                if (Mathf.Abs(frequency - rounded) > 0.0001f)
                {
                    QuickLog.Warning<NoiseBakerSettings>(
                        "Tileable (TorusWrap) bakes require an integer frequency for a perfect seam; rounding {0} to {1}.",
                        frequency, rounded
                    );
                    frequency = rounded;
                }

                if (frequency < 2f)
                {
                    frequency = 2f;
                }

                if (wrapMode != TextureWrapMode.Repeat)
                {
                    wrapMode = TextureWrapMode.Repeat;
                }
            }

            if (fractal != null)
            {
                ClampFractal(fractal);
            }

            if (voronoi != null)
            {
                ClampVoronoi(voronoi);
            }

            if (gradient == null)
            {
                gradient = CreateDefaultGradient();
            }

            if (channels == null || channels.Length != 4)
            {
                channels = CreateDefaultChannels();
            }
            else
            {
                for (int i = 0; i < channels.Length; i++)
                {
                    if (channels[i] == null)
                    {
                        channels[i] = new ChannelSettings();
                    }
                }
            }

            for (int i = 0; i < channels.Length; i++)
            {
                ChannelSettings channel = channels[i];
                if (channel.fractal == null)
                {
                    channel.fractal = new FractalSettings();
                }

                if (channel.voronoi == null)
                {
                    channel.voronoi = new VoronoiSettings();
                }

                ClampFractal(channel.fractal);
                ClampVoronoi(channel.voronoi);
            }

            // Grayscale/Gradient bakes use channel R as their single source.
            if (colorMode != ColorMode.ChannelPack)
            {
                channels[0].enabled = true;
            }

            if (!SystemInfo.SupportsTextureFormat(format))
            {
                QuickLog.Warning<NoiseBakerSettings>(
                    "Texture format {0} is not supported on this platform; falling back to RGBA32.",
                    format
                );
                format = TextureFormat.RGBA32;
            }
        }

        /// <summary>
        /// Key identifying everything the module graph depends on for a bake.
        /// The module graph is always built from the per-channel settings,
        /// so channel keys are always included (not only in ChannelPack mode).
        /// </summary>
        public string GetCacheKey()
        {
            return string.Join(
                "|",
                domain,
                fractal?.GetKey() ?? "-",
                voronoi?.GetKey() ?? "-",
                channels != null
                    ? string.Join(",", Array.ConvertAll(channels, c => c?.GetKey() ?? "-"))
                    : "-"
            );
        }

        /// <summary>
        /// Key for the legacy global module path (<see cref="moduleType"/>),
        /// kept for backward compatibility of <c>BuildModule</c>.
        /// </summary>
        public string GetModuleCacheKey()
        {
            return string.Join(
                "|",
                "g",
                moduleType,
                domain,
                frequency.ToString("0.###"),
                seed,
                fractal?.GetKey() ?? "-",
                voronoi?.GetKey() ?? "-"
            );
        }

        private static void ClampFractal(FractalSettings fractal)
        {
            fractal.octaves = Mathf.Clamp(fractal.octaves, 1, 12);
            fractal.lacunarity = Mathf.Clamp(fractal.lacunarity, 1.01f, 4f);
            fractal.gain = Mathf.Clamp(fractal.gain, 0.1f, 1f);
            fractal.ridgeOffset = Mathf.Clamp(fractal.ridgeOffset, 0f, 2f);
        }

        private static void ClampVoronoi(VoronoiSettings voronoi)
        {
            voronoi.cellCount = Mathf.Clamp(voronoi.cellCount, 1, 512);
            voronoi.jitter = Mathf.Clamp(voronoi.jitter, 0f, 0.95f);
            voronoi.exponent = Mathf.Clamp(voronoi.exponent, 1f, 8f);
            voronoi.borderWidth = Mathf.Clamp(voronoi.borderWidth, 0f, 0.5f);
            voronoi.borderSoftness = Mathf.Clamp(voronoi.borderSoftness, 0f, 1f);
        }

        private static Gradient CreateDefaultGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.black, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                }
            );
            return gradient;
        }

        private static ChannelSettings[] CreateDefaultChannels()
        {
            ChannelSettings[] result = new ChannelSettings[4];
            for (int i = 0; i < 4; i++)
            {
                result[i] = new ChannelSettings { seed = 1337 + i * 7919 };
            }

            return result;
        }
    }
}
