using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// The ONLY place where board orientation (XZ vs XY), cell size, and the
    /// grid&lt;-&gt;world mapping live. The logic core and movement module never
    /// touch world axes directly.
    /// </summary>
    public interface IGridCoordinateProvider
    {
        Vector3 CellToWorld(Vector2Int cell);                    // cell center in world
        Vector2Int WorldToCell(Vector3 world);
        bool TryRaycastGridPlane(Ray ray, out Vector3 point);    // pointer ray -> grid plane
        Vector2 Flatten(Vector3 world);                          // world -> grid-plane coords
        Vector3 Unflatten(Vector2 plane, Vector3 anchorWorld);   // grid-plane -> world
    }
}
