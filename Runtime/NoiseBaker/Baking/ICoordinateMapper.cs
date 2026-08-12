using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoiseBaker
{
    /// <summary>
    /// Maps pixel coordinates to normalized UV coordinates before sampling.
    /// Modules receive u,v in 0..1 and apply their own frequency/period math,
    /// so the only difference between mapping modes is how the domain folds.
    /// </summary>
    public interface ICoordinateMapper
    {
        void Map(int px, int py, int width, int height, out float u, out float v);

        /// <summary>Folds normalized coordinates (used by the Sample() API).</summary>
        void MapNormalized(float u, float v, out float uo, out float vo);

        bool IsTileable { get; }
    }

    /// <summary>Plain UV mapping with edge-aligned sampling (u = px / (w - 1)).</summary>
    public sealed class UvCoordinateMapper : ICoordinateMapper
    {
        public bool IsTileable => false;

        public void Map(int px, int py, int width, int height, out float u, out float v)
        {
            u = width > 1 ? px / (width - 1f) : 0.5f;
            v = height > 1 ? py / (height - 1f) : 0.5f;
        }

        public void MapNormalized(float u, float v, out float uo, out float vo)
        {
            uo = u;
            vo = v;
        }
    }

    /// <summary>
    /// Ping-pong (mirror) folding: coordinates reflect at the texture borders,
    /// producing a soft seamless pattern without lattice wrapping.
    /// </summary>
    public sealed class MirrorCoordinateMapper : ICoordinateMapper
    {
        public bool IsTileable => true;

        public void Map(int px, int py, int width, int height, out float u, out float v)
        {
            float uRaw = width > 1 ? px / (width - 1f) : 0.5f;
            float vRaw = height > 1 ? py / (height - 1f) : 0.5f;
            u = Fold(uRaw);
            v = Fold(vRaw);
        }

        public void MapNormalized(float u, float v, out float uo, out float vo)
        {
            uo = Fold(u);
            vo = Fold(v);
        }

        private static float Fold(float t)
        {
            t = Mathf.Repeat(t, 2f);
            return t > 1f ? 2f - t : t;
        }
    }

    /// <summary>Creates the mapper for a domain mode.</summary>
    public static class CoordinateMapperFactory
    {
        public static ICoordinateMapper Create(DomainMode domain)
        {
            return domain == DomainMode.Mirror
                ? new MirrorCoordinateMapper()
                : new UvCoordinateMapper();
        }
    }
}
