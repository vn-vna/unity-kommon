using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    [Flags]
    public enum PreloadTriggerMode
    {
        None = 0,
        EventDriven = 1,
        Polling = 2,
        Explicit = 4
    }

    public interface IPuzzleLevelOrderProvider
    {
        string GetLevelIdAt(int index);
        int GetLevelCount();
    }

    [AddComponentMenu("Scheherazade/Puzzle Levels/Preloader")]
    public class PuzzleLevelPreloader : MonoBehaviour
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
        private int _lookaheadCount = 2;

#if UNITY_EDITOR
        [Tooltip("Maximum number of preloaded levels kept in memory.")]
#endif
        [SerializeField]
        private int _maxCachedLevels = 4;

#if UNITY_EDITOR
        [Tooltip("Polling interval in seconds (only used in Polling mode).")]
#endif
        [SerializeField]
        private float _pollingInterval = 1f;

        #endregion

        #region Interfaces & Properties

        public PreloadTriggerMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public int LookaheadCount
        {
            get => _lookaheadCount;
            set => _lookaheadCount = Mathf.Max(1, value);
        }

        public int MaxCachedLevels
        {
            get => _maxCachedLevels;
            set => _maxCachedLevels = Mathf.Max(1, value);
        }

        public bool IsPreloading { get; private set; }

        public IReadOnlyCollection<string> CachedLevelIds
            => _lruOrder;

        #endregion

        #region Private Fields

        private PuzzleLevelManager _manager;
        private IPuzzleLevelOrderProvider _orderProvider;
        private int _currentLevelIndex = -1;

        private readonly LinkedList<string> _lruOrder = new LinkedList<string>();
        private readonly Dictionary<string, IPuzzleLevelData> _cache
            = new Dictionary<string, IPuzzleLevelData>();

        private float _pollingTimer;
        private readonly Queue<PreloadRequest> _preloadQueue
            = new Queue<PreloadRequest>();
        private Coroutine _currentPreloadCoroutine;

        #endregion

        #region Structs

        private struct PreloadRequest
        {
            public string LevelId;
        }

        #endregion

        #region Unity Callbacks

        private void Awake()
        {
            _manager = PuzzleLevelManager.Instance;
            if (_manager == null)
            {
                QuickLog.Warning<PuzzleLevelPreloader>(
                    "PuzzleLevelManager instance not found. Preloader disabled.");
            }
        }

        private void Update()
        {
            if (!HasPollingMode())
            {
                return;
            }

            _pollingTimer += Time.deltaTime;
            if (_pollingTimer < _pollingInterval)
            {
                return;
            }

            _pollingTimer = 0f;
            TriggerPreload();
        }

        private void OnDestroy()
        {
            _cache.Clear();
            _lruOrder.Clear();
            _preloadQueue.Clear();
        }

        #endregion

        #region Public Methods

        public void ApplyConfig(PuzzleLevelPreloaderConfig config)
        {
            if (config == null)
            {
                return;
            }

            _mode = config.Mode;
            _lookaheadCount = Mathf.Max(1, config.LookaheadCount);
            _maxCachedLevels = Mathf.Max(1, config.MaxCachedLevels);
            _pollingInterval = Mathf.Max(0.1f, config.PollingInterval);
        }

        public void SetOrderProvider(IPuzzleLevelOrderProvider orderProvider)
        {
            _orderProvider = orderProvider;
        }

        public void OnLevelChanged(int levelIndex)
        {
            _currentLevelIndex = levelIndex;

            if ((_mode & PreloadTriggerMode.EventDriven) != 0)
            {
                TriggerPreload();
            }
        }

        public void PreloadLevel(string levelId)
        {
            if ((_mode & PreloadTriggerMode.Explicit) == 0)
            {
                return;
            }

            EnqueuePreload(levelId);
        }

        public bool TryGetPreloadedLevel(string levelId,
            out IPuzzleLevelData levelData)
        {
            if (_cache.TryGetValue(levelId, out levelData))
            {
                MoveToFront(levelId);
                return true;
            }

            levelData = null;
            return false;
        }

        public void Clear()
        {
            _lruOrder.Clear();
            _cache.Clear();
            _preloadQueue.Clear();
        }

        #endregion

        #region Private Methods

        private bool HasPollingMode()
        {
            return (_mode & PreloadTriggerMode.Polling) != 0;
        }

        private void TriggerPreload()
        {
            if (_manager == null || _orderProvider == null)
            {
                return;
            }

            if (_currentLevelIndex < 0)
            {
                return;
            }

            EnqueueUpcomingLevels();
            ProcessQueue();
        }

        private void EnqueueUpcomingLevels()
        {
            int stopIndex = _currentLevelIndex + _lookaheadCount;

            for (int i = _currentLevelIndex + 1; i <= stopIndex; i++)
            {
                if (i >= _orderProvider.GetLevelCount())
                {
                    break;
                }

                string levelId = _orderProvider.GetLevelIdAt(i);
                if (string.IsNullOrEmpty(levelId))
                {
                    continue;
                }

                if (_cache.ContainsKey(levelId)
                    || _lruOrder.Contains(levelId))
                {
                    continue;
                }

                EnqueuePreload(levelId);
            }
        }

        private void EnqueuePreload(string levelId)
        {
            _preloadQueue.Enqueue(new PreloadRequest
            {
                LevelId = levelId
            });

            QuickLog.Debug<PuzzleLevelPreloader>(
                "Enqueued preload for level '{0}'. Queue: {1}",
                levelId, _preloadQueue.Count
            );
        }

        private void ProcessQueue()
        {
            if (_currentPreloadCoroutine != null)
            {
                return;
            }

            if (_preloadQueue.Count == 0)
            {
                return;
            }

            _currentPreloadCoroutine = StartCoroutine(
                ProcessQueueCoroutine());
        }

        private System.Collections.IEnumerator ProcessQueueCoroutine()
        {
            IsPreloading = true;

            while (_preloadQueue.Count > 0)
            {
                PreloadRequest request = _preloadQueue.Dequeue();

                ResourceLoadingHandler<IPuzzleLevelData> handler
                    = _manager.GetLevelAsync(request.LevelId);

                yield return new WaitUntil(
                    () => handler.LoadingStatus == LoadingStatus.Completed);

                if (handler.ResourceStatus == ResourceStatus.Loaded
                    && handler.Resouce != null)
                {
                    AddToCache(request.LevelId, handler.Resouce);
                }
                else
                {
                    QuickLog.Warning<PuzzleLevelPreloader>(
                        "Failed to preload level '{0}'.", request.LevelId);
                }
            }

            _currentPreloadCoroutine = null;
            IsPreloading = false;
        }

        private void AddToCache(string levelId, IPuzzleLevelData data)
        {
            if (_lruOrder.Contains(levelId))
            {
                _lruOrder.Remove(levelId);
            }

            _lruOrder.AddLast(levelId);
            _cache[levelId] = data;

            while (_lruOrder.Count > _maxCachedLevels && _lruOrder.First != null)
            {
                string evictedId = _lruOrder.First.Value;
                _lruOrder.RemoveFirst();
                _cache.Remove(evictedId);

                QuickLog.Debug<PuzzleLevelPreloader>(
                    "LRU evicted preloaded level '{0}'.", evictedId);
            }
        }

        private void MoveToFront(string levelId)
        {
            if (!_lruOrder.Contains(levelId))
            {
                return;
            }

            _lruOrder.Remove(levelId);
            _lruOrder.AddLast(levelId);
        }

        #endregion
    }
}
