using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.GridSystem
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
        private bool _hasDisplaced;

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
            _hasDisplaced = false;
            (drifter as IGridReleasePresentation)?.PrepareForDrift();
            _drifters.Add(drifter, new DrifterInformation
            {
                Drifter = drifter,
                MovementAbility = DirectionFlag.All,
                CurrentSpeed = GetDrifterBaseSpeed(),
                IntentPointerAnchor = _board.PointerPlanePosition,
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

            RecenterDriftersOnHookedCells();

            _feedback?.PlayDriftRelease(_driftMaster);
            _drifters.Clear();
            _driftMaster = null;
            _hoveringCell = null;
            _drifting = false;
            _hasDisplaced = false;
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
                _hasDisplaced = false;
                DriftingStarted?.Invoke();
            }

            (drifter as IGridReleasePresentation)?.PrepareForDrift();
            _drifters.Add(drifter, new DrifterInformation
            {
                Drifter = drifter,
                MovementAbility = DirectionFlag.All,
                CurrentSpeed = GetDrifterBaseSpeed(),
                IntentPointerAnchor = _board.PointerPlanePosition,
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
            if (!_drifters.Remove(drifter))
            {
                return;
            }

            drifter.HandleDriftDetached();
            if (drifter == _driftMaster)
            {
                _driftMaster = GetFirstDrifter();
            }

            DrifterRemoved?.Invoke();

            if (_drifters.Count > 0)
            {
                return;
            }

            _drifting = false;
            _hasDisplaced = false;
            _driftMaster = null;
            _hoveringCell = null;
            DriftingFinished?.Invoke();
        }

        #endregion

        #region Private Methods

        // Only successful occupancy commits count; visual nudges and rejected
        // placement attempts must not consume the initial blocked-move feedback.
        internal void NotifyGridDisplacement() => _hasDisplaced = true;

        /// <summary>
        /// Commits the final gameplay position before release callbacks. Optional
        /// presentation support can ease the visible pose onto that position.
        /// </summary>
        private void RecenterDriftersOnHookedCells()
        {
            IGridCoordinateProvider coordinates = _board.Coordinates;
            if (coordinates == null) return;

            foreach (var (drifter, _) in _drifters)
            {
                if (drifter.Occupant?.HookedCell != null)
                {
                    Vector3 previousPosition = drifter.ControlledPosition;
                    drifter.ControlledPosition =
                        coordinates.CellToWorld(drifter.Occupant.HookedCell.GridPosition);
                    (drifter as IGridReleasePresentation)?.AnimateRelease(
                        previousPosition, _config.ReleaseSnapDuration);
                }
            }
        }

        private IDrifter GetFirstDrifter()
        {
            foreach (IDrifter drifter in _drifters.Keys)
            {
                return drifter;
            }

            return null;
        }

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
            float hysteresis = Mathf.Max(0f, _config.DrifterAxisHysteresis);
            data.UpdatePointerIntent(pointer, hysteresis);
            Vector2 desired = pointer - data.RelativeMousePosition;
            Vector2 current = _board.Coordinates.Flatten(drifter.ControlledPosition);
            Vector2 hook = _board.Coordinates.Flatten(
                _board.Coordinates.CellToWorld(drifter.HookedCell.GridPosition)
            );

            DirectionFlag requestedDirection = (desired - hook).ToDirectionFlag();
            bool hasMovement = requestedDirection != DirectionFlag.None;
            bool movementBlocked = IsMovementBlocked(requestedDirection);

            float nudge = _config.DisableNudgingAfterDisplacement && _hasDisplaced
                ? 0f : _config.NudgeAmount;
            desired.x = ClampAxis(
                desired.x, DirectionFlag.West, DirectionFlag.East,
                hook.x, nudge
            );
            desired.y = ClampAxis(
                desired.y, DirectionFlag.South, DirectionFlag.North,
                hook.y, nudge
            );

            _movementDirection = (desired - hook).ToDirectionFlag();
            DirectionFlag requestedDiagonal = requestedDirection & DirectionFlag.Diagonal;
            bool blockedDiagonal = requestedDiagonal != DirectionFlag.None
                && (requestedDiagonal & _movementAbility) == 0;
            if (blockedDiagonal)
            {
                DirectionFlag horizontalDirection =
                    requestedDirection & DirectionFlag.Horizontal;
                DirectionFlag verticalDirection =
                    requestedDirection & DirectionFlag.Vertical;
                bool horizontalFree = horizontalDirection != DirectionFlag.None
                    && (_movementAbility & horizontalDirection) != 0;
                bool verticalFree = verticalDirection != DirectionFlag.None
                    && (_movementAbility & verticalDirection) != 0;

                float horizontalDistance = Mathf.Abs(desired.x - hook.x);
                float verticalDistance = Mathf.Abs(desired.y - hook.y);
                bool preferHorizontal = horizontalFree != verticalFree
                    ? horizontalFree
                    : horizontalDistance > verticalDistance;
                if (horizontalFree == verticalFree
                    && hysteresis > 0f
                    && (_movementAbility & DirectionFlag.Diagonal) != DirectionFlag.Diagonal
                    && Mathf.Max(horizontalDistance, verticalDistance) <= hysteresis
                    && data.PreferredAxis != DirectionFlag.None)
                {
                    preferHorizontal = data.PreferredAxis == DirectionFlag.Horizontal;
                }

                if (preferHorizontal)
                {
                    desired.y = hook.y;
                    _movementDirection &= DirectionFlag.Horizontal;
                }
                else
                {
                    desired.x = hook.x;
                    _movementDirection &= DirectionFlag.Vertical;
                }
            }

            float speed = ResolveDrifterSpeed(data, movementBlocked, hasMovement);
            return Vector2.MoveTowards(current, desired, speed);
        }

        private bool IsMovementBlocked(DirectionFlag direction)
        {
            DirectionFlag diagonal = direction & DirectionFlag.Diagonal;
            if (diagonal != DirectionFlag.None)
            {
                return (_movementAbility & diagonal) == 0;
            }

            DirectionFlag cardinal = direction & DirectionFlag.Cardinal;
            return cardinal != DirectionFlag.None
                && (_movementAbility & cardinal) != cardinal;
        }

        private float ResolveDrifterSpeed(
            DrifterInformation data,
            bool movementBlocked,
            bool hasMovement
        )
        {
            float baseSpeed = GetDrifterBaseSpeed();
            float speedLimit = Mathf.Max(0f, _config.DrifterSpeedLimit);
            if (movementBlocked)
            {
                data.CurrentSpeed = baseSpeed;
                return baseSpeed;
            }

            float speed = Mathf.Clamp(data.CurrentSpeed, baseSpeed, speedLimit);
            if (hasMovement)
            {
                data.CurrentSpeed = Mathf.Min(
                    speedLimit,
                    speed + Mathf.Max(0f, _config.DrifterAcceleration)
                );
            }

            return speed;
        }

        private float GetDrifterBaseSpeed()
            => Mathf.Min(
                Mathf.Max(0f, _config.DrifterBaseSpeed),
                Mathf.Max(0f, _config.DrifterSpeedLimit)
            );

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
