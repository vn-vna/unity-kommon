using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// 2D simplex noise (value-simplex variant) with optional periodic
    /// wrapping. Triangular lattice avoids the axis-aligned artifacts of
    /// classic Perlin noise.
    ///
    /// Tiling note: the simplex lattice is skewed, so lattice-index wrapping
    /// cannot align with the torus (irrational skew constants). Tileable
    /// sampling therefore uses the standard 4D torus embedding: the 4D
    /// simplex is evaluated on a circle per axis, which is mathematically
    /// exactly periodic. Cost is higher (4 trig + 5-corner kernel).
    ///
    /// Both paths use partition-of-unity normalization (corner hashes are
    /// blended with kernel weights that always sum to a positive value), so
    /// the output spans 0..1 with a balanced mean regardless of path.
    /// </summary>
    public sealed class SimplexNoise : INoiseSource
    {
        private static readonly float F2 = 0.5f * (Mathf.Sqrt(3f) - 1f);
        private static readonly float G2 = (3f - Mathf.Sqrt(3f)) / 6f;
        private const float F4 = 0.309016994f; // (sqrt(5) - 1) / 4
        private const float G4 = 0.138196601f; // (5 - sqrt(5)) / 20

        private readonly int _seed;
        private readonly float _frequency;
        private readonly int _period;

        /// <param name="seed">Deterministic hash seed.</param>
        /// <param name="frequency">Features across the texture (lattice cells).</param>
        /// <param name="period">Lattice wrap period; 0 = aperiodic, &gt; 0 = seamless torus.</param>
        public SimplexNoise(int seed, float frequency, int period)
        {
            _seed = seed;
            _frequency = Mathf.Max(0.0001f, frequency);
            _period = Mathf.Max(0, period);
        }

        /// <summary>Convenience constructor: period is derived from frequency when tileable.</summary>
        public SimplexNoise(int seed, float frequency, bool tileable)
            : this(seed, frequency, tileable ? NoiseBakerUtility.PeriodFor(frequency) : 0)
        {
        }

        public float Sample2D(float u, float v)
        {
            return _period > 0
                ? SampleTorus(u, v)
                : Sample2DPlane(u * _frequency, v * _frequency);
        }

        /// <summary>Seamless torus sampling via 4D simplex on a circle per axis.</summary>
        private float SampleTorus(float u, float v)
        {
            // One full turn per tile; the circle radius is chosen so the arc
            // length (lattice crossings) equals the period -> period features.
            float radius = _period / (Mathf.PI * 2f);
            float angleU = u * Mathf.PI * 2f;
            float angleV = v * Mathf.PI * 2f;

            float x = Mathf.Cos(angleU) * radius;
            float y = Mathf.Sin(angleU) * radius;
            float z = Mathf.Cos(angleV) * radius;
            float w = Mathf.Sin(angleV) * radius;

            return Simplex4D(x, y, z, w);
        }

        private float Sample2DPlane(float x, float y)
        {
            // Skew into simplex space.
            float skew = (x + y) * F2;
            float xs = x + skew;
            float ys = y + skew;

            int i = NoiseBakerUtility.FloorToInt(xs);
            int j = NoiseBakerUtility.FloorToInt(ys);

            // Unskew the cell origin back to regular space.
            float t = (i + j) * G2;
            float x0 = x - (i - t);
            float y0 = y - (j - t);

            // Second corner of the simplex: which diagonal is the point on?
            int i1;
            int j1;
            if (x0 > y0)
            {
                i1 = 1;
                j1 = 0;
            }
            else
            {
                i1 = 0;
                j1 = 1;
            }

            float x1 = x0 - i1 + G2;
            float y1 = y0 - j1 + G2;
            float x2 = x0 - 1f + 2f * G2;
            float y2 = y0 - 1f + 2f * G2;

            float w0 = KernelWeight2D(x0, y0);
            float w1 = KernelWeight2D(x1, y1);
            float w2 = KernelWeight2D(x2, y2);

            float weightSum = w0 + w1 + w2;
            if (weightSum <= 0f)
            {
                return 0.5f;
            }

            float v0 = NoiseHash.Hash01(i, j, _seed);
            float v1 = NoiseHash.Hash01(i + i1, j + j1, _seed);
            float v2 = NoiseHash.Hash01(i + 1, j + 1, _seed);

            return (w0 * v0 + w1 * v1 + w2 * v2) / weightSum;
        }

        private float Simplex4D(float x, float y, float z, float w)
        {
            // Skew into 4D simplex space.
            float skew = (x + y + z + w) * F4;
            float xs = x + skew;
            float ys = y + skew;
            float zs = z + skew;
            float ws = w + skew;

            int i = NoiseBakerUtility.FloorToInt(xs);
            int j = NoiseBakerUtility.FloorToInt(ys);
            int k = NoiseBakerUtility.FloorToInt(zs);
            int l = NoiseBakerUtility.FloorToInt(ws);

            float t = (i + j + k + l) * G4;
            float x0 = x - (i - t);
            float y0 = y - (j - t);
            float z0 = z - (k - t);
            float w0 = w - (l - t);

            // Rank the four deltas descending: the simplex corners add 1 to the
            // dimensions with the largest deltas (1, 2, 3 of them, then all 4).
            Span<float> deltas = stackalloc float[4] { x0, y0, z0, w0 };
            Span<int> rank = stackalloc int[4] { 0, 1, 2, 3 };
            for (int a = 1; a < 4; a++)
            {
                int current = rank[a];
                float currentValue = deltas[current];
                int b = a - 1;
                while (b >= 0 && deltas[rank[b]] < currentValue)
                {
                    rank[b + 1] = rank[b];
                    b--;
                }

                rank[b + 1] = current;
            }

            int i1 = rank[0] == 0 ? 1 : 0;
            int j1 = rank[0] == 1 ? 1 : 0;
            int k1 = rank[0] == 2 ? 1 : 0;
            int l1 = rank[0] == 3 ? 1 : 0;

            int i2 = rank[0] == 0 || rank[1] == 0 ? 1 : 0;
            int j2 = rank[0] == 1 || rank[1] == 1 ? 1 : 0;
            int k2 = rank[0] == 2 || rank[1] == 2 ? 1 : 0;
            int l2 = rank[0] == 3 || rank[1] == 3 ? 1 : 0;

            int i3 = rank[0] == 0 || rank[1] == 0 || rank[2] == 0 ? 1 : 0;
            int j3 = rank[0] == 1 || rank[1] == 1 || rank[2] == 1 ? 1 : 0;
            int k3 = rank[0] == 2 || rank[1] == 2 || rank[2] == 2 ? 1 : 0;
            int l3 = rank[0] == 3 || rank[1] == 3 || rank[2] == 3 ? 1 : 0;

            float x1 = x0 - i1 + G4;
            float y1 = y0 - j1 + G4;
            float z1 = z0 - k1 + G4;
            float w1 = w0 - l1 + G4;

            float x2 = x0 - i2 + 2f * G4;
            float y2 = y0 - j2 + 2f * G4;
            float z2 = z0 - k2 + 2f * G4;
            float w2 = w0 - l2 + 2f * G4;

            float x3 = x0 - i3 + 3f * G4;
            float y3 = y0 - j3 + 3f * G4;
            float z3 = z0 - k3 + 3f * G4;
            float w3 = w0 - l3 + 3f * G4;

            float x4 = x0 - 1f + 4f * G4;
            float y4 = y0 - 1f + 4f * G4;
            float z4 = z0 - 1f + 4f * G4;
            float w4 = w0 - 1f + 4f * G4;

            float w0k = KernelWeight4D(x0, y0, z0, w0);
            float w1k = KernelWeight4D(x1, y1, z1, w1);
            float w2k = KernelWeight4D(x2, y2, z2, w2);
            float w3k = KernelWeight4D(x3, y3, z3, w3);
            float w4k = KernelWeight4D(x4, y4, z4, w4);

            float weightSum = w0k + w1k + w2k + w3k + w4k;
            if (weightSum <= 0f)
            {
                return 0.5f;
            }

            float v0 = NoiseHash.Hash01(i, j, k, l, _seed);
            float v1 = NoiseHash.Hash01(i + i1, j + j1, k + k1, l + l1, _seed);
            float v2 = NoiseHash.Hash01(i + i2, j + j2, k + k2, l + l2, _seed);
            float v3 = NoiseHash.Hash01(i + i3, j + j3, k + k3, l + l3, _seed);
            float v4 = NoiseHash.Hash01(i + 1, j + 1, k + 1, l + 1, _seed);

            return (w0k * v0 + w1k * v1 + w2k * v2 + w3k * v3 + w4k * v4) / weightSum;
        }

        private static float KernelWeight2D(float dx, float dy)
        {
            float d2 = dx * dx + dy * dy;
            float t = 0.5f - d2;
            if (t <= 0f)
            {
                return 0f;
            }

            float t4 = t * t * t * t;
            return t4;
        }

        private static float KernelWeight4D(float dx, float dy, float dz, float dw)
        {
            float d2 = dx * dx + dy * dy + dz * dz + dw * dw;
            float t = 0.6f - d2;
            if (t <= 0f)
            {
                return 0f;
            }

            float t4 = t * t * t * t;
            return t4;
        }
    }
}
