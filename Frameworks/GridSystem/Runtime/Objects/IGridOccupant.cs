using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Anything that can occupy grid cells. Any type (MonoBehaviour,
    /// ScriptableObject, plain class) may implement it; <see cref="GridOccupant"/>
    /// is the default composition reference implementation.
    /// </summary>
    public interface IGridOccupant
    {
        IPlaceableObject PlaceableObject { get; }
        GridOccupantFlag Flags { get; }
        Vector2Int Position { get; set; }
        GridCell HookedCell { get; set; }
        event Action<IGridOccupant, GridCell, GridCell> HookedCellChanged;

        Vector2Int? GetRelativePositionOnPlaceableGrid(GridCell cell, bool forceInside = false);
        Vector2Int GetRelativePositionToHookedCell(GridCell cell);
        bool CheckReplaceableBy(IGridOccupant other, GridCell cell, Vector2Int relativePosition);
        void HandleCellReplacedRequest(GridCell cell, IGridOccupant replacement);
    }
}
