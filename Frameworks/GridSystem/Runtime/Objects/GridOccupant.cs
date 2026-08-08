using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Default reference implementation of <see cref="IGridOccupant"/> as a plain
    /// composable class. Game entities compose it instead of reimplementing the
    /// whole contract.
    /// </summary>
    public class GridOccupant : IGridOccupant
    {
        #region Properties

        public IPlaceableObject PlaceableObject { get; set; }
        public GridOccupantFlag Flags { get; set; }
        public Vector2Int Position { get; set; }

        public GridCell HookedCell
        {
            get => _hookedCell;
            set
            {
                if (_hookedCell == value) return;
                GridCell oldCell = _hookedCell;
                _hookedCell = value;
                HookedCellChanged?.Invoke(this, oldCell, value);
            }
        }

        public event Action<IGridOccupant, GridCell, GridCell> HookedCellChanged;

        #endregion

        #region Private Fields

        private GridCell _hookedCell;

        #endregion

        #region Public Methods

        public Vector2Int? GetRelativePositionOnPlaceableGrid(GridCell cell, bool forceInside = false)
        {
            if (PlaceableObject == null || PlaceableObject.Grid == null) return null;
            if (cell == null || (forceInside && cell.Occupant != this)) return null;
            return cell.GridPosition - HookedCell.GridPosition - PlaceableObject.Offset;
        }

        public Vector2Int GetRelativePositionToHookedCell(GridCell cell)
            => HookedCell == null ? Vector2Int.zero : HookedCell.GridPosition - cell.GridPosition;

        public virtual bool CheckReplaceableBy(IGridOccupant other, GridCell cell, Vector2Int relativePosition)
            => false;

        public virtual void HandleCellReplacedRequest(GridCell cell, IGridOccupant replacement) { }

        #endregion
    }
}
