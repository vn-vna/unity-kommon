using Com.Scheherazade.Common.Frameworks.GridSystem;
#if PLA_CREATIVE_BUILD
using Core = Com.Scheherazade.Common.Frameworks.SandEngine.Pla.SandPlaAlgorithmCore;
#else
using Core = Com.Scheherazade.Common.Frameworks.SandEngine.SandAlgorithmCore;
#endif

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    /// <summary>Conversions for the packed-cell cardinal gravity encoding.</summary>
    public static class SandGravityDirection
    {
        public static GridCoord Normalize(GridCoord direction) => Core.NormalizeGravityDirection(direction);
        public static GridCoord Decode(byte code) => SandGravityMap.Decode(code);
        public static byte Encode(GridCoord direction) => SandGravityMap.Encode(direction);
    }
}
