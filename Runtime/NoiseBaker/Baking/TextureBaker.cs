using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// Parallel bake engine. Pure data processing: samples the module graph
    /// for every pixel (parallel over scanline rows), then writes the result
    /// into a Texture2D on the calling (main) thread.
    /// The sampling hot loop is allocation-free (stackalloc spans).
    /// </summary>
    public static class TextureBaker
    {
        /// <summary>
        /// Synchronous bake. Must be called on the main thread (Texture2D APIs
        /// are not thread-safe). The heavy sampling itself runs on worker threads.
        /// </summary>
        public static Texture2D Bake(
            NoiseBakerSettings settings,
            INoiseSource[] sources,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default
        )
        {
            Color[] pixels = SampleAll(settings, sources, progress, cancellationToken);
            return WriteTexture(settings, pixels);
        }

        /// <summary>
        /// Async bake: samples on a background task, resumes on the Unity main
        /// thread (via the UnitySynchronizationContext) to write the texture.
        /// </summary>
        public static async Task<Texture2D> BakeAsync(
            NoiseBakerSettings settings,
            INoiseSource[] sources,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default
        )
        {
            Color[] pixels = await Task.Run(
                () => SampleAll(settings, sources, progress, cancellationToken),
                cancellationToken
            );
            return WriteTexture(settings, pixels);
        }

        /// <summary>
        /// Pure sampling pass; safe to run on any thread. Returns the raw
        /// color buffer that WriteTexture consumes.
        /// </summary>
        public static Color[] SampleAll(
            NoiseBakerSettings settings,
            INoiseSource[] sources,
            IProgress<float> progress,
            CancellationToken cancellationToken
        )
        {
            int width = settings.width;
            int height = settings.height;
            int sourceCount = sources.Length;

            ICoordinateMapper mapper = CoordinateMapperFactory.Create(settings.domain);

            float[] mins = new float[sourceCount];
            float[] maxs = new float[sourceCount];
            if (settings.normalize || AnyChannelNormalizes(settings))
            {
                ComputeMinMax(settings, sources, mapper, cancellationToken, mins, maxs);
            }
            else
            {
                for (int i = 0; i < sourceCount; i++)
                {
                    mins[i] = 0f;
                    maxs[i] = 1f;
                }
            }

            Color[] pixels = new Color[width * height];
            int totalRows = Mathf.Max(1, height);

            Parallel.For(0, height, y =>
            {
                // Early-return per row: Parallel.For would wrap a thrown
                // OperationCanceledException into an AggregateException, so
                // cancellation is checked after the loop instead.
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                int rowBase = y * width;
                Span<float> values = stackalloc float[sourceCount];

                for (int x = 0; x < width; x++)
                {
                    mapper.Map(x, y, width, height, out float u, out float v);
                    for (int c = 0; c < sourceCount; c++)
                    {
                        values[c] = sources[c] != null
                            ? sources[c].Sample2D(u, v)
                            : 0f;
                    }

                    pixels[rowBase + x] = ColorMapper.Map(settings, values, mins, maxs);
                }

                progress?.Report(y / (float)totalRows);
            });

            // Clean single-threaded cancellation signal.
            cancellationToken.ThrowIfCancellationRequested();

            return pixels;
        }

        /// <summary>Writes a sampled color buffer into a Texture2D (main thread).</summary>
        public static Texture2D WriteTexture(NoiseBakerSettings settings, Color[] pixels)
        {
            Texture2D texture = new Texture2D(settings.width, settings.height, settings.format, settings.mipmaps)
            {
                wrapMode = settings.wrapMode,
                filterMode = settings.filterMode,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(settings.mipmaps);
            return texture;
        }

        private static void ComputeMinMax(
            NoiseBakerSettings settings,
            INoiseSource[] sources,
            ICoordinateMapper mapper,
            CancellationToken cancellationToken,
            float[] mins,
            float[] maxs
        )
        {
            int width = settings.width;
            int height = settings.height;
            int sourceCount = sources.Length;

            float[] rowMin = new float[height * sourceCount];
            float[] rowMax = new float[height * sourceCount];

            Parallel.For(0, height, y =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Span<float> localMin = stackalloc float[sourceCount];
                Span<float> localMax = stackalloc float[sourceCount];
                for (int c = 0; c < sourceCount; c++)
                {
                    localMin[c] = float.MaxValue;
                    localMax[c] = float.MinValue;
                }

                for (int x = 0; x < width; x++)
                {
                    mapper.Map(x, y, width, height, out float u, out float v);
                    for (int c = 0; c < sourceCount; c++)
                    {
                        if (sources[c] == null)
                        {
                            continue;
                        }

                        float value = sources[c].Sample2D(u, v);
                        if (value < localMin[c])
                        {
                            localMin[c] = value;
                        }

                        if (value > localMax[c])
                        {
                            localMax[c] = value;
                        }
                    }
                }

                for (int c = 0; c < sourceCount; c++)
                {
                    rowMin[y * sourceCount + c] = localMin[c];
                    rowMax[y * sourceCount + c] = localMax[c];
                }
            });

            cancellationToken.ThrowIfCancellationRequested();

            for (int c = 0; c < sourceCount; c++)
            {
                float min = float.MaxValue;
                float max = float.MinValue;
                for (int y = 0; y < height; y++)
                {
                    if (rowMin[y * sourceCount + c] < min)
                    {
                        min = rowMin[y * sourceCount + c];
                    }

                    if (rowMax[y * sourceCount + c] > max)
                    {
                        max = rowMax[y * sourceCount + c];
                    }
                }

                mins[c] = min == float.MaxValue ? 0f : min;
                maxs[c] = max == float.MinValue ? 1f : max;
            }
        }

        private static bool AnyChannelNormalizes(NoiseBakerSettings settings)
        {
            if (settings.channels == null)
            {
                return false;
            }

            for (int i = 0; i < settings.channels.Length; i++)
            {
                if (settings.channels[i] != null && settings.channels[i].enabled && settings.channels[i].normalize)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
