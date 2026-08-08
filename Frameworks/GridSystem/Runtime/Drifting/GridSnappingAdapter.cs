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
        private Vector2 _moveDirection;      // NOTE: drop-car never assigns this; ported as-is
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

            Vector2Int predictedDirection = _predictedCell.GridPosition - _currentPositionCell.GridPosition;
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

            Vector2Int predictedPosition = _currentPositionCell.GridPosition + predictedDirection;

            DirectionFlag directionFlag = predictedDirection.ToDirectionFlag();
            if ((directionFlag & DirectionFlag.Diagonal) != 0)
            {
                if (Mathf.Abs(_moveDirection.x) > Mathf.Abs(_moveDirection.y))
                {
                    predictedDirection.y = 0;
                }
                else
                {
                    predictedDirection.x = 0;
                }
            }

            _predictedCell = _map.CheckValidGridPosition(predictedPosition)
                ? _map.AccessCell(predictedPosition)
                : _map.AccessCell(_currentPositionCell.GridPosition);

            if (_predictedCell == _currentPositionCell) return;

            SnapOccupantsToGrid();
        }

        #endregion

        #region Private Methods

        private void SnapOccupantsToGrid()
        {
            if (_predictedCell == null) return;

            _occupantScratch.Clear();
            foreach (IDrifter drifter in _drifter.Drifters)
            {
                _occupantScratch.Add(drifter.Occupant);
            }

            if (!_map.MoveOccupants(_occupantScratch, _currentPositionCell, _predictedCell)) return;

            _currentPositionCell = _predictedCell;
            SnappedCellChanged?.Invoke();      // game hooks haptics/audio here (drop-car played HapticPattern)
        }

        #endregion
    }
}
