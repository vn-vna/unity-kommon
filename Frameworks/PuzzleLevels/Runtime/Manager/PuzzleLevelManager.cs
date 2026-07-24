using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    [AsyncResourceManagerHost("Project/Tools/Puzzle Levels")]
    public class PuzzleLevelManager :
        AsyncResourceManagerBase<PuzzleLevelManager, TextAsset>
    {
        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Maximum number of preloaded levels kept in the LRU cache.")]
#endif
        [SerializeField]
        [Min(1)]
        private int _maxCachedLevels = 4;

#if UNITY_EDITOR
        [Tooltip("Enable detailed debug logging for preload and cache operations.")]
#endif
        [SerializeField]
        private bool _verboseLogging;

#if UNITY_EDITOR
        [Tooltip("Override configuration applied at startup.")]
#endif
        [SerializeField]
        private PuzzleLevelOverrideConfig _overrideConfig;

        #endregion

        #region Private Fields

        private PuzzleLevelOverrideRegistry _overrideRegistry;

        private readonly Dictionary<string, IPuzzleLevelData> _cache
            = new Dictionary<string, IPuzzleLevelData>();
        private readonly LinkedList<string> _lruOrder
            = new LinkedList<string>();
        private readonly Dictionary<string, ResourceLoadingHandler<IPuzzleLevelData>> _pendingLoads
            = new Dictionary<string, ResourceLoadingHandler<IPuzzleLevelData>>();

        #endregion

        #region Properties

        internal PuzzleLevelOverrideConfig OverrideConfig
            => _overrideConfig;

        public IReadOnlyCollection<string> CachedLevelIds
            => _lruOrder;

        #endregion

        #region Runtime Initialization

        private const string OverrideConfigResourcePath
            = "PuzzleLevelOverrideConfig";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntimeObjects()
        {
            PuzzleLevelManager manager = Instance;
            if (manager == null)
            {
                QuickLog.Warning<PuzzleLevelManager>(
                    "PuzzleLevelManager instance not found in Resources. "
                    + "Override registry and preloader will not be initialized.");
                return;
            }

            PuzzleLevelOverrideRegistry registry
                = FindAnyObjectByType<PuzzleLevelOverrideRegistry>();
            if (registry != null) return;

            GameObject go = new GameObject("[PuzzleLevelOverrideRegistry]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            registry = go.AddComponent<PuzzleLevelOverrideRegistry>();
            LoadPreconfiguredOverrides(registry, manager);
        }

        private static void LoadPreconfiguredOverrides(
            PuzzleLevelOverrideRegistry registry,
            PuzzleLevelManager manager
        )
        {
            PuzzleLevelOverrideConfig config = manager?._overrideConfig
                ?? Resources.Load<PuzzleLevelOverrideConfig>(OverrideConfigResourcePath);

            if (config == null)
            {
                LogVerbose(
                    manager,
                    "No override config found. Proceeding without overrides.");
                return;
            }

            foreach (PuzzleLevelOverrideEntry entry in config.Entries)
            {
                if (
                    string.IsNullOrEmpty(entry.LevelId) ||
                    !entry.OverrideAsset
                ) continue;

                PuzzleLevelData data = new PuzzleLevelData(
                    entry.LevelId,
                    entry.OverrideAsset,
                    entry.DataType
                );

                registry.SetOverride(entry.LevelId, data);
            }
        }

        #endregion

        #region Unity Callbacks

        protected override void OnEnable()
        {
            base.OnEnable();
        }

        #endregion

        #region Public Methods

        public ResourceLoadingHandler<TextAsset> LoadResouceAsync(int v)
        {
            return LoadResouceAsync(new PuzzleLevelId()
            {
                ResourceId = v.ToString()
            });
        }

        public void InitializeManager(float timeout = float.MaxValue)
        {
            Initialize(timeout);
        }

        public IEnumerator InitializeManagerCoroutine(
            float timeout = float.MaxValue)
        {
            yield return InitializeCoroutine(timeout);
        }

        public ResourceLoadingHandler<IPuzzleLevelData> GetLevelAsync(
            string levelId)
        {
            ResourceLoadingHandler<IPuzzleLevelData> handler
                = new ResourceLoadingHandler<IPuzzleLevelData>();
            PerformGetLevelCoroutine(levelId, handler).DispatchOnDispatcher();
            return handler;
        }

        public PuzzleLevelOverrideRegistry GetOverrideRegistry()
        {
            if (_overrideRegistry != null) return _overrideRegistry;
            _overrideRegistry = FindAnyObjectByType<PuzzleLevelOverrideRegistry>();
            return _overrideRegistry;
        }

        public void PreloadLevel(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return;

            LogVerbose(
                this,
                "Preload requested for level '{0}'.",
                levelId);

            GetLevelAsync(levelId);
        }

        public bool TryGetPreloadedLevel(
            string levelId, out IPuzzleLevelData data)
        {
            if (_cache.TryGetValue(levelId, out data))
            {
                TouchLru(levelId);
                return true;
            }

            data = null;
            return false;
        }

        public void ClearCache()
        {
            _lruOrder.Clear();
            _cache.Clear();

            LogVerbose(
                this,
                "Preloader cache cleared.");
        }

        #endregion

        #region Private Methods — Loading

        private IEnumerator PerformGetLevelCoroutine(
            string levelId,
            ResourceLoadingHandler<IPuzzleLevelData> handler
        )
        {
            handler.LoadingStatus = LoadingStatus.Initiating;
            handler.ResourceStatus = ResourceStatus.Unknown;

            if (string.IsNullOrEmpty(levelId))
            {
                FailHandler(handler, new ArgumentNullException(nameof(levelId)));
                yield break;
            }

            // ── 1. Override check ──────────────────────────────

            PuzzleLevelOverrideRegistry registry = GetOverrideRegistry();
            IPuzzleLevelData overrideLevel
                = registry ? registry.TryGet(levelId) : null;
            if (overrideLevel != null)
            {
                CompleteHandler(handler, overrideLevel, "Override");
                LogVerbose(
                    this,
                    "Override hit for level '{0}'.", levelId);
                yield break;
            }

            // ── 2. Preloader cache check ───────────────────────

            if (TryGetPreloadedLevel(levelId, out IPuzzleLevelData cached))
            {
                LogVerbose(
                    this,
                    "Preloader cache hit for level '{0}'.", levelId);
                CompleteHandler(handler, cached, "Preloader");
                yield break;
            }

            // ── 3. In-flight dedup check ───────────────────────

            if (_pendingLoads.TryGetValue(
                    levelId, out ResourceLoadingHandler<IPuzzleLevelData> pending)
                && pending != handler)
            {
                LogVerbose(
                    this,
                    "In-flight load found for '{0}', waiting...", levelId);

                yield return new WaitUntil(
                    () => pending.LoadingStatus == LoadingStatus.Completed);

                if (
                    pending.ResourceStatus == ResourceStatus.Loaded &&
                    pending.Resouce != null
                )
                {
                    CompleteHandler(
                        handler,
                        pending.Resouce,
                        pending.ProviderSource);
                }
                else
                {
                    FailHandler(
                        handler,
                        pending.Exception
                        ?? new InvalidOperationException(
                            $"In-flight load for '{levelId}' failed."));
                }

                yield break;
            }

            // ── 4. Register in-flight, load, unregister ───────

            _pendingLoads[levelId] = handler;

            yield return LoadViaProviderChain(levelId, handler);

            _pendingLoads.Remove(levelId);

            // ── 5. Cache on success ────────────────────────────

            if (
                handler.ResourceStatus == ResourceStatus.Loaded &&
                handler.Resouce != null
            )
            {
                AddToCache(levelId, handler.Resouce);
            }
        }

        private IEnumerator LoadViaProviderChain(
            string levelId,
            ResourceLoadingHandler<IPuzzleLevelData> handler
        )
        {
            IAsyncResourceId resourceId = new PuzzleLevelId
            {
                ResourceId = levelId
            };
            ResourceLoadingHandler<TextAsset> assetHandler
                = LoadResouceAsync(resourceId);

            WaitUntil waitForLoadCompleted = new WaitUntil(
                () => assetHandler.LoadingStatus == LoadingStatus.Completed);
            yield return waitForLoadCompleted;

            if (
                assetHandler.ResourceStatus != ResourceStatus.Loaded ||
                assetHandler.Resouce == null
            )
            {
                FailHandler(
                    handler,
                    assetHandler.Exception
                    ?? new InvalidOperationException(
                        $"Failed to load level '{levelId}'."));
                yield break;
            }

            DataType dataType = ResolveDataType(levelId);
            PuzzleLevelData levelData = new PuzzleLevelData(
                levelId, assetHandler.Resouce, dataType);

            CompleteHandler(handler, levelData, assetHandler.ProviderSource);
        }

        private DataType ResolveDataType(string levelId)
        {
            foreach (IAsyncResourceProvider<TextAsset> provider in Providers)
            {
                if (
                    provider is ICatalogAwareAsyncResourceProvider catalogProvider &&
                    catalogProvider.HasResource(
                        new PuzzleLevelId { ResourceId = levelId })
                )
                {
                    return catalogProvider.GetDataType(levelId);
                }
            }

            return DataType.Unknown;
        }

        #endregion

        #region Private Methods — Handler Helpers

        private static void CompleteHandler(
            ResourceLoadingHandler<IPuzzleLevelData> handler,
            IPuzzleLevelData data,
            string providerSource
        )
        {
            handler.Resouce = data;
            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Loaded;
            handler.ProviderSource = providerSource;
        }

        private static void FailHandler(
            ResourceLoadingHandler<IPuzzleLevelData> handler,
            Exception exception
        )
        {
            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Failed;
            handler.Exception = exception;
        }

        #endregion

        #region Private Methods — Preloader Cache

        private void AddToCache(string levelId, IPuzzleLevelData data)
        {
            if (_lruOrder.Contains(levelId))
            {
                _lruOrder.Remove(levelId);
            }

            _lruOrder.AddLast(levelId);
            _cache[levelId] = data;

            LogVerbose(
                this,
                "Cached preloaded level '{0}'. Cache size: {1}",
                levelId, _cache.Count);

            while (
                _lruOrder.Count > _maxCachedLevels &&
                _lruOrder.First != null
            )
            {
                string evicted = _lruOrder.First.Value;
                _lruOrder.RemoveFirst();
                _cache.Remove(evicted);

                LogVerbose(
                    this,
                    "LRU evicted preloaded level '{0}'.", evicted);
            }
        }

        private void TouchLru(string levelId)
        {
            if (!_lruOrder.Contains(levelId)) return;
            _lruOrder.Remove(levelId);
            _lruOrder.AddLast(levelId);
        }

        #endregion

        #region Private Methods — Logging

        private static void LogVerbose(
            PuzzleLevelManager self,
            string message,
            params object[] args)
        {
            if (self == null) return;
            if (!self._verboseLogging) return;
            QuickLog.Debug<PuzzleLevelManager>(message, args);
        }

        #endregion

        #region Nested Types

        #endregion
    }
}
