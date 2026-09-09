using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Snap prediction + commit (drop-car <c>GridSnappingAdapter</c> ported to a
    /// pure class owned by <see cref="GridBoard"/>). Raises
    /// <see cref="SnappedCellChanged"/> instead of playing haptics.
    /// </summary>
    public class GridSnappingAdapter
    {
        #region Events

        public event Action SnappedCellChanged;

        #endregion

        #region Properties

        public GridCell SnapTargetCell => _predictedCell;

        #endregion

        #region Private Fields

        private readonly GridMap _map;
        private readonly GridDrifter _drifter;

        private GridCell _predictedCell;
        private GridCell _currentPositionCell;
        private readonly List<IGridOccupant> _occupantScratch = new List<IGridOccupant>();

        #endregion

        #region Construction

        public GridSnappingAdapter(GridMap map, GridDrifter drifter)
        {
            _map = map;
            _drifter = drifter;
        }

        #endregion

        #region Public Methods

        public void UpdateSnapping()
        {
            if (!_drifter.Enabled || !_drifter.Drifting) return;

            _currentPositionCell = _drifter.MasterCell;
            if (_currentPositionCell == null) return;

            if (_drifter.HoveringCell != null)
            {
                _predictedCell = _drifter.HoveringCell;
            }

            GridCoord predictedDirection = _predictedCell.GridPosition - _currentPositionCell.GridPosition;
            predictedDirection.x = Math.Clamp(
                predictedDirection.x,
                _drifter.MovementAbility.HasFlag(DirectionFlag.West) ? -1 : 0,
                _drifter.MovementAbility.HasFlag(DirectionFlag.East) ? 1 : 0
            );
            predictedDirection.y = Math.Clamp(
                predictedDirection.y,
                _drifter.MovementAbility.HasFlag(DirectionFlag.South) ? -1 : 0,
                _drifter.MovementAbility.HasFlag(DirectionFlag.North) ? 1 : 0
            );

            // Keep a diagonal when both orthogonal intermediates are traversable;
            // otherwise resolve through the free cardinal axis. MoveOccupants still
            // validates the destination footprint before committing anything.
            if ((predictedDirection.ToDirectionFlag() & DirectionFlag.Diagonal) != 0)
            {
                predictedDirection = ResolveDiagonalAxis(predictedDirection);
            }

            // NOTE: predictedPosition must be computed from the RESOLVED direction.
            GridCoord predictedPosition = _currentPositionCell.GridPosition + predictedDirection;

            _predictedCell = _map.CheckValidGridPosition(predictedPosition)
                ? _map.AccessCell(predictedPosition)
                : _map.AccessCell(_currentPositionCell.GridPosition);

            if (_predictedCell == _currentPositionCell) return;

            SnapOccupantsToGrid();
        }

        #endregion

        #region Private Methods

        private GridCoord ResolveDiagonalAxis(GridCoord direction)
        {
            IGridOccupant occupant = GetFirstOccupant();
            if (occupant == null) return GridCoord.zero;

            GridCoord horizontal = new GridCoord(direction.x, 0);
            GridCoord vertical = new GridCoord(0, direction.y);

            bool horizontalFree = IsTraversable(occupant, horizontal);
            bool verticalFree = IsTraversable(occupant, vertical);

            // Free diagonal movement when BOTH orthogonal intermediates are clear
            // (matches CheckObjectMovement's no-corner-cutting rule). Only corner
            // through the free axis when one intermediate is blocked.
            if (horizontalFree && verticalFree)
            {
                return direction;
            }
            if (horizontalFree) return horizontal;
            if (verticalFree) return vertical;
            return GridCoord.zero;   // no free intermediate -> no snap
        }

        private bool IsTraversable(IGridOccupant occupant, GridCoord relativeStep)
        {
            GridCoord target = _currentPositionCell.GridPosition + relativeStep;
            if (!_map.CheckValidGridPosition(target)) return false;
            return _map.CheckObjectPlaceable(target, occupant);
        }

        private IGridOccupant GetFirstOccupant()
        {
            foreach (IDrifter drifter in _drifter.Drifters)
            {
                if (drifter.Occupant != null) return drifter.Occupant;
            }
            return null;
        }

        private void SnapOccupantsToGrid()
        {
            if (_predictedCell == null) return;

            _occupantScratch.Clear();
            foreach (IDrifter drifter in _drifter.Drifters)
            {
                _occupantScratch.Add(drifter.Occupant);
            }

            if (!_map.MoveOccupants(_occupantScratch, _currentPositionCell, _predictedCell)) return;

            _drifter.NotifyGridDisplacement();

            // Logical snap only: the HookedCell follows the drag (keeps movement
            // clamping correct), but the VISUAL stays at the pointer position while
            // dragging. The box settles visually onto the cell on release
            // (GridDrifter.ReleaseAllDrifters -> RecenterDriftersOnHookedCells).
            _currentPositionCell = _predictedCell;
            SnappedCellChanged?.Invoke();      // game hooks haptics/audio here (drop-car played HapticPattern)
        }

        #endregion
    }
}
