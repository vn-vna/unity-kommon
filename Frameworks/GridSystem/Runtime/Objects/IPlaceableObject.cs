using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// A placeable shape configuration: identity + multi-cell grid shape.
    /// </summary>
    public interface IPlaceableObject
    {
        string Id { get; }
        char Identifier { get; }
        int SpawnPriority { get; }
        Vector2Int Offset { get; set; }
        PlaceableObjectGrid Grid { get; set; }
    }
}
