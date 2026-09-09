using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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
        public GridCoord GridSize => _map != null
            ? _map.GridSize
            : (configuration != null ? configuration.GridSize : GridCoord.zero);
        public GridCoord PoolSize => configuration != null ? configuration.PoolSize : GridCoord.zero;
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

#if UNITY_EDITOR
        [Tooltip("Cells created per frame/chunk during InitializeAsync/InitializeCoroutine (0 = create everything in one pass).")]
#endif
        [SerializeField]
        private int _cellsPerChunk = 256;

#if UNITY_EDITOR
        [Tooltip("Parent transform for spawned cell views. Leave empty to auto-create a container under this board.")]
#endif
        [SerializeField]
        private Transform cellRoot;

#if UNITY_EDITOR
        [Tooltip("Auto-create a child container under this board when cellRoot is empty.")]
#endif
        [SerializeField]
        private bool autoCreateCellContainer = true;

#if UNITY_EDITOR
        [Tooltip("Name of the auto-created cell container.")]
#endif
        [SerializeField]
        private string cellContainerName = "Cells";

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
        private bool _isInitializing;
        private CancellationTokenSource _initCts;

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
            _initCts?.Cancel();
            _initCts?.Dispose();
            _initCts = null;
            StopAllCoroutines();

            if (GridManager.Instance != null)
            {
                GridManager.Instance.Unregister(this);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Synchronously initializes the board from its configuration. If the
        /// board is already initialized it is torn down and rebuilt first.
        /// </summary>
        public void Initialize()
        {
            if (_isInitializing) return;
            _isInitializing = true;
            try
            {
                ShutdownIfInitialized();
                InitializeCore();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// Awaitable-based initialization. Cell views are created in chunks
        /// (<see cref="_cellsPerChunk"/>) across frames on the main thread so
        /// large grids do not stall a single frame. Cancellable via token or by
        /// disabling the board.
        /// </summary>
        public async Awaitable InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_isInitializing) return;
            _isInitializing = true;
            try
            {
                ShutdownIfInitialized();

                CancellationToken token = GetInitCts().Token;
                cancellationToken.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();

                ResolveProviders();
                CreateModules();

                IEnumerator chunked = _map.CreateAllCellsChunked(_cellsPerChunk);
                while (chunked.MoveNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    token.ThrowIfCancellationRequested();
                    await Awaitable.NextFrameAsync();
                }

                FinalizeInitialization();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// Coroutine-based initialization. Equivalent to
        /// <see cref="InitializeAsync"/> but driven by a Unity coroutine.
        /// </summary>
        public IEnumerator InitializeCoroutine()
        {
            if (_isInitializing) yield break;
            _isInitializing = true;
            try
            {
                ShutdownIfInitialized();
                yield return InitializeCoreCoroutine(_cellsPerChunk);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public void Shutdown()
        {
            if (!Initialized) return;

            if (_map != null)
            {
                _map.ClearAllCells();
                UnsubscribeModules();
                _map = null;
                _drifter = null;
                _snappingAdapter = null;
            }

            Initialized = false;
        }

        /// <summary>
        /// Runtime resize of the active grid region. The requested size is clamped
        /// to the configured pool size. Requires the board to be initialized.
        /// </summary>
        public void Resize(GridCoord newSize, bool[] mapping = null)
        {
            if (!Initialized || _map == null)
            {
                QuickLog.Warning<GridBoard>(
                    "Board '{0}': Resize ignored — board is not initialized.",
                    configuration != null ? configuration.Id : name
                );
                return;
            }

            GridCoord appliedSize = _map.Resize(newSize, mapping);

            QuickLog.Info<GridBoard>(
                "Board '{0}' resized to {1}x{2} (requested {3}x{4}).",
                configuration.Id, 
                appliedSize.x, appliedSize.y,
                newSize.x, newSize.y
            );
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
            // Providers without press assistance retain their original behavior
            // when a held pointer first enters from outside the board.
            if (!_isPointerDown && cell == null && _pointer is not IGridPointerSelectionProvider)
                return;
            if (!_isPointerDown)
            {
                if (_pointer is IGridPointerSelectionProvider selectionProvider)
                {
                    cell = selectionProvider.ResolvePressCell(this, point, cell);
                }
                // Latch even a miss, so holding an empty press cannot acquire a
                // different block later. Assistance runs exactly once per press.
                _isPointerDown = true;
                _downCell = cell;
                if (cell != null) MouseDownOnCell?.Invoke(cell);
            }
            else if (cell != null)
            {
                MouseDragOnCell?.Invoke(cell);
            }

            // Keep a held drag alive outside the board, using the real pointer.
            _drifter.UpdateDrifting(planePosition);
            _snappingAdapter.UpdateSnapping();
        }

        public bool PlaceObjectAtPosition(GridCoord position, IGridOccupant occupant)
            => _map.PlaceObjectAtPosition(position, occupant);

        public bool CheckObjectPlaceable(GridCoord position, IGridOccupant occupant)
            => _map.CheckObjectPlaceable(position, occupant);

        public bool MoveOccupants(
            IEnumerable<IGridOccupant> occupants,
            GridCell from,
            GridCell to
        )
            => _map.MoveOccupants(occupants, from, to);

        public bool RemoveObject(IGridOccupant occupant)
        {
            if (occupant is IDrifter drifter)
            {
                _drifter?.RemoveDrifter(drifter);
            }

            return _map.RemoveObject(occupant);
        }
        public GridCell AccessCell(GridCoord position) => _map.AccessCell(position);
        public GridCell AccessCell(int x, int y) => _map.AccessCell(new GridCoord(x, y));
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

        private void InitializeCore()
        {
            ResolveProviders();
            CreateModules();
            _map.CreateAllCells();
            FinalizeInitialization();
        }

        private IEnumerator InitializeCoreCoroutine(int cellsPerChunk)
        {
            ResolveProviders();
            CreateModules();

            IEnumerator chunked = _map.CreateAllCellsChunked(cellsPerChunk);
            while (chunked.MoveNext())
            {
                yield return chunked.Current;
            }

            FinalizeInitialization();
        }

        private void CreateModules()
        {
            _cellFactory?.BindParent(ResolveCellRoot());

            _map = new GridMap(configuration);
            _drifter = new GridDrifter(this, _map, configuration, _feedback);
            _snappingAdapter = new GridSnappingAdapter(_map, _drifter);

            _snappingAdapter.SnappedCellChanged += HandleSnappedCellChanged;
            _drifter.DrifterAppended += HandleDrifterAppended;
            _drifter.DriftingFinished += HandleDriftingFinished;
            MouseDownOnCell += HandleMouseDownOnCell;

            _map.AllCellsCreated += HandleAllCellsCreated;
            _map.AllCellsCleared += HandleAllCellsCleared;
        }

        private void UnsubscribeModules()
        {
            if (_snappingAdapter != null)
            {
                _snappingAdapter.SnappedCellChanged -= HandleSnappedCellChanged;
            }

            if (_drifter != null)
            {
                _drifter.DrifterAppended -= HandleDrifterAppended;
                _drifter.DriftingFinished -= HandleDriftingFinished;
            }

            MouseDownOnCell -= HandleMouseDownOnCell;

            if (_map != null)
            {
                _map.AllCellsCreated -= HandleAllCellsCreated;
                _map.AllCellsCleared -= HandleAllCellsCleared;
            }
        }

        private void FinalizeInitialization()
        {
            _map.EnableRegion(configuration.GridSize);
            RefreshBorder();
            Initialized = true;
            QuickLog.Info<GridBoard>(
                "Board '{0}' initialized. Grid {1}x{2}.",
                configuration.Id,
                _map.GridSize.x,
                _map.GridSize.y
            );
        }

        private void ShutdownIfInitialized()
        {
            if (Initialized) Shutdown();
        }

        /// <summary>
        /// Resolves the parent transform for spawned cell views: explicit
        /// <see cref="cellRoot"/>, otherwise an auto-created child container,
        /// otherwise the board's own transform (never the scene root).
        /// </summary>
        private Transform ResolveCellRoot()
        {
            if (cellRoot != null) return cellRoot;
            if (!autoCreateCellContainer) return transform;

            Transform container = transform.Find(cellContainerName);
            if (container == null)
            {
                GameObject go = new GameObject(cellContainerName);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                container = go.transform;
            }

            return container;
        }

        private CancellationTokenSource GetInitCts()
        {
            if (_initCts == null)
            {
                _initCts = new CancellationTokenSource();
            }

            return _initCts;
        }

        private void HandleSnappedCellChanged() => SnappedCellChanged?.Invoke();
        private void HandleDrifterAppended(IDrifter drifter) => DrifterAppended?.Invoke(drifter);
        private void HandleDriftingFinished() => DriftingFinished?.Invoke();
        private void HandleMouseDownOnCell(GridCell cell) => _drifter?.HandleMouseDownOnCell(cell);
        private void HandleAllCellsCreated() => AllCellsCreated?.Invoke();
        private void HandleAllCellsCleared() => AllCellsCleared?.Invoke();

        private void RefreshBorder() => _borderGenerator?.Combine(_map.BuildBorderData(), _map.GridSize);

        private void ReleasePointer()
        {
            if (!_isPointerDown) return;
            _isPointerDown = false;
            if (_downCell != null) MouseUpOnCell?.Invoke(_downCell);
            _downCell = null;
            _drifter.ReleaseAllDrifters();
        }

        #endregion
    }
}
