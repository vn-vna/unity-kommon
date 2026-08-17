using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Plain per-board configuration (NOT a singleton — one asset per grid).
    /// Providers are scriptable-delivered; assets can be shared across configs.
    /// </summary>
    [CreateAssetMenu(menuName = "Scheherazade/Grid System/Grid Configuration")]
    public class GridConfiguration : ScriptableObject
    {
        #region Properties

        public string Id => _id;
        public bool AutoConfigure => _autoConfigure;
        public GridCoord GridSize => _gridSize;
        public GridCoord PoolSize => _poolSize;
        public GridCoord BorderSize => _borderSize;
        public float DrifterSpeedLimit => _drifterSpeedLimit;
        public float NudgeAmount => _nudgeAmount;

        public IGridCellFactoryProvider CellFactory => _cellFactoryProvider as IGridCellFactoryProvider;
        public IGridCoordinateProvider CoordinateProvider => _coordinateProvider as IGridCoordinateProvider;
        public IGridPointerProvider PointerProvider => _pointerProvider as IGridPointerProvider;
        public IGridFeedbackProvider FeedbackProvider => _feedbackProvider as IGridFeedbackProvider;
        public IGridGameStateProvider GameStateProvider => _gameStateProvider as IGridGameStateProvider;
        public IBorderGenerator BorderGenerator => _borderGenerator as IBorderGenerator;

        #endregion

        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Unique id used for multi-grid lookup via GridManager.GetGrid(id).")]
#endif
        [SerializeField]
        private string _id;

#if UNITY_EDITOR
        [Tooltip("Auto-initialize the board when enabled. Disable to initialize on demand via GridBoard.Initialize / InitializeAsync / InitializeCoroutine.")]
#endif
        [SerializeField]
        private bool _autoConfigure = true;

#if UNITY_EDITOR
        [Tooltip("Active playable region size in cells.")]
#endif
        [SerializeField]
        private GridCoord _gridSize = new GridCoord(15, 15);

#if UNITY_EDITOR
        [Tooltip("Total pooled region size in cells (>= gridSize). Cells beyond the active region stay inactive.")]
#endif
        [SerializeField]
        private GridCoord _poolSize = new GridCoord(15, 15);

#if UNITY_EDITOR
        [Tooltip("Border ring thickness around the pool.")]
#endif
        [SerializeField]
        private GridCoord _borderSize = new GridCoord(5, 5);

#if UNITY_EDITOR
        [Tooltip("Maximum drift speed in world units per tick.")]
#endif
        [SerializeField]
        private float _drifterSpeedLimit = 0.5f;

#if UNITY_EDITOR
        [Tooltip("How far a blocked drifter can nudge toward the blocked direction.")]
#endif
        [SerializeField]
        private float _nudgeAmount = 0.05f;

        [SerializeField]
        private ScriptableObject _cellFactoryProvider;

        [SerializeField]
        private ScriptableObject _coordinateProvider;

        [SerializeField]
        private ScriptableObject _pointerProvider;

        [SerializeField]
        private ScriptableObject _feedbackProvider;

        [SerializeField]
        private ScriptableObject _gameStateProvider;

        [SerializeField]
        private ScriptableObject _borderGenerator;

        #endregion
    }
}
