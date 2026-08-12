using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// Fractal (multi-octave) noise. Decorates any lattice base module with
    /// fBm, ridged or turbulence accumulation. Supports seamless tiling by
    /// giving every octave its own integer lattice period.
    /// </summary>
    public sealed class FractalNoise : INoiseSource
    {
        private readonly FractalType _type;
        private readonly float _baseFrequency;
        private readonly int _octaves;
        private readonly float _lacunarity;
        private readonly float _gain;
        private readonly float _ridgeOffset;
        private readonly bool _normalize;
        private readonly bool _tileable;

        private readonly INoiseSource _sharedBase;
        private readonly INoiseSource[] _octaveBases;
        private readonly int[] _octavePeriods;
        private readonly bool _simplexBase;
        private readonly float _weightSum;

        /// <summary>
        /// Creates a fractal module over the given base lattice type.
        /// </summary>
        /// <param name="baseType">Lattice noise used per octave (Perlin / Value / Simplex).</param>
        /// <param name="baseFrequency">Features of octave 0 across the texture.</param>
        /// <param name="octaves">1..12 octaves.</param>
        /// <param name="lacunarity">Frequency multiplier between octaves.</param>
        /// <param name="gain">Amplitude multiplier between octaves.</param>
        /// <param name="type">Accumulation variant.</param>
        /// <param name="ridgeOffset">Sharpness control for the ridged variant.</param>
        /// <param name="seed">Deterministic seed.</param>
        /// <param name="tileable">True = each octave wraps seamlessly.</param>
        /// <param name="normalize">True = divide by the analytic max of the accumulation.</param>
        public FractalNoise(
            NoiseModuleType baseType,
            float baseFrequency,
            int octaves,
            float lacunarity,
            float gain,
            FractalType type,
            float ridgeOffset,
            int seed,
            bool tileable,
            bool normalize
        )
        {
            _type = type;
            _baseFrequency = Mathf.Max(0.0001f, baseFrequency);
            _octaves = Mathf.Clamp(octaves, 1, 12);
            _lacunarity = Mathf.Clamp(lacunarity, 1.01f, 4f);
            _gain = Mathf.Clamp(gain, 0.01f, 1f);
            _ridgeOffset = Mathf.Max(0f, ridgeOffset);
            _normalize = normalize;
            _tileable = tileable;

            _weightSum = 0f;
            for (int i = 0; i < _octaves; i++)
            {
                _weightSum += Mathf.Pow(_gain, i);
            }

            // The simplex torus path consumes normalized UVs directly
            // (its radius derives from the period), unlike lattice bases.
            _simplexBase = tileable && baseType == NoiseModuleType.Simplex;

            if (tileable)
            {
                // Each octave gets its own integer period so the whole sum
                // wraps exactly once across the texture.
                _octaveBases = new INoiseSource[_octaves];
                _octavePeriods = new int[_octaves];
                for (int i = 0; i < _octaves; i++)
                {
                    int period = Mathf.Max(2, Mathf.RoundToInt(_baseFrequency * Mathf.Pow(_lacunarity, i)));
                    _octavePeriods[i] = period;
                    _octaveBases[i] = CreateBase(baseType, seed, period);
                }
            }
            else
            {
                _sharedBase = CreateBase(baseType, seed, 0);
            }
        }

        public float Sample2D(float u, float v)
        {
            float sum = 0f;
            float amplitude = 1f;

            for (int i = 0; i < _octaves; i++)
            {
                float value;
                if (_tileable)
                {
                    if (_simplexBase)
                    {
                        value = _octaveBases[i].Sample2D(u, v);
                    }
                    else
                    {
                        float period = _octavePeriods[i];
                        value = _octaveBases[i].Sample2D(u * period, v * period);
                    }
                }
                else
                {
                    float frequency = _baseFrequency * Mathf.Pow(_lacunarity, i);
                    value = _sharedBase.Sample2D(u * frequency, v * frequency);
                }

                switch (_type)
                {
                    case FractalType.Ridged:
                    {
                        float ridge = 1f - Mathf.Abs(2f * value - 1f);
                        sum += amplitude * (ridge * ridge + _ridgeOffset * ridge);
                        break;
                    }
                    case FractalType.Turbulence:
                        sum += amplitude * Mathf.Abs(2f * value - 1f);
                        break;
                    default:
                        sum += amplitude * value;
                        break;
                }

                amplitude *= _gain;
            }

            float result = _normalize ? sum / _weightSum : sum;
            return Mathf.Clamp01(result);
        }

        private static INoiseSource CreateBase(NoiseModuleType baseType, int seed, int period)
        {
            return baseType switch
            {
                NoiseModuleType.Value => new ValueNoise(seed, 1f, period),
                NoiseModuleType.Simplex => new SimplexNoise(seed, period, period),
                _ => new PerlinNoise(seed, 1f, period),
            };
        }
    }
}
