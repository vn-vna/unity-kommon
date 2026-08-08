using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Drift movement module (drop-car <c>GridDrifter</c> ported to a pure class
    /// owned by <see cref="GridBoard"/>). Drift math operates in grid-plane space
    /// via the coordinate provider — the module never sees world axes.
    /// </summary>
    public class GridDrifter
    {
        #region Events & Delegates

        public event Action<IDrifter> DrifterAppended;
        public event Action DrifterRemoved;
        public event Action DriftingStarted;
        public event Action DriftingFinished;

        #endregion

        #region Properties

        public IDrifter DriftMaster => _driftMaster;
        public GridCell HoveringCell => _hoveringCell;
        public GridCell MasterCell => _driftMaster?.HookedCell;
        public IEnumerable<IDrifter> Drifters => _drifters.Keys;
        public bool Drifting => _drifting;
        public DirectionFlag MovementAbility => _movementAbility;
        public DirectionFlag MovementDirection => _movementDirection;
        public bool Enabled { get; set; } = true;

        #endregion

        #region Private Fields

        private readonly GridBoard _board;
        private readonly GridMap _map;
        private readonly GridConfiguration _config;
        private readonly IGridFeedbackProvider _feedback;

        private readonly Dictionary<IDrifter, DrifterInformation> _drifters
            = new Dictionary<IDrifter, DrifterInformation>();

        private IDrifter _driftMaster;
        private GridCell _hoveringCell;
        private DirectionFlag _movementAbility;
        private DirectionFlag _movementDirection;
        private bool _drifting;

        #endregion

        #region Construction

        public GridDrifter(
            GridBoard board,
            GridMap map,
            GridConfiguration config,
            IGridFeedbackProvider feedback
        )
        {
            _board = board;
            _map = map;
            _config = config;
            _feedback = feedback;
        }

        #endregion

        #region Public Methods

        /// <summary>Called by <see cref="GridBoard"/> on MouseDownOnCell — attaches an IDrifter occupant.</summary>
        public void HandleMouseDownOnCell(GridCell cell)
        {
            if (cell == null || cell.Occupant is not IDrifter drifter) return;

            if (!drifter.Driftable)
            {
                drifter.HandleDrifterFailed();
                _feedback?.PlayDriftFail(drifter);
                return;
            }

            _driftMaster = drifter;
            _drifting = true;
            _drifters.Clear();
            _drifters.Add(drifter, new DrifterInformation
            {
                Drifter = drifter,
                MovementAbility = DirectionFlag.All,
                RelativeMousePosition = _board.PointerPlanePosition
                    - _board.Coordinates.Flatten(drifter.ControlledPosition)
            });
            drifter.HandleDriftAttached();
            DriftingStarted?.Invoke();
            DrifterAppended?.Invoke(drifter);
            _feedback?.PlayDriftStart(drifter);
        }

        /// <summary>Called by <see cref="GridBoard"/> on pointer release.</summary>
        public void ReleaseAllDrifters()
        {
            if (!_drifting) return;

            foreach (var (drifter, _) in _drifters)
            {
                drifter.HandleDriftDetached();
            }
            _feedback?.PlayDriftRelease(_driftMaster);
            _drifters.Clear();
            _driftMaster = null;
            _hoveringCell = null;
            _drifting = false;
            DrifterRemoved?.Invoke();
            DriftingFinished?.Invoke();
        }

        /// <summary>Per-tick movement (called from <see cref="GridBoard.Tick"/> after touch resolution).</summary>
        public void UpdateDrifting(Vector2 pointerPlanePosition)
        {
            if (!Enabled || !_drifting || _drifters.Count == 0) return;

            foreach (var (drifter, _) in _drifters)
            {
                drifter.HandleDrifterPreUpdate();
            }

            UpdateDrifterMovementAbility();

            foreach (var (drifter, data) in _drifters)
            {
                Vector2 newPos = ClampDrifterPosition(drifter, data, pointerPlanePosition);
                drifter.ControlledPosition = _board.Coordinates.Unflatten(
                    newPos, drifter.ControlledPosition
                );
                drifter.HandleDriftUpdated();
            }

            _hoveringCell = _map.AccessCell(
                _board.Coordinates.WorldToCell(_driftMaster.ControlledPosition)
            );
        }

        public bool ContainsDrifters(params IDrifter[] drifters)
        {
            foreach (IDrifter drifter in drifters)
            {
                if (!_drifters.ContainsKey(drifter))
                {
                    return false;
                }
            }

            return true;
        }

        public void AppendDrifter(IDrifter drifter)
        {
            if (_drifters.ContainsKey(drifter))
            {
                return;
            }

            if (_drifters.Count == 0)
            {
                DriftingStarted?.Invoke();
            }

            _drifters.Add(drifter, new DrifterInformation
            {
                Drifter = drifter,
                MovementAbility = DirectionFlag.All,
                RelativeMousePosition = _board.PointerPlanePosition
                    - _board.Coordinates.Flatten(drifter.ControlledPosition)
            });

            if (_drifters.Count == 1)
            {
                _driftMaster = drifter;
            }

            drifter.HandleDriftAttached();
        }

        public void RemoveDrifter(IDrifter drifter)
        {
            if (drifter == _driftMaster)
            {
                foreach (KeyValuePair<IDrifter, DrifterInformation> pair in _drifters)
                {
                    if (pair.Key == drifter) continue;
                    _driftMaster = pair.Key;
                    break;
                }
                return;
            }

            if (!_drifters.Remove(drifter)) return;
            drifter.HandleDriftDetached();
            DrifterRemoved?.Invoke();

            if (_drifters.Count == 0)
            {
                _drifting = false;
                _driftMaster = null;
                DriftingFinished?.Invoke();
            }
        }

        #endregion

        #region Private Methods

        private void UpdateDrifterMovementAbility()
        {
            foreach (var (drifter, data) in _drifters)
            {
                if (drifter.HookedCell == null)
                {
                    data.MovementAbility = DirectionFlag.All;
                    continue;
                }

                data.MovementAbility = drifter.Driftable
                    ? _map.CheckObjectMovement(drifter.Occupant) & drifter.MovementLimitations
                    : DirectionFlag.None;
            }

            DirectionFlag combined = DirectionFlag.All;
            foreach (DrifterInformation data in _drifters.Values)
            {
                combined &= data.MovementAbility;
            }
            foreach (var (drifter, data) in _drifters)
            {
                data.MovementAbility = combined;
                drifter.ControlledMovementMask = combined | DirectionFlag.Cardinal;
            }
            _movementAbility = combined;
        }

        private Vector2 ClampDrifterPosition(
            IDrifter drifter, DrifterInformation data, Vector2 pointer
        )
        {
            Vector2 desired = pointer - data.RelativeMousePosition;
            Vector2 current = _board.Coordinates.Flatten(drifter.ControlledPosition);
            Vector2 hook = _board.Coordinates.Flatten(
                _board.Coordinates.CellToWorld(drifter.HookedCell.GridPosition)
            );

            float distance = Vector2.Distance(desired, current);
            if (distance > _config.DrifterSpeedLimit)
            {
                desired = current + (desired - current).normalized * _config.DrifterSpeedLimit;
            }

            desired.x = ClampAxis(
                desired.x, DirectionFlag.West, DirectionFlag.East,
                hook.x, _config.NudgeAmount
            );
            desired.y = ClampAxis(
                desired.y, DirectionFlag.South, DirectionFlag.North,
                hook.y, _config.NudgeAmount
            );

            _movementDirection = (desired - hook).ToDirectionFlag();
            if ((_movementDirection & DirectionFlag.Diagonal & _movementAbility) == 0)
            {
                if (Mathf.Abs(desired.x - hook.x) > Mathf.Abs(desired.y - hook.y))
                {
                    desired.y = hook.y;
                }
                else
                {
                    desired.x = hook.x;
                }
            }

            return desired;
        }

        private float ClampAxis(
            float value, DirectionFlag negDir, DirectionFlag posDir,
            float pivot, float nudge
        )
            => Mathf.Clamp(
                value,
                _movementAbility.HasFlag(negDir)
                    ? float.NegativeInfinity
                    : pivot - Mathf.Min(nudge, Mathf.Abs(pivot - value)),
                _movementAbility.HasFlag(posDir)
                    ? float.PositiveInfinity
                    : pivot + Mathf.Min(nudge, Mathf.Abs(pivot - value))
            );

        #endregion
    }
}
