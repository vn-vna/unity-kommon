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
        GridCoord Position { get; set; }
        GridCell HookedCell { get; set; }
        event Action<IGridOccupant, GridCell, GridCell> HookedCellChanged;

        GridCoord? GetRelativePositionOnPlaceableGrid(GridCell cell, bool forceInside = false);
        GridCoord GetRelativePositionToHookedCell(GridCell cell);
        bool CheckReplaceableBy(IGridOccupant other, GridCell cell, GridCoord relativePosition);
        void HandleCellReplacedRequest(GridCell cell, IGridOccupant replacement);
    }
}
