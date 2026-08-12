namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// Contract for every noise module in the NoiseBaker pipeline.
    /// Implementations must be deterministic (same inputs -> same outputs)
    /// and return a value in the 0..1 range.
    /// </summary>
    public interface INoiseSource
    {
        /// <summary>
        /// Samples the noise field at normalized UV coordinates.
        /// <paramref name="u"/> and <paramref name="v"/> are expected in 0..1
        /// (the module applies its own frequency/period math internally).
        /// </summary>
        float Sample2D(float u, float v);
    }

    /// <summary>Top-level selector for the module used by a bake.</summary>
    public enum NoiseModuleType
    {
        Value,
        Perlin,
        Simplex,
        Fractal,
        Voronoi,
    }

    /// <summary>Accumulation variant for <see cref="FractalNoise"/>.</summary>
    public enum FractalType
    {
        Fbm,
        Ridged,
        Turbulence,
    }

    /// <summary>Output mode of the Voronoi module.</summary>
    public enum VoronoiFeature
    {
        /// <summary>Distance to nearest feature point (cell gradient).</summary>
        Cell,

        /// <summary>Distance to second-nearest feature point.</summary>
        F2,

        /// <summary>F2 - F1: thin bright lines along cell borders.</summary>
        Edge,

        /// <summary>Smooth, controllable band around cell borders.</summary>
        Border,

        /// <summary>Flat per-cell hash values (biome/ID maps).</summary>
        CellId,
    }

    /// <summary>Distance metric used by the Voronoi module.</summary>
    public enum VoronoiDistanceMetric
    {
        Euclidean,
        Manhattan,
        Chebyshev,
        Exponent,
    }

    /// <summary>How pixel coordinates are mapped into the sampling domain.</summary>
    public enum DomainMode
    {
        /// <summary>Plain UV sampling. Edges do not match.</summary>
        None,

        /// <summary>Periodic (torus) wrapping handled by each module -> seamless texture.</summary>
        TorusWrap,

        /// <summary>Ping-pong coordinate folding. Cheap seamless look without lattice wrapping.</summary>
        Mirror,
    }

    /// <summary>How the sampled value is converted into a color.</summary>
    public enum ColorMode
    {
        Grayscale,
        Gradient,
        ChannelPack,
    }
}
