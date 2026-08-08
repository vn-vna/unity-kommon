using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Extensions;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Pure grid logic (drop-car <c>GridMap</c> ported with no MonoBehaviour,
    /// no Tilemap/Grid/TouchEchoer, no mouse loop). Owns the cell pool + border
    /// region, placement/replacement/removal, adjacency and border data. Ticked
    /// by <see cref="GridBoard"/> (never implements Update itself).
    /// </summary>
    public class GridMap
    {
        #region Events

        public event Action AllCellsCreated;
        public event Action AllCellsCleared;

        #endregion

        #region Properties

        public GridCell[,] CellObjects => _inboundCells;
        public GridCell[,] PooledCells => _pooledCells;

        public Vector2Int GridSize => _gridSize;
        public Vector2Int EffectiveGridSize { get; private set; }
        public Vector3 CenterPosition { get; private set; }

        public GridConfiguration Configuration => _configuration;

        #endregion

        #region Private Fields

        private readonly GridConfiguration _configuration;
        private readonly IGridCellFactoryProvider _cellFactory;
        private readonly IGridCoordinateProvider _coordinates;
        private readonly IBorderGenerator _borderGenerator;

        private Vector2Int _gridSize;
        private readonly Vector2Int _poolSize;
        private readonly Vector2Int _borderSize;

        private GridCell[,] _inboundCells;
        private GridCell[,] _pooledCells;

        private Dictionary<DirectionFlag, Vector2Int> OffsetMapping => DirectionFlagHelper.D2VInt;

        #endregion

        #region Construction

        public GridMap(GridConfiguration configuration)
        {
            _configuration = configuration;
            _cellFactory = configuration.CellFactory;
            _coordinates = configuration.CoordinateProvider;
            _borderGenerator = configuration.BorderGenerator;
            _gridSize = configuration.GridSize;
            _poolSize = configuration.PoolSize;
            _borderSize = configuration.BorderSize;
        }

        #endregion

        #region Lifecycle

        public void CalculateCurrentEffectiveGridSize()
        {
            Vector2Int wrange = new Vector2Int(int.MaxValue, int.MinValue);
            Vector2Int hrange = new Vector2Int(int.MaxValue, int.MinValue);

            for (int x = 0; x < _gridSize.x; x++)
            {
                for (int y = 0; y < _gridSize.y; y++)
                {
                    GridCell cell = _inboundCells[x, y];
                    if (
                        cell != null &&
                        cell.Occupant != null &&
                        cell.Occupant.Flags.HasFlag(GridOccupantFlag.RemoveBase)
                    )
                    {
                        continue;
                    }
                    wrange.x = Math.Min(wrange.x, x);
                    wrange.y = Math.Max(wrange.y, x);
                    hrange.x = Math.Min(hrange.x, y);
                    hrange.y = Math.Max(hrange.y, y);
                }
            }

            EffectiveGridSize = new Vector2Int(
                wrange.y - wrange.x + 1,
                hrange.y - hrange.x + 1
            );

            Vector2 planeCenter = new Vector2(
                wrange.x + (EffectiveGridSize.x - 1) * 0.5f,
                hrange.x + (EffectiveGridSize.y - 1) * 0.5f
            );

            CenterPosition = _coordinates != null
                ? _coordinates.Unflatten(planeCenter, Vector3.zero)
                : new Vector3(planeCenter.x, 0f, planeCenter.y);
        }

        public GridCell AccessCell(int x, int y)
        {
            if (!CheckValidGridPosition(x, y)) return null;
            return _inboundCells[x, y];
        }

        public GridCell AccessCell(Vector2Int position)
        {
            return AccessCell(position.x, position.y);
        }

        public void EnableRegion(Vector2Int size)
        {
            _gridSize = size;

            if (_inboundCells == null) return;
            for (int x = -_borderSize.x; x < _poolSize.x + _borderSize.x; x++)
            {
                for (int y = -_borderSize.y; y < _poolSize.y + _borderSize.y; y++)
                {
                    EnableRegionSingleCell(x, y);
                }
            }
        }

        public void DisableAllRegions()
        {
            if (_inboundCells == null) return;

            for (int x = -_borderSize.x; x < _poolSize.x + _borderSize.x; x++)
            {
                for (int y = -_borderSize.y; y < _poolSize.y + _borderSize.y; y++)
                {
                    GridCell cell = _pooledCells[y + _borderSize.y, x + _borderSize.x];
                    if (cell != null)
                    {
                        _cellFactory?.SetCellActive(cell, false);
                    }
                }
            }

            _gridSize = Vector2Int.zero;
        }

        public void RefreshBorder()
        {
            _borderGenerator?.Combine(BuildBorderData(), _gridSize);
            CalculateCurrentEffectiveGridSize();
        }

        public void ResetMap()
        {
            if (CellObjects == null || _inboundCells.Length < _gridSize.x * _gridSize.y)
            {
                ClearAllCells();
                CreateAllCells();
            }

            foreach (GridCell cell in _inboundCells)
            {
                if (cell == null) continue;
                cell.ResetCell();
            }

            DisableAllRegions();
            RefreshBorder();
        }

        public GridCell GetCellRelativeTo(GridCell cell, Vector2Int relativePosition)
        {
            if (cell == null) return null;
            Vector2Int targetPosition = cell.GridPosition + relativePosition;
            return !CheckValidGridPosition(targetPosition) ? null : _inboundCells.Access(targetPosition);
        }

        public GridCell GetNeighborCell(int x, int y, DirectionFlag direction)
        {
            Vector2Int relativePosition = OffsetMapping[direction];
            return GetCellRelativeTo(_inboundCells[x, y], relativePosition);
        }

        public GridCell GetNeighborCell(GridCell cell, DirectionFlag direction)
        {
            if (cell == null) return null;

            Vector2Int relativePosition = OffsetMapping[direction];
            return GetCellRelativeTo(cell, relativePosition);
        }

        public bool CheckValidGridPosition(Vector2Int gridPosition)
            => CheckValidGridPosition(gridPosition.x, gridPosition.y);

        public bool CheckValidGridPosition(int x, int y)
            => x.InRange(0, _gridSize.x - 1) && y.InRange(0, _gridSize.y - 1);

        public bool MoveOccupants(
            IEnumerable<IGridOccupant> occupants,
            GridCell from,
            GridCell to
        )
        {
            List<MovingOccupantRelativeInfo> relatives = new List<MovingOccupantRelativeInfo>();

            foreach (IGridOccupant occupant in occupants)
            {
                if (occupant == null || occupant.PlaceableObject == null) continue;

                Vector2Int relativePosition = occupant.GetRelativePositionToHookedCell(from);
                relatives.Add(new MovingOccupantRelativeInfo
                {
                    Occupant = occupant,
                    RelativePosition = relativePosition
                });
            }

            foreach (MovingOccupantRelativeInfo info in relatives)
            {
                if (!CheckObjectPlaceable(info.RelativePosition + to.GridPosition, info.Occupant))
                {
                    return false;
                }
            }

            foreach (MovingOccupantRelativeInfo info in relatives)
            {
                RemoveObjectAtCell(info.Occupant.HookedCell, info.Occupant);
            }

            foreach (MovingOccupantRelativeInfo info in relatives)
            {
                PlaceObjectAtPosition(info.RelativePosition + to.GridPosition, info.Occupant);
            }

            return true;
        }

        public void RefreshGrid()
        {
            ClearAllCells();
            CreateAllCells();
        }

        public void ResetCellsSelectedStatus()
        {
            for (int x = 0; x < _gridSize.x; x++)
            {
                for (int y = 0; y < _gridSize.y; y++)
                {
                    ResetSingleCellSelectedStatus(x, y);
                }
            }
        }

        public void ResetSingleCellSelectedStatus(int x, int y)
        {
            GridCell cell = _inboundCells[x, y];
            if (cell == null) return;

            cell.Selected = false;
        }

        public bool PlaceObjectAtPosition(Vector2Int gridPosition, IGridOccupant occupant)
        {
            if (!CheckValidGridPosition(gridPosition.x, gridPosition.y)) return false;

            GridCell cell = _inboundCells.Access(gridPosition);
            return PlaceObjectAtCell(cell, occupant);
        }

        public bool PlaceObjectAtCell(GridCell cell, IGridOccupant occupant)
        {
            occupant.Position = cell.GridPosition;
            occupant.HookedCell = cell;
            bool success = false;
            ExecuteObjectPlacementAction(cell, occupant, ref success, PlaceSingleCellPart);
            return success;
        }

        public bool ReplaceObjectAtCell(GridCell cell, IGridOccupant occupant)
        {
            if (cell == null || occupant == null) return false;
            if (!CheckObjectPlaceable(cell, occupant)) return false;

            return
                RemoveObjectAtCell(cell, occupant) &&
                PlaceObjectAtCell(cell, occupant);
        }

        public bool RemoveObjectAtCell(GridCell cell, IGridOccupant occupant)
        {
            bool success = false;
            ExecuteObjectPlacementAction(cell, occupant, ref success, RemoveSingleCellPart);
            return success;
        }

        public bool CheckObjectPlaceable(Vector2Int position, IGridOccupant occupant)
        {
            if (!CheckValidGridPosition(position.x, position.y)) return false;
            GridCell cell = _inboundCells.Access(position);
            return cell != null && CheckObjectPlaceable(cell, occupant);
        }

        public bool CheckObjectPlaceable(GridCell cell, IGridOccupant occupant)
        {
            bool success = true;
            ExecuteObjectPlacementAction(cell, occupant, ref success, CheckSingleCellPartPlaceable);
            return success;
        }

        public DirectionFlag CheckObjectMovement(IGridOccupant occupant)
        {
            DirectionFlag movement = DirectionFlag.All;
            ExecuteObjectPlacementAction(
                occupant.HookedCell,
                occupant,
                ref movement,
                CheckSingleCellPartMovable
            );
            return movement;
        }

        public bool RemoveObject(IGridOccupant occupant)
        {
            if (occupant == null)
            {
                return false;
            }

            bool success = false;

            ExecuteObjectPlacementAction(
                occupant.HookedCell,
                occupant,
                ref success,
                RemoveSingleCellPart
            );

            occupant.HookedCell = null;
            return success;
        }

        public void ForceAttachForResume(IGridOccupant occupant, GridCell cell)
        {
            occupant.HookedCell = cell;
            occupant.Position = cell.GridPosition;

            bool dummy = false;

            ExecuteObjectPlacementAction(
                cell,
                occupant,
                ref dummy,
                PlaceSingleCellPart
            );
        }

        public void ClearAllCells()
        {
            if (_pooledCells != null)
            {
                foreach (GridCell cell in _pooledCells)
                {
                    if (cell == null) continue;
                    _cellFactory?.DetachCellView(cell);
                }
            }

            _inboundCells = null;
            _pooledCells = null;

            AllCellsCleared?.Invoke();
        }

        public void CreateAllCells()
        {
            _inboundCells = new GridCell[_poolSize.x, _poolSize.y];
            _pooledCells = new GridCell[
                _poolSize.x + _borderSize.x * 2,
                _poolSize.y + _borderSize.y * 2
            ];

            for (int x = -_borderSize.x; x < _poolSize.x + _borderSize.x; x++)
            {
                for (int y = -_borderSize.y; y < _poolSize.y + _borderSize.y; y++)
                {
                    CreateSingleCell(x, y);
                }
            }

            AllCellsCreated?.Invoke();
        }

        public void GetAdjacentCellsNonAlloc(
            IGridOccupant occupant, DirectionFlag direction, GridCell[] buffer, out int count
        )
        {
            AdjacentCellNonAllocCollectorData data = new AdjacentCellNonAllocCollectorData
            {
                Direction = direction,
                Buffer = buffer,
                Count = 0,
                VisitedCells = new HashSet<GridCell>()
            };

            ExecuteObjectPlacementAction(
                occupant.HookedCell, occupant, ref data,
                CollectCellAdjacentsNonAlloc
            );

            count = data.Count;
        }

        /// <summary>
        /// Border data where <c>true</c> = passable, <c>false</c> = border/wall.
        /// Cells whose occupant has <see cref="GridOccupantFlag.BlocksBorder"/>
        /// are reported as impassable (replaces drop-car's <c>WallBlock</c> check).
        /// </summary>
        public bool[] BuildBorderData()
        {
            bool[] gridData = new bool[_gridSize.x * _gridSize.y];
            for (int x = 0; x < _gridSize.x; x++)
            {
                for (int y = 0; y < _gridSize.y; y++)
                {
                    if (!CheckValidGridPosition(x, y) || _inboundCells[x, y] == null)
                    {
                        gridData[x + y * _gridSize.x] = false;
                        continue;
                    }
                    IGridOccupant occupant = _inboundCells[x, y].Occupant;
                    gridData[x + y * _gridSize.x] =
                        occupant == null ||
                        !occupant.Flags.HasFlag(GridOccupantFlag.BlocksBorder);
                }
            }

            return gridData;
        }

        #endregion

        #region Private Methods

        private void EnableRegionSingleCell(int x, int y)
        {
            GridCell cell = _pooledCells[y + _borderSize.y, x + _borderSize.x];
            if (cell == null) return;

            bool enabled =
                cell.GridPosition.x.InRange(-_borderSize.x, _gridSize.x + _borderSize.x - 1) &&
                cell.GridPosition.y.InRange(-_borderSize.y, _gridSize.y + _borderSize.y - 1);

            cell.IsBorder =
                !cell.GridPosition.x.InRange(0, _gridSize.x - 1) ||
                !cell.GridPosition.y.InRange(0, _gridSize.y - 1);

            _cellFactory?.SetCellActive(cell, enabled);
        }

        private void CheckSingleCellPartMovable(
            GridCell cell, IGridOccupant occupant,
            Vector2Int position, ref DirectionFlag movement
        )
        {
            if (occupant.PlaceableObject.Grid[position] == PlaceableObjectGrid.GridCellEmptySentinelValue) return;
            foreach (DirectionFlag direction in OffsetMapping.Keys)
            {
                Vector2Int predict = cell.GridPosition + OffsetMapping[direction];
                if (ValidateCellPlaceable(cell, occupant, predict, position)) continue;
                movement &= ~direction;
            }
        }

        private bool ValidateCellPlaceable(
            GridCell cell, IGridOccupant occupant,
            Vector2Int predictPosition, Vector2Int relPosition
        )
        {
            if (!CheckValidGridPosition(predictPosition.x, predictPosition.y)) return false;
            GridCell predictedCell = _inboundCells.Access(predictPosition);
            if (!predictedCell.CheckOccupantPlaceable(occupant)) return false;
            IGridOccupant perdOccupant = predictedCell.Occupant;
            if (perdOccupant == null) return true;
            if (perdOccupant == occupant) return true;
            return perdOccupant.CheckReplaceableBy(occupant, cell, relPosition);
        }

        private void CheckSingleCellPartPlaceable(
            GridCell cell, IGridOccupant occupant,
            Vector2Int position, ref bool success
        )
        {
            if (occupant.PlaceableObject.Grid[position] == PlaceableObjectGrid.GridCellEmptySentinelValue) return;
            if (ValidateCellPlaceable(cell, occupant, cell.GridPosition, position)) return;

            success = false;
        }

        private void PlaceSingleCellPart(
            GridCell cell, IGridOccupant occupant,
            Vector2Int position, ref bool success
        )
        {
            if (occupant.PlaceableObject.Grid[position] == PlaceableObjectGrid.GridCellEmptySentinelValue) return;
            cell.Occupant = occupant;
            success = true;
        }

        private void RemoveSingleCellPart(
            GridCell cell, IGridOccupant occupant,
            Vector2Int position, ref bool success
        )
        {
            if (occupant.PlaceableObject.Grid[position] == PlaceableObjectGrid.GridCellEmptySentinelValue) return;
            cell.Occupant = null;
            success = true;
        }

        private void CollectCellAdjacentsNonAlloc(
            GridCell cell, IGridOccupant occupant,
            Vector2Int relativePosition,
            ref AdjacentCellNonAllocCollectorData actionData
        )
        {
            if (cell.Occupant != occupant) return;
            Vector2Int directionOffset = actionData.Direction.ToVector2Int();
            Vector2Int targetPosition = cell.GridPosition + directionOffset;

            if (!CheckValidGridPosition(targetPosition)) return;
            GridCell targetCell = _inboundCells.Access(targetPosition);
            if (targetCell == null) return;
            if (targetCell.Occupant == occupant) return;
            if (actionData.VisitedCells.Contains(targetCell)) return;
            actionData.VisitedCells.Add(targetCell);
            actionData.Buffer[actionData.Count++] = targetCell;
        }

        private void ExecuteObjectPlacementAction<T>(
            GridCell cell, IGridOccupant occupant, ref T actionData,
            SingleCellAction<T> action = null
        )
        {
            Vector2Int ps = occupant.PlaceableObject.Grid.Size; // Placement Size
            Vector2Int pp = cell.GridPosition; // Placement Position

            for (int x = 0; x < ps.x; x++)
            {
                for (int y = 0; y < ps.y; y++)
                {
                    ExecuteSingleCellObjectPlacementAction(
                        occupant, action, pp,
                        new Vector2Int(x, y),
                        ref actionData
                    );
                }
            }
        }

        private T ExecuteSingleCellObjectPlacementAction<T>(
            IGridOccupant occupant, SingleCellAction<T> action,
            Vector2Int pp, Vector2Int relativePosition, ref T actionData
        )
        {
            Vector2Int cellPosition =
                CalculateObjectCellPosition(pp, relativePosition, occupant.PlaceableObject.Offset);

            if (CheckValidGridPosition(cellPosition.x, cellPosition.y))
            {
                action?.Invoke(
                    _inboundCells.Access(cellPosition), occupant, relativePosition,
                    ref actionData
                );
            }

            return actionData;
        }

        private Vector2Int CalculateObjectCellPosition(
            Vector2Int placementPosition, Vector2Int relativePosition,
            Vector2Int offset
        )
            => new Vector2Int(
                placementPosition.x + relativePosition.x + offset.x,
                placementPosition.y + relativePosition.y + offset.y
            );

        private void CreateSingleCell(int x, int y)
        {
            GridCell cell = new GridCell();
            cell.Map = this;
            cell.GridPosition = new Vector2Int(x, y);

            _pooledCells[x + _borderSize.x, y + _borderSize.y] = cell;

            _cellFactory?.AttachCellView(cell, _coordinates);

            if (x >= 0 && y >= 0 && x < _poolSize.x && y < _poolSize.y)
            {
                _inboundCells[x, y] = cell;
            }
            else
            {
                _cellFactory?.SetCellActive(cell, true);
            }
        }

        #endregion

        #region Nested Types

        private delegate void SingleCellAction<T>(
            GridCell cell,
            IGridOccupant occupant,
            Vector2Int relativePosition,
            ref T actionData
        );

        private class AdjacentCellNonAllocCollectorData
        {
            public DirectionFlag Direction { get; set; }
            public GridCell[] Buffer { get; set; }
            public int Count { get; set; }
            public HashSet<GridCell> VisitedCells { get; set; }
        }

        private struct MovingOccupantRelativeInfo
        {
            public IGridOccupant Occupant;
            public Vector2Int RelativePosition;
        }

        #endregion
    }
}
