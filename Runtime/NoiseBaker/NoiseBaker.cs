using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// Static facade for the NoiseBaker module. Entry point for both the
    /// runtime API (Bake / BakeAsync / Sample) and the editor window.
    /// All module instances are cached per settings key.
    /// </summary>
    public static class NoiseBaker
    {
        private static readonly Dictionary<string, INoiseSource> ModuleCache = new Dictionary<string, INoiseSource>();
        private static readonly object CacheLock = new object();

        /// <summary>Bakes a texture synchronously. Call on the main thread.</summary>
        public static Texture2D Bake(NoiseBakerSettings settings)
        {
            return Bake(settings, null, default);
        }

        /// <summary>Bakes a texture with progress reporting and cancellation.</summary>
        public static Texture2D Bake(
            NoiseBakerSettings settings,
            IProgress<float> progress,
            CancellationToken cancellationToken
        )
        {
            NoiseBakerSettings sanitized = settings.Sanitized();
            INoiseSource[] sources = BuildSources(sanitized);
            return TextureBaker.Bake(sanitized, sources, progress, cancellationToken);
        }

        /// <summary>
        /// Bakes asynchronously: sampling runs on a background thread and the
        /// texture is written on the Unity main thread when the task completes.
        /// </summary>
        public static async Task<Texture2D> BakeAsync(
            NoiseBakerSettings settings,
            CancellationToken cancellationToken = default
        )
        {
            NoiseBakerSettings sanitized = settings.Sanitized();
            INoiseSource[] sources = BuildSources(sanitized);
            return await TextureBaker.BakeAsync(sanitized, sources, null, cancellationToken);
        }

        /// <summary>
        /// Samples a single point (normalized UV, 0..1). Note: min/max
        /// normalization is a texture-level operation and becomes identity here.
        /// </summary>
        public static float Sample(NoiseBakerSettings settings, float u, float v)
        {
            NoiseBakerSettings sanitized = settings.Sanitized();
            INoiseSource source = BuildModule(sanitized);
            return SampleValue(sanitized, source, u, v);
        }

        /// <summary>Single-point color sample (respects color mode and post chain).</summary>
        public static Color SampleColor(NoiseBakerSettings settings, float u, float v)
        {
            NoiseBakerSettings sanitized = settings.Sanitized();
            INoiseSource[] sources = BuildSources(sanitized);
            int count = sources.Length;
            float[] values = new float[count];
            float[] mins = new float[count];
            float[] maxs = new float[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = sources[i] != null ? SampleValue(sanitized, sources[i], u, v) : 0f;
                mins[i] = 0f;
                maxs[i] = 1f;
            }

            return ColorMapper.Map(sanitized, values, mins, maxs);
        }

        /// <summary>
        /// Builds (and caches) the module graph for a sanitized settings object.
        /// The graph is always built from the per-channel settings:
        /// one source (channel R) for Grayscale/Gradient, four for ChannelPack.
        /// </summary>
        public static INoiseSource[] BuildSources(NoiseBakerSettings settings)
        {
            ChannelSettings[] channels = settings.channels ?? new ChannelSettings[4];
            if (settings.colorMode != ColorMode.ChannelPack)
            {
                ChannelSettings channel = channels[0] ?? new ChannelSettings();
                return new[] { BuildChannelModule(channel, settings) };
            }

            INoiseSource[] sources = new INoiseSource[4];
            for (int i = 0; i < 4; i++)
            {
                ChannelSettings channel = channels[i];
                sources[i] = channel != null && channel.enabled
                    ? BuildChannelModule(channel, settings)
                    : null;
            }

            return sources;
        }

        /// <summary>
        /// Legacy: builds the module from the global <see cref="NoiseBakerSettings.moduleType"/>
        /// fields. The bake pipeline uses the per-channel sources instead.
        /// </summary>
        public static INoiseSource BuildModule(NoiseBakerSettings settings)
        {
            string key = settings.GetModuleCacheKey();
            lock (CacheLock)
            {
                if (ModuleCache.TryGetValue(key, out INoiseSource cached))
                {
                    return cached;
                }
            }

            bool tileable = settings.domain == DomainMode.TorusWrap;
            INoiseSource module = settings.moduleType switch
            {
                NoiseModuleType.Value => new ValueNoise(settings.seed, settings.frequency, tileable),
                NoiseModuleType.Simplex => new SimplexNoise(settings.seed, settings.frequency, tileable),
                NoiseModuleType.Voronoi => new VoronoiNoise(
                    settings.seed,
                    settings.voronoi?.cellCount ?? 16,
                    settings.voronoi?.jitter ?? 0.8f,
                    settings.voronoi?.metric ?? VoronoiDistanceMetric.Euclidean,
                    settings.voronoi?.exponent ?? 2f,
                    settings.voronoi?.feature ?? VoronoiFeature.Cell,
                    settings.voronoi?.borderWidth ?? 0.05f,
                    settings.voronoi?.borderSoftness ?? 0.2f,
                    tileable,
                    settings.voronoi?.normalizeDistances ?? true
                ),
                NoiseModuleType.Fractal => new FractalNoise(
                    settings.fractal?.baseType ?? NoiseModuleType.Perlin,
                    settings.frequency,
                    settings.fractal?.octaves ?? 5,
                    settings.fractal?.lacunarity ?? 2f,
                    settings.fractal?.gain ?? 0.5f,
                    settings.fractal?.type ?? FractalType.Fbm,
                    settings.fractal?.ridgeOffset ?? 0f,
                    settings.seed,
                    tileable,
                    settings.fractal?.normalize ?? true
                ),
                _ => new PerlinNoise(settings.seed, settings.frequency, tileable),
            };

            lock (CacheLock)
            {
                ModuleCache[key] = module;
            }

            return module;
        }

        private static INoiseSource BuildChannelModule(ChannelSettings channel, NoiseBakerSettings settings)
        {
            bool tileable = settings.domain == DomainMode.TorusWrap;
            string key = string.Join(
                "|",
                "c",
                tileable,
                channel.GetKey(),
                settings.fractal?.GetKey() ?? "-",
                settings.voronoi?.GetKey() ?? "-"
            );
            lock (CacheLock)
            {
                if (ModuleCache.TryGetValue(key, out INoiseSource cached))
                {
                    return cached;
                }
            }

            INoiseSource module = BuildChannelModuleUncached(channel, settings, tileable);
            lock (CacheLock)
            {
                ModuleCache[key] = module;
            }

            return module;
        }

        private static INoiseSource BuildChannelModuleUncached(ChannelSettings channel, NoiseBakerSettings settings, bool tileable)
        {
            // Per-channel parameter sets; fall back to the legacy global sets
            // for presets serialized before channels owned their own params.
            FractalSettings fractal = channel.fractal ?? settings.fractal;
            VoronoiSettings voronoi = channel.voronoi ?? settings.voronoi;

            switch (channel.moduleType)
            {
                case NoiseModuleType.Value:
                    return new ValueNoise(channel.seed, channel.frequency, tileable);

                case NoiseModuleType.Simplex:
                    return new SimplexNoise(channel.seed, channel.frequency, tileable);

                case NoiseModuleType.Voronoi:
                    return new VoronoiNoise(
                        channel.seed,
                        voronoi?.cellCount ?? 16,
                        voronoi?.jitter ?? 0.8f,
                        voronoi?.metric ?? VoronoiDistanceMetric.Euclidean,
                        voronoi?.exponent ?? 2f,
                        voronoi?.feature ?? VoronoiFeature.Cell,
                        voronoi?.borderWidth ?? 0.05f,
                        voronoi?.borderSoftness ?? 0.2f,
                        tileable,
                        voronoi?.normalizeDistances ?? true
                    );

                case NoiseModuleType.Fractal:
                    return new FractalNoise(
                        channel.baseType,
                        channel.frequency,
                        fractal?.octaves ?? 5,
                        fractal?.lacunarity ?? 2f,
                        fractal?.gain ?? 0.5f,
                        fractal?.type ?? FractalType.Fbm,
                        fractal?.ridgeOffset ?? 0f,
                        channel.seed,
                        tileable,
                        fractal?.normalize ?? true
                    );

                default:
                    return new PerlinNoise(channel.seed, channel.frequency, tileable);
            }
        }

        private static float SampleValue(NoiseBakerSettings settings, INoiseSource source, float u, float v)
        {
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            if (settings.domain == DomainMode.Mirror)
            {
                ICoordinateMapper mapper = CoordinateMapperFactory.Create(settings.domain);
                mapper.MapNormalized(u, v, out u, out v);
            }

            return source.Sample2D(u, v);
        }
    }
}
