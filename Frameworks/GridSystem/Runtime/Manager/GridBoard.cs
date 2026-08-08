using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// User-placed per-grid component (drop-car's GridMap GO equivalent). Has no
    /// Update() — it is ticked by <see cref="GridManager"/>. Owns the pure logic
    /// core (GridMap / GridDrifter / GridSnappingAdapter) plus the resolved
    /// providers.
    /// </summary>
    [AddComponentMenu("Scheherazade/Grid System/Grid Board")]
    public class GridBoard : MonoBehaviour
    {
        #region Events & Delegates

        public event Action<GridCell> MouseDownOnCell;
        public event Action<GridCell> MouseDragOnCell;
        public event Action<GridCell> MouseUpOnCell;
        public event Action AllCellsCleared;
        public event Action AllCellsCreated;
        public event Action SnappedCellChanged;          // forwarded from GridSnappingAdapter
        public event Action<IDrifter> DrifterAppended;   // forwarded from GridDrifter
        public event Action DriftingFinished;            // forwarded from GridDrifter

        #endregion

        #region Properties

        public GridConfiguration Configuration => configuration;
        public GridMap Map => _map;
        public GridCell[,] CellObjects => _map != null ? _map.CellObjects : null;
        public Vector2Int GridSize => configuration != null ? configuration.GridSize : Vector2Int.zero;
        public IGridCoordinateProvider Coordinates => _coordinates;
        public Vector2 PointerPlanePosition { get; private set; }
        public bool Initialized { get; private set; }

        #endregion

        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Per-board configuration asset (id, sizes, providers).")]
#endif
        [SerializeField]
        private GridConfiguration configuration;

        #endregion

        #region Private Fields

        private GridMap _map;
        private GridDrifter _drifter;
        private GridSnappingAdapter _snappingAdapter;

        private IGridPointerProvider _pointer;
        private IGridCoordinateProvider _coordinates;
        private IGridCellFactoryProvider _cellFactory;
        private IGridFeedbackProvider _feedback;
        private IGridGameStateProvider _gameState;
        private IBorderGenerator _borderGenerator;

        private GridCell _downCell;
        private bool _isPointerDown;

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            if (configuration == null)
            {
                QuickLog.Error<GridBoard>(
                    "GridBoard '{0}' has no GridConfiguration assigned. Board disabled.",
                    name
                );
                enabled = false;
                return;
            }

            GridManager.EnsureCreated().Register(this);
        }

        private void OnDisable()
        {
            if (GridManager.Instance != null)
            {
                GridManager.Instance.Unregister(this);
            }
        }

        #endregion

        #region Public Methods

        public void Initialize()
        {
            ResolveProviders();

            _map = new GridMap(configuration);
            _drifter = new GridDrifter(this, _map, configuration, _feedback);
            _snappingAdapter = new GridSnappingAdapter(_map, _drifter);

            _snappingAdapter.SnappedCellChanged += () => SnappedCellChanged?.Invoke();
            _drifter.DrifterAppended += drifter => DrifterAppended?.Invoke(drifter);
            _drifter.DriftingFinished += () => DriftingFinished?.Invoke();
            MouseDownOnCell += cell => _drifter.HandleMouseDownOnCell(cell);

            _map.AllCellsCreated += () => AllCellsCreated?.Invoke();
            _map.AllCellsCleared += () => AllCellsCleared?.Invoke();

            _map.CreateAllCells();
            _map.EnableRegion(configuration.GridSize);
            RefreshBorder();

            Initialized = true;
            QuickLog.Info<GridBoard>(
                "Board '{0}' initialized. Grid {1}x{2}.",
                configuration.Id,
                configuration.GridSize.x,
                configuration.GridSize.y
            );
        }

        public void Shutdown()
        {
            if (!Initialized) return;
            _map.ClearAllCells();
            Initialized = false;
        }

        public void Tick()
        {
            if (!Initialized) return;

            if (_gameState != null && (_gameState.IsPaused || !_gameState.IsInteractionActive))
            {
                ReleasePointer();
                return;
            }

            if (_coordinates == null)
            {
                ReleasePointer();
                return;
            }

            if (_pointer == null || !_pointer.Ready || !_pointer.IsPointerActive || _pointer.IsPointerOverUI)
            {
                ReleasePointer();
                return;
            }

            if (!_coordinates.TryRaycastGridPlane(_pointer.GetPointerRay(), out Vector3 point))
            {
                ReleasePointer();
                return;
            }

            Vector2 planePosition = _coordinates.Flatten(point);
            PointerPlanePosition = planePosition;
            GridCell cell = _map.AccessCell(_coordinates.WorldToCell(point));
            if (cell == null)
            {
                ReleasePointer();
                return;
            }

            if (!_isPointerDown)
            {
                _isPointerDown = true;
                _downCell = cell;
                MouseDownOnCell?.Invoke(cell);
            }
            else
            {
                MouseDragOnCell?.Invoke(cell);
            }

            _drifter.UpdateDrifting(planePosition);
            _snappingAdapter.UpdateSnapping();
        }

        public void PlaceObjectAtPosition(Vector2Int position, IGridOccupant occupant)
            => _map.PlaceObjectAtPosition(position, occupant);

        public bool CheckObjectPlaceable(Vector2Int position, IGridOccupant occupant)
            => _map.CheckObjectPlaceable(position, occupant);

        public bool MoveOccupants(
            IEnumerable<IGridOccupant> occupants,
            GridCell from,
            GridCell to
        )
            => _map.MoveOccupants(occupants, from, to);

        public bool RemoveObject(IGridOccupant occupant) => _map.RemoveObject(occupant);
        public GridCell AccessCell(Vector2Int position) => _map.AccessCell(position);
        public void Clear() => _map.ClearAllCells();
        public void ResetMap() => _map.ResetMap();

        #endregion

        #region Private Methods

        private void ResolveProviders()
        {
            _coordinates = configuration.CoordinateProvider;
            _pointer = configuration.PointerProvider;
            _cellFactory = configuration.CellFactory;   // used by GridMap via config
            _feedback = configuration.FeedbackProvider;
            _gameState = configuration.GameStateProvider;
            _borderGenerator = configuration.BorderGenerator;

            if (_coordinates == null)
            {
                QuickLog.Error<GridBoard>(
                    "Board '{0}': no IGridCoordinateProvider.",
                    configuration.Id
                );
            }

            if (_pointer == null)
            {
                QuickLog.Warning<GridBoard>(
                    "Board '{0}': no IGridPointerProvider.",
                    configuration.Id
                );
            }
        }

        private void RefreshBorder() => _borderGenerator?.Combine(_map.BuildBorderData(), _map.GridSize);

        private void ReleasePointer()
        {
            if (!_isPointerDown) return;
            _isPointerDown = false;
            MouseUpOnCell?.Invoke(_downCell);
            _downCell = null;
            _drifter.ReleaseAllDrifters();
        }

        #endregion
    }
}
