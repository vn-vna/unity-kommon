using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>Interpolation fade used by <see cref="ValueNoise"/>.</summary>
    public enum FadeType
    {
        Linear,
        Smoothstep,
        Quintic,
    }

    /// <summary>
    /// Lattice value noise: hashes the four surrounding lattice points and
    /// blends them with a fade. Cheapest module; produces soft blobby patterns.
    /// </summary>
    public sealed class ValueNoise : INoiseSource
    {
        private readonly int _seed;
        private readonly float _frequency;
        private readonly int _period;
        private readonly FadeType _fade;

        /// <param name="seed">Deterministic hash seed.</param>
        /// <param name="frequency">Features across the texture (lattice cells).</param>
        /// <param name="period">Lattice wrap period; 0 = aperiodic, &gt; 0 = seamless torus.</param>
        /// <param name="fade">Interpolation fade function.</param>
        public ValueNoise(int seed, float frequency, int period, FadeType fade = FadeType.Smoothstep)
        {
            _seed = seed;
            _frequency = Mathf.Max(0.0001f, frequency);
            _period = Mathf.Max(0, period);
            _fade = fade;
        }

        /// <summary>Convenience constructor: period is derived from frequency when tileable.</summary>
        public ValueNoise(int seed, float frequency, bool tileable, FadeType fade = FadeType.Smoothstep)
            : this(seed, frequency, tileable ? NoiseBakerUtility.PeriodFor(frequency) : 0, fade)
        {
        }

        public float Sample2D(float u, float v)
        {
            float x = u * _frequency;
            float y = v * _frequency;

            int xi = NoiseBakerUtility.FloorToInt(x);
            int yi = NoiseBakerUtility.FloorToInt(y);
            float tx = x - xi;
            float ty = y - yi;

            float sx = Fade(tx);
            float sy = Fade(ty);

            int x0 = xi;
            int x1 = xi + 1;
            int y0 = yi;
            int y1 = yi + 1;
            if (_period > 0)
            {
                x0 = NoiseBakerUtility.WrapIndex(x0, _period);
                x1 = NoiseBakerUtility.WrapIndex(x1, _period);
                y0 = NoiseBakerUtility.WrapIndex(y0, _period);
                y1 = NoiseBakerUtility.WrapIndex(y1, _period);
            }

            float v00 = NoiseHash.Hash01(x0, y0, _seed);
            float v10 = NoiseHash.Hash01(x1, y0, _seed);
            float v01 = NoiseHash.Hash01(x0, y1, _seed);
            float v11 = NoiseHash.Hash01(x1, y1, _seed);

            float a = Mathf.LerpUnclamped(v00, v10, sx);
            float b = Mathf.LerpUnclamped(v01, v11, sx);
            return Mathf.LerpUnclamped(a, b, sy);
        }

        private float Fade(float t)
        {
            return _fade switch
            {
                FadeType.Linear => t,
                FadeType.Quintic => t * t * t * (t * (t * 6f - 15f) + 10f),
                _ => t * t * (3f - 2f * t),
            };
        }
    }
}
