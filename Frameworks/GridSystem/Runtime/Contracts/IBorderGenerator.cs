using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Optional hook for border visuals — port target for drop-car
    /// <c>AutoBorderMeshCombiner</c>. Null-safe: when a board has no generator
    /// assigned, borders are simply not generated.
    /// </summary>
    public interface IBorderGenerator
    {
        void Combine(bool[] gridData, Vector2Int gridSize);
    }
}
