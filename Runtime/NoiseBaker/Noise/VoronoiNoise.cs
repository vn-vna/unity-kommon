using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// 2D Voronoi (cell) noise: distances to jittered feature points, with
    /// multiple output features (cell gradient, F2, edges, borders, cell IDs)
    /// and distance metrics. Supports seamless tiling via cell-index wrapping
    /// plus torus-min distance deltas.
    /// </summary>
    public sealed class VoronoiNoise : INoiseSource
    {
        private readonly int _seed;
        private readonly int _cellCount;
        private readonly float _jitter;
        private readonly VoronoiDistanceMetric _metric;
        private readonly float _exponent;
        private readonly VoronoiFeature _feature;
        private readonly float _borderWidth;
        private readonly float _borderSoftness;
        private readonly bool _tileable;
        private readonly bool _normalizeDistances;

        /// <param name="seed">Deterministic seed for feature points.</param>
        /// <param name="cellCount">Number of cells across the texture (integer for tiling).</param>
        /// <param name="jitter">0 = perfect grid, 0.95 = max displacement.</param>
        /// <param name="metric">Distance metric.</param>
        /// <param name="exponent">Exponent p for the Exponent metric (1..8).</param>
        /// <param name="feature">Output feature.</param>
        /// <param name="borderWidth">Border thickness in normalized distance units (Border only).</param>
        /// <param name="borderSoftness">Smoothstep width of the border edge (Border only).</param>
        /// <param name="tileable">True = cells wrap seamlessly across the texture.</param>
        /// <param name="normalizeDistances">True = distances divided by their metric maximum.</param>
        public VoronoiNoise(
            int seed,
            int cellCount,
            float jitter,
            VoronoiDistanceMetric metric,
            float exponent,
            VoronoiFeature feature,
            float borderWidth,
            float borderSoftness,
            bool tileable,
            bool normalizeDistances
        )
        {
            _seed = seed;
            _cellCount = Mathf.Max(1, cellCount);
            _jitter = Mathf.Clamp(jitter, 0f, 0.95f);
            _metric = metric;
            _exponent = Mathf.Clamp(exponent, 1f, 8f);
            _feature = feature;
            _borderWidth = Mathf.Clamp(borderWidth, 0f, 0.5f);
            _borderSoftness = Mathf.Clamp(borderSoftness, 0f, 1f);
            _tileable = tileable;
            _normalizeDistances = normalizeDistances;
        }

        public float Sample2D(float u, float v)
        {
            float x = u * _cellCount;
            float y = v * _cellCount;

            int cx = NoiseBakerUtility.FloorToInt(x);
            int cy = NoiseBakerUtility.FloorToInt(y);

            float best1 = float.MaxValue;
            float best2 = float.MaxValue;
            int bestCellHash = 0;

            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++)
                {
                    int nx = cx + i;
                    int ny = cy + j;
                    if (_tileable)
                    {
                        nx = NoiseBakerUtility.WrapIndex(nx, _cellCount);
                        ny = NoiseBakerUtility.WrapIndex(ny, _cellCount);
                    }

                    float px = nx + _jitter * NoiseHash.Hash01(nx, ny, _seed);
                    float py = ny + _jitter * NoiseHash.Hash01(nx, ny, _seed ^ 0x1F2E3D4C);

                    float dx = x - px;
                    float dy = y - py;
                    if (_tileable)
                    {
                        dx = WrapDelta(dx, _cellCount);
                        dy = WrapDelta(dy, _cellCount);
                    }

                    float distance = Distance(dx, dy);
                    if (distance < best1)
                    {
                        best2 = best1;
                        best1 = distance;
                        bestCellHash = nx * 73856093 ^ ny * 19349663 ^ _seed;
                    }
                    else if (distance < best2)
                    {
                        best2 = distance;
                    }
                }
            }

            return ResolveFeature(best1, best2, bestCellHash);
        }

        private float ResolveFeature(float best1, float best2, int bestCellHash)
        {
            float maxDistance = _normalizeDistances ? MetricMax() : 1f;

            switch (_feature)
            {
                case VoronoiFeature.Cell:
                    return Mathf.Clamp01(best1 / maxDistance);

                case VoronoiFeature.F2:
                    return Mathf.Clamp01(best2 / (2f * maxDistance));

                case VoronoiFeature.Edge:
                    return Mathf.Clamp01((best2 - best1) / maxDistance);

                case VoronoiFeature.Border:
                {
                    float edge = Mathf.Clamp01((best2 - best1) / maxDistance);
                    if (_borderWidth <= 0f)
                    {
                        return edge <= 0f ? 1f : 0f;
                    }

                    float inner = _borderWidth * (1f - _borderSoftness);
                    float border = 1f - SmoothStep(inner, _borderWidth, edge);
                    return Mathf.Clamp01(border);
                }

                default:
                    return NoiseHash.Hash01(bestCellHash, _seed ^ 0x0F0F0F0F);
            }
        }

        private float Distance(float dx, float dy)
        {
            float ax = Mathf.Abs(dx);
            float ay = Mathf.Abs(dy);
            return _metric switch
            {
                VoronoiDistanceMetric.Manhattan => ax + ay,
                VoronoiDistanceMetric.Chebyshev => Mathf.Max(ax, ay),
                VoronoiDistanceMetric.Exponent => Mathf.Pow(Mathf.Pow(ax, _exponent) + Mathf.Pow(ay, _exponent), 1f / _exponent),
                _ => Mathf.Sqrt(dx * dx + dy * dy),
            };
        }

        private float MetricMax()
        {
            return _metric switch
            {
                VoronoiDistanceMetric.Manhattan => 2f,
                VoronoiDistanceMetric.Chebyshev => 1f,
                VoronoiDistanceMetric.Exponent => Mathf.Pow(2f, 1f / _exponent),
                _ => Mathf.Sqrt(2f),
            };
        }

        private static float WrapDelta(float delta, int period)
        {
            float half = period * 0.5f;
            if (delta > half)
            {
                return delta - period;
            }

            return delta < -half ? delta + period : delta;
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            float t = Mathf.Clamp01((value - edge0) / Mathf.Max(0.000001f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
