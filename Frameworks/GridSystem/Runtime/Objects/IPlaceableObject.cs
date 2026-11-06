using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// A placeable shape configuration: identity + multi-cell grid shape.
    /// </summary>
    public interface IPlaceableObject
    {
        string Id { get; }
        GridCoord Offset { get; set; }
        PlaceableObjectGrid Grid { get; set; }
    }
}
