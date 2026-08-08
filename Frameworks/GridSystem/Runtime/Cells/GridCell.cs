using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Pure logic cell — drop-car <c>GridMapCell</c> ported with no MonoBehaviour.
    /// Position is data (<see cref="GridPosition"/>); world mapping is entirely
    /// the coordinate provider's job.
    /// </summary>
    public class GridCell
    {
        #region Events

        public event Action<GridCell, IGridOccupant, IGridOccupant> OccupantChanged;
        public event Action<GridCell, bool> SelectedChanged;
        public event Action<GridCell, GridCellFlag> CellFlagChanged;

        #endregion

        #region Properties

        /// <summary>Owning map (set on creation by <see cref="GridMap"/>).</summary>
        internal GridMap Map { get; set; }

        public Vector2Int GridPosition { get; set; }

        public HashSet<ICellExternalController> ExternalControllers { get; private set; } =
            new HashSet<ICellExternalController>();

        public GridCellFlag Flags
        {
            get => _flags;
            set
            {
                if (_flags == value) return;
                _flags = value;
                CellFlagChanged?.Invoke(this, value);
            }
        }

        public bool Selected
        {
            get => Flags.HasFlag(GridCellFlag.Selected);
            set
            {
                if (Selected == value) return;
                Flags = value
                    ? Flags | GridCellFlag.Selected
                    : Flags & ~GridCellFlag.Selected;
                SelectedChanged?.Invoke(this, value);
            }
        }

        public bool Tracked
        {
            get => Flags.HasFlag(GridCellFlag.Tracked);
            set
            {
                if (Tracked == value) return;
                Flags = value
                    ? Flags | GridCellFlag.Tracked
                    : Flags & ~GridCellFlag.Tracked;
            }
        }

        public bool Debugging
        {
            get => Flags.HasFlag(GridCellFlag.Debug);
            set
            {
                if (Debugging == value) return;
                Flags = value
                    ? Flags | GridCellFlag.Debug
                    : Flags & ~GridCellFlag.Debug;
            }
        }

        public bool IsBorder
        {
            get => Flags.HasFlag(GridCellFlag.Border);
            set
            {
                if (IsBorder == value) return;
                Flags = value
                    ? Flags | GridCellFlag.Border
                    : Flags & ~GridCellFlag.Border;
            }
        }

        public IGridOccupant Occupant
        {
            get => _occupant;
            set
            {
                if (_occupant == value) return;
                IGridOccupant prev = _occupant;
                _occupant = value;
                OccupantChanged?.Invoke(this, prev, _occupant);
            }
        }

        #endregion

        #region Private Fields

        private GridCellFlag _flags;
        private IGridOccupant _occupant;

        #endregion

        #region Public Methods

        public void ResetCell()
        {
            _occupant = null;
            _flags = GridCellFlag.None;
        }

        public void AttachExternalController(ICellExternalController controller)
        {
            ExternalControllers.Add(controller);
            controller.ControlledGridMapCell = this;
            controller.HandleControllerAttached(this);
        }

        public void DetachExternalController(ICellExternalController controller)
        {
            controller.HandleControllerDetached(this);
            controller.ControlledGridMapCell = null;
            ExternalControllers.Remove(controller);
        }

        public IEnumerable<T> EnumerateExternalControllers<T>()
            where T : class, ICellExternalController
        {
            foreach (ICellExternalController controller in ExternalControllers)
            {
                if (controller is not T typedController) continue;
                yield return typedController;
            }
        }

        public bool CheckOccupantPlaceable(IGridOccupant occupant)
        {
            foreach (ICellExternalController controller in ExternalControllers)
            {
                if (controller == null) continue;
                if (!controller.CheckObjectPlaceable(occupant, this)) return false;
            }

            return true;
        }

        public IEnumerable<GridCell> GetAdjacentCells(DirectionFlag direction)
        {
            if (Map == null) yield break;

            if (direction.HasFlag(DirectionFlag.North))
            {
                GridCell c = Map.AccessCell(GridPosition + new Vector2Int(0, 1));
                if (c != null) yield return c;
            }

            if (direction.HasFlag(DirectionFlag.East))
            {
                GridCell c = Map.AccessCell(GridPosition + new Vector2Int(1, 0));
                if (c != null) yield return c;
            }

            if (direction.HasFlag(DirectionFlag.South))
            {
                GridCell c = Map.AccessCell(GridPosition + new Vector2Int(0, -1));
                if (c != null) yield return c;
            }

            if (direction.HasFlag(DirectionFlag.West))
            {
                GridCell c = Map.AccessCell(GridPosition + new Vector2Int(-1, 0));
                if (c != null) yield return c;
            }

            if (direction.HasFlag(DirectionFlag.NorthEast))
            {
                GridCell c = Map.AccessCell(GridPosition + new Vector2Int(1, 1));
                if (c != null) yield return c;
            }

            if (direction.HasFlag(DirectionFlag.SouthEast))
            {
                GridCell c = Map.AccessCell(GridPosition + new Vector2Int(1, -1));
                if (c != null) yield return c;
            }

            if (direction.HasFlag(DirectionFlag.SouthWest))
            {
                GridCell c = Map.AccessCell(GridPosition + new Vector2Int(-1, -1));
                if (c != null) yield return c;
            }

            if (direction.HasFlag(DirectionFlag.NorthWest))
            {
                GridCell c = Map.AccessCell(GridPosition + new Vector2Int(-1, 1));
                if (c != null) yield return c;
            }
        }

        #endregion
    }
}
