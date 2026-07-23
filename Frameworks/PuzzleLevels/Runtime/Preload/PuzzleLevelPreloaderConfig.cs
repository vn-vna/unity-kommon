using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    [CreateAssetMenu(
        fileName = "PuzzleLevelPreloaderConfig",
        menuName = "Scheherazade/Puzzle Levels/Preloader Config"
    )]
    public class PuzzleLevelPreloaderConfig : ScriptableObject
    {
        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("How the preloader is triggered.")]
#endif
        [SerializeField]
        private PreloadTriggerMode _mode = PreloadTriggerMode.EventDriven;

#if UNITY_EDITOR
        [Tooltip("Number of upcoming levels to preload.")]
#endif
        [SerializeField]
        [Min(1)]
        private int _lookaheadCount = 2;

#if UNITY_EDITOR
        [Tooltip("Maximum number of preloaded levels kept in memory.")]
#endif
        [SerializeField]
        [Min(1)]
        private int _maxCachedLevels = 4;

#if UNITY_EDITOR
        [Tooltip("Polling interval in seconds (only used in Polling mode).")]
#endif
        [SerializeField]
        [Min(0.1f)]
        private float _pollingInterval = 1f;

#if UNITY_EDITOR
        [Tooltip("Max levels to fetch per preload burst.")]
#endif
        [SerializeField]
        [Min(1)]
        private int _burstLoadLimit = 2;

#if UNITY_EDITOR
        [Tooltip("Delay in seconds before retrying a failed preload.")]
#endif
        [SerializeField]
        [Min(0.1f)]
        private float _retryDelay = 2f;

#if UNITY_EDITOR
        [Tooltip("Maximum concurrent fetch operations.")]
#endif
        [SerializeField]
        [Min(1)]
        private int _maxConcurrentFetch = 1;

        #endregion

        #region Properties

        public PreloadTriggerMode Mode => _mode;
        public int LookaheadCount => _lookaheadCount;
        public int MaxCachedLevels => _maxCachedLevels;
        public float PollingInterval => _pollingInterval;
        public int BurstLoadLimit => _burstLoadLimit;
        public float RetryDelay => _retryDelay;
        public int MaxConcurrentFetch => _maxConcurrentFetch;

        #endregion
    }
}
