using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// Classic Ken Perlin noise (2D) with a seeded permutation table and
    /// optional periodic wrapping for seamless tiling.
    /// Output is remapped to 0..1.
    /// </summary>
    public sealed class PerlinNoise : INoiseSource
    {
        // 8 unit gradient vectors as (x, y) pairs: diagonals + axis aligned.
        private static readonly float[] Gradients =
        {
            1f, 1f, -1f, 1f, 1f, -1f, -1f, -1f,
            1f, 0f, -1f, 0f, 0f, 1f, 0f, -1f,
        };

        private readonly int[] _perm;
        private readonly float _frequency;
        private readonly int _period;

        /// <param name="seed">Deterministic seed for the permutation table.</param>
        /// <param name="frequency">Features across the texture (lattice cells).</param>
        /// <param name="period">Lattice wrap period; 0 = aperiodic, &gt; 0 = seamless torus.</param>
        public PerlinNoise(int seed, float frequency, int period)
        {
            _perm = new int[512];
            int[] table = NoiseHash.BuildPermutation(seed);
            System.Array.Copy(table, 0, _perm, 0, 256);
            System.Array.Copy(table, 0, _perm, 256, 256);

            _frequency = Mathf.Max(0.0001f, frequency);
            _period = Mathf.Max(0, period);
        }

        /// <summary>Convenience constructor: period is derived from frequency when tileable.</summary>
        public PerlinNoise(int seed, float frequency, bool tileable)
            : this(seed, frequency, tileable ? NoiseBakerUtility.PeriodFor(frequency) : 0)
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

            float n00 = DotGradient(IndexAt(x0, y0), tx, ty);
            float n10 = DotGradient(IndexAt(x1, y0), tx - 1f, ty);
            float n01 = DotGradient(IndexAt(x0, y1), tx, ty - 1f);
            float n11 = DotGradient(IndexAt(x1, y1), tx - 1f, ty - 1f);

            float nx0 = Mathf.LerpUnclamped(n00, n10, sx);
            float nx1 = Mathf.LerpUnclamped(n01, n11, sx);
            float n = Mathf.LerpUnclamped(nx0, nx1, sy);

            // Remap [-1, 1] to [0, 1].
            return Mathf.Clamp01(n * 0.5f + 0.5f);
        }

        private int IndexAt(int ix, int iy)
        {
            return _perm[(_perm[ix & 255] + iy) & 255];
        }

        private static float DotGradient(int hash, float dx, float dy)
        {
            int h = hash & 7;
            return Gradients[h * 2] * dx + Gradients[h * 2 + 1] * dy;
        }

        private static float Fade(float t)
        {
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }
}
