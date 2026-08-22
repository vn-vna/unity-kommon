using System;
using System.Collections;
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

        public GridCoord GridSize => _gridSize;
        public GridCoord PoolSize => _poolSize;
        public GridCoord BorderSize => _borderSize;
        public GridCoord EffectiveGridSize { get; private set; }
        public Vector3 CenterPosition { get; private set; }

        public GridConfiguration Configuration => _configuration;

        #endregion

        #region Private Fields

        private readonly GridConfiguration _configuration;
        private readonly IGridCellFactoryProvider _cellFactory;
        private readonly IGridCoordinateProvider _coordinates;
        private readonly IBorderGenerator _borderGenerator;

        private GridCoord _gridSize;
        private readonly GridCoord _poolSize;
        private readonly GridCoord _borderSize;

        private GridCell[,] _inboundCells;
        private GridCell[,] _pooledCells;

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
            GridCoord wrange = new GridCoord(int.MaxValue, int.MinValue);
            GridCoord hrange = new GridCoord(int.MaxValue, int.MinValue);

            for (int x = 0; x < _gridSize.x; x++)
            {
                for (int y = 0; y < _gridSize.y; y++)
                {
                    GridCell cell = _inboundCells[x, y];
                    if (cell == null || cell.IsBorder)
                    {
                        continue;
                    }

                    if (cell.Occupant != null && cell.Occupant.Flags.HasFlag(GridOccupantFlag.RemoveBase))
                    {
                        continue;
                    }
                    wrange.x = Math.Min(wrange.x, x);
                    wrange.y = Math.Max(wrange.y, x);
                    hrange.x = Math.Min(hrange.x, y);
                    hrange.y = Math.Max(hrange.y, y);
                }
            }

            if (wrange.x > wrange.y || hrange.x > hrange.y)
            {
                EffectiveGridSize = GridCoord.zero;
                CenterPosition = Vector3.zero;
                return;
            }

            EffectiveGridSize = new GridCoord(
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

        public GridCell AccessCell(GridCoord position)
        {
            return AccessCell(position.x, position.y);
        }

        public void EnableRegion(GridCoord size)
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

            _gridSize = GridCoord.zero;
        }

        /// <summary>
        /// Runtime resize. Clamps to the pooled region (cells only exist within
        /// pool+border bounds), re-enables the active region for the new size and
        /// refreshes border/effective-size data. Returns the applied size.
        /// </summary>
        public GridCoord Resize(GridCoord size, bool[] mapping = null)
        {
            GridCoord clamped = ClampToPool(size);
            EnableRegion(clamped);

            if (mapping != null)
            {
                for (int x = 0; x < clamped.x; ++x)
                {
                    for (int y = 0; y < clamped.y; ++y)
                    {
                        GridCell cell = AccessCell(new GridCoord(x, y));
                        cell.IsBorder = !mapping[y * size.x + x];
                        _cellFactory.RefreshCellView(cell);
                    }
                }
            }

            RefreshBorder();
            return clamped;
        }

        public void RefreshBorder()
        {
            if (_inboundCells == null)
            {
                return;
            }

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

        public GridCell GetCellRelativeTo(GridCell cell, GridCoord relativePosition)
        {
            if (cell == null) return null;
            GridCoord targetPosition = cell.GridPosition + relativePosition;
            return !CheckValidGridPosition(targetPosition) ? null : _inboundCells[targetPosition.x, targetPosition.y];
        }

        public GridCell GetNeighborCell(int x, int y, DirectionFlag direction)
        {
            GridCoord relativePosition = direction.ToGridCoord();
            return GetCellRelativeTo(_inboundCells[x, y], relativePosition);
        }

        public GridCell GetNeighborCell(GridCell cell, DirectionFlag direction)
        {
            if (cell == null) return null;

            GridCoord relativePosition = direction.ToGridCoord();
            return GetCellRelativeTo(cell, relativePosition);
        }

        public bool CheckValidGridPosition(GridCoord gridPosition)
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

                GridCoord relativePosition = occupant.GetRelativePositionToHookedCell(from);
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

        public bool PlaceObjectAtPosition(GridCoord gridPosition, IGridOccupant occupant)
        {
            if (!CheckValidGridPosition(gridPosition.x, gridPosition.y)) return false;

            GridCell cell = _inboundCells[gridPosition.x, gridPosition.y];
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

        public bool CheckObjectPlaceable(GridCoord position, IGridOccupant occupant)
        {
            if (!CheckValidGridPosition(position.x, position.y)) return false;
            GridCell cell = _inboundCells[position.x, position.y];
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

        /// <summary>
        /// Creates all pooled cells but yields back to the caller every
        /// <paramref name="cellsPerChunk"/> cells so large grids can be built
        /// incrementally across frames. The caller drives the iterator (coroutine
        /// or a Task loop). <see cref="AllCellsCreated"/> fires once at the end.
        /// </summary>
        public IEnumerator CreateAllCellsChunked(int cellsPerChunk)
        {
            _inboundCells = new GridCell[_poolSize.x, _poolSize.y];
            _pooledCells = new GridCell[
                _poolSize.x + _borderSize.x * 2,
                _poolSize.y + _borderSize.y * 2
            ];

            int cellsSinceYield = 0;
            for (int x = -_borderSize.x; x < _poolSize.x + _borderSize.x; x++)
            {
                for (int y = -_borderSize.y; y < _poolSize.y + _borderSize.y; y++)
                {
                    CreateSingleCell(x, y);
                    if (cellsPerChunk > 0 && ++cellsSinceYield >= cellsPerChunk)
                    {
                        cellsSinceYield = 0;
                        yield return null;
                    }
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

        private GridCoord ClampToPool(GridCoord size) => new GridCoord(
            Mathf.Clamp(size.x, 1, _poolSize.x),
            Mathf.Clamp(size.y, 1, _poolSize.y)
        );

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
            GridCoord position, ref DirectionFlag movement
        )
        {
            if (occupant.PlaceableObject.Grid[position] == PlaceableObjectGrid.GridCellEmptySentinelValue) return;
            foreach (DirectionFlag direction in DirectionFlagHelper.AllCellDirections)
            {
                GridCoord offset = direction.ToGridCoord();
                GridCoord predict = cell.GridPosition + offset;
                if (!ValidateCellPlaceable(cell, occupant, predict, position))
                {
                    movement &= ~direction;
                    continue;
                }

                // No corner cutting: a diagonal move is only possible when BOTH
                // orthogonal neighbors are also clear. This keeps free diagonal
                // movement in open space while forcing cornering (D→B→A) when one
                // intermediate is blocked (e.g. an obstacle at C blocks D→A west).
                if ((direction & DirectionFlag.Diagonal) != 0)
                {
                    GridCoord horizontal = cell.GridPosition + new GridCoord(offset.x, 0);
                    GridCoord vertical = cell.GridPosition + new GridCoord(0, offset.y);
                    if (!ValidateCellPlaceable(cell, occupant, horizontal, position)
                        || !ValidateCellPlaceable(cell, occupant, vertical, position))
                    {
                        movement &= ~direction;
                    }
                }
            }
        }

        private bool ValidateCellPlaceable(
            GridCell cell, IGridOccupant occupant,
            GridCoord predictPosition, GridCoord relPosition
        )
        {
            if (!CheckValidGridPosition(predictPosition.x, predictPosition.y)) return false;
            GridCell predictedCell = _inboundCells[predictPosition.x, predictPosition.y];
            if (predictedCell.IsBorder) return false;
            if (!predictedCell.CheckOccupantPlaceable(occupant)) return false;
            IGridOccupant perdOccupant = predictedCell.Occupant;
            if (perdOccupant == null) return true;
            if (perdOccupant == occupant) return true;
            return perdOccupant.CheckReplaceableBy(occupant, cell, relPosition);
        }

        private void CheckSingleCellPartPlaceable(
            GridCell cell, IGridOccupant occupant,
            GridCoord position, ref bool success
        )
        {
            if (occupant.PlaceableObject.Grid[position] == PlaceableObjectGrid.GridCellEmptySentinelValue) return;
            if (ValidateCellPlaceable(cell, occupant, cell.GridPosition, position)) return;

            success = false;
        }

        private void PlaceSingleCellPart(
            GridCell cell, IGridOccupant occupant,
            GridCoord position, ref bool success
        )
        {
            if (occupant.PlaceableObject.Grid[position] == PlaceableObjectGrid.GridCellEmptySentinelValue) return;
            cell.Occupant = occupant;
            success = true;
        }

        private void RemoveSingleCellPart(
            GridCell cell, IGridOccupant occupant,
            GridCoord position, ref bool success
        )
        {
            if (occupant.PlaceableObject.Grid[position] == PlaceableObjectGrid.GridCellEmptySentinelValue) return;
            cell.Occupant = null;
            success = true;
        }

        private void CollectCellAdjacentsNonAlloc(
            GridCell cell, IGridOccupant occupant,
            GridCoord relativePosition,
            ref AdjacentCellNonAllocCollectorData actionData
        )
        {
            if (cell.Occupant != occupant) return;
            GridCoord directionOffset = actionData.Direction.ToGridCoord();
            GridCoord targetPosition = cell.GridPosition + directionOffset;

            if (!CheckValidGridPosition(targetPosition)) return;
            GridCell targetCell = _inboundCells[targetPosition.x, targetPosition.y];
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
            GridCoord ps = occupant.PlaceableObject.Grid.Size; // Placement Size
            GridCoord pp = cell.GridPosition; // Placement Position

            for (int x = 0; x < ps.x; x++)
            {
                for (int y = 0; y < ps.y; y++)
                {
                    ExecuteSingleCellObjectPlacementAction(
                        occupant, action, pp,
                        new GridCoord(x, y),
                        ref actionData
                    );
                }
            }
        }

        private T ExecuteSingleCellObjectPlacementAction<T>(
            IGridOccupant occupant, SingleCellAction<T> action,
            GridCoord pp, GridCoord relativePosition, ref T actionData
        )
        {
            GridCoord cellPosition =
                CalculateObjectCellPosition(pp, relativePosition, occupant.PlaceableObject.Offset);

            if (CheckValidGridPosition(cellPosition.x, cellPosition.y))
            {
                action?.Invoke(
                    _inboundCells[cellPosition.x, cellPosition.y], occupant, relativePosition,
                    ref actionData
                );
            }

            return actionData;
        }

        private GridCoord CalculateObjectCellPosition(
            GridCoord placementPosition, GridCoord relativePosition,
            GridCoord offset
        )
            => new GridCoord(
                placementPosition.x + relativePosition.x + offset.x,
                placementPosition.y + relativePosition.y + offset.y
            );

        private void CreateSingleCell(int x, int y)
        {
            GridCell cell = new GridCell();
            cell.Map = this;
            cell.GridPosition = new GridCoord(x, y);

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
            GridCoord relativePosition,
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
            public GridCoord RelativePosition;
        }

        #endregion
    }
}
