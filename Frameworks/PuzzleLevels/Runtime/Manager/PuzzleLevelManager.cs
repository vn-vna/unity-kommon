using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels.Providers;
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
        [Tooltip(
            "When false, the manager refuses all level loads "
            + "(remote level loading is disabled).")]
#endif
        [SerializeField]
        private bool _allowLoadLevels = true;

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

#if UNITY_EDITOR
        [Tooltip(
            "Custom tags for interpolating provider format strings. "
            + "Use {key} placeholders in provider path/url/key formats.")]
#endif
        [SerializeField]
        private CustomTagEntry[] _customTags = new CustomTagEntry[0];

        #endregion

        #region Private Fields

        private PuzzleLevelOverrideRegistry _overrideRegistry;
        private PuzzleLevelOverrideRegistry _configuredOverrideRegistry;

        private readonly Dictionary<string, CachedLevel> _cache
            = new Dictionary<string, CachedLevel>();
        private readonly LinkedList<string> _lruOrder
            = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> _lruNodes
            = new Dictionary<string, LinkedListNode<string>>();
        private readonly Dictionary<string, PendingLevelLoad> _pendingLoads
            = new Dictionary<string, PendingLevelLoad>();

        private Dictionary<string, string> _customTagLookup
            = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, string> _customTagsView;
        private int _requestGeneration;

        #endregion

        #region Properties

        internal PuzzleLevelOverrideConfig OverrideConfig
            => _overrideConfig;

        public IReadOnlyCollection<string> CachedLevelIds
            => CreateCachedLevelIdSnapshot();

        public IReadOnlyDictionary<string, string> CustomTags
            => _customTagsView ??= new ReadOnlyDictionary<string, string>(
                _customTagLookup);

        public bool AllowLoadLevels
        {
            get => _allowLoadLevels;
            set => _allowLoadLevels = value;
        }

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
            if (registry == null)
            {
                registry = CreateOverrideRegistryObject();
            }

            manager._overrideRegistry = registry;
            manager.LoadPreconfiguredOverrides(registry);
        }

        private void LoadPreconfiguredOverrides(
            PuzzleLevelOverrideRegistry registry)
        {
            if (registry == null || _configuredOverrideRegistry == registry)
            {
                return;
            }

            PuzzleLevelOverrideConfig config = _overrideConfig
                ?? Resources.Load<PuzzleLevelOverrideConfig>(OverrideConfigResourcePath);

            if (config == null)
            {
                LogVerbose(
                    this,
                    "No override config found. Proceeding without overrides.");
                _configuredOverrideRegistry = registry;
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

            _configuredOverrideRegistry = registry;
        }

        private static PuzzleLevelOverrideRegistry CreateOverrideRegistryObject()
        {
            GameObject gameObject = new GameObject(
                "[PuzzleLevelOverrideRegistry]");
            DontDestroyOnLoad(gameObject);
            gameObject.hideFlags = HideFlags.HideInHierarchy;
            return gameObject.AddComponent<PuzzleLevelOverrideRegistry>();
        }

        #endregion

        #region Unity Callbacks

        protected override void OnEnable()
        {
            base.OnEnable();
            SyncCustomTags();
            PropagateInterpolationTagsToProviders();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Dictionary<string, string> previousTags
                = new Dictionary<string, string>(_customTagLookup);
            _maxCachedLevels = Mathf.Max(1, _maxCachedLevels);
            SyncCustomTags();
            ValidateSerializedTags();
            if (Application.isPlaying
                && !AreTagsEqual(previousTags, _customTagLookup))
            {
                PropagateInterpolationTagsToProviders();
                HandleRequestIdentityChanged();
            }
        }
#endif

        #endregion

        #region Public Methods

        public ResourceLoadingHandler<TextAsset> LoadResouceAsync(int v)
        {
            return LoadResouceAsync(new PuzzleLevelId()
            {
                ResourceId = v.ToString(),
                CustomTags = _customTagLookup
            });
        }

        public void SetCustomTag(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string normalizedValue = value ?? string.Empty;
            if (_customTagLookup.TryGetValue(key, out string currentValue)
                && string.Equals(
                    currentValue,
                    normalizedValue,
                    StringComparison.Ordinal))
            {
                return;
            }

            _customTagLookup[key] = normalizedValue;
            PropagateInterpolationTagsToProviders();
            HandleRequestIdentityChanged();
        }

        public void SetCustomTags(
            IReadOnlyDictionary<string, string> tags)
        {
            if (tags == null)
            {
                return;
            }

            bool changed = false;
            foreach (KeyValuePair<string, string> kvp in tags)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key))
                {
                    string value = kvp.Value ?? string.Empty;
                    if (!_customTagLookup.TryGetValue(
                            kvp.Key,
                            out string currentValue)
                        || !string.Equals(
                            currentValue,
                            value,
                            StringComparison.Ordinal))
                    {
                        _customTagLookup[kvp.Key] = value;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                return;
            }

            PropagateInterpolationTagsToProviders();
            HandleRequestIdentityChanged();
        }

        public void RemoveCustomTag(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!_customTagLookup.Remove(key))
            {
                return;
            }

            PropagateInterpolationTagsToProviders();
            HandleRequestIdentityChanged();
        }

        public void ClearCustomTags()
        {
            if (_customTagLookup.Count == 0)
            {
                return;
            }

            _customTagLookup.Clear();
            PropagateInterpolationTagsToProviders();
            HandleRequestIdentityChanged();
        }

        public void InitializeManager(float timeout = 30f)
        {
            Initialize(timeout);
        }

        public IEnumerator InitializeManagerCoroutine(
            float timeout = 30f)
        {
            IEnumerator operation = InitializeCoroutine(timeout);
            while (operation.MoveNext())
            {
                yield return operation.Current;
            }

            if (Status != ResourceManagerStatus.Initialized)
            {
                throw InitializationException
                    ?? new InvalidOperationException(
                        "Puzzle level manager failed to initialize.");
            }
        }

        public ResourceLoadingHandler<IPuzzleLevelData> GetLevelAsync(
            string levelId)
        {
            ResourceLoadingHandler<IPuzzleLevelData> handler
                = new ResourceLoadingHandler<IPuzzleLevelData>();
            LevelRequest request;
            try
            {
                request = CreateRequest(levelId);
            }
            catch (Exception exception)
            {
                FailHandler(handler, exception);
                return handler;
            }

            bool dispatched = Dispatcher.TryDispatchCoroutine(
                PerformGetLevelCoroutine(request, handler),
                out _);
            if (!dispatched)
            {
                FailHandler(
                    handler,
                    new InvalidOperationException(
                        "Puzzle level loading requires a Dispatcher instance."));
            }

            return handler;
        }

        public PuzzleLevelOverrideRegistry GetOverrideRegistry()
        {
            if (_overrideRegistry != null) return _overrideRegistry;
            _overrideRegistry = FindAnyObjectByType<PuzzleLevelOverrideRegistry>();

            if (_overrideRegistry == null && Application.isPlaying)
            {
                _overrideRegistry = CreateOverrideRegistryObject();
            }

            LoadPreconfiguredOverrides(_overrideRegistry);
            return _overrideRegistry;
        }

        public void PreloadLevel(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return;

            if (!_allowLoadLevels)
            {
                LogVerbose(
                    this,
                    "Preload for level '{0}' skipped (AllowLoadLevels = false).",
                    levelId);
                return;
            }

            LogVerbose(
                this,
                "Preload requested for level '{0}'.",
                levelId);

            GetLevelAsync(levelId);
        }

        public bool TryGetPreloadedLevel(
            string levelId, out IPuzzleLevelData data)
        {
            data = null;
            LevelRequest request;
            try
            {
                request = CreateRequest(levelId);
            }
            catch (Exception)
            {
                return false;
            }

            if (_cache.TryGetValue(
                    request.CacheKey,
                    out CachedLevel cachedLevel))
            {
                data = cachedLevel.Data;
                TouchLru(request.CacheKey);
                return true;
            }

            return false;
        }

        public void ClearCache()
        {
            AdvanceGeneration();
            CancelPendingLoads(new OperationCanceledException(
                "Puzzle level cache was cleared."));
            ClearManagerCache();
            ClearProviderCaches();

            LogVerbose(
                this,
                "Preloader cache cleared."
            );
        }

        public void RefreshCatalogs(
            CatalogInvalidationMode mode
            = CatalogInvalidationMode.Aggressive)
        {
            bool dispatched = Dispatcher.TryDispatchCoroutine(
                RefreshCatalogsCoroutine(mode),
                out _);
            if (!dispatched)
            {
                QuickLog.Error<PuzzleLevelManager>(
                    "Catalog refresh requires a Dispatcher instance.");
            }
        }

        public IEnumerator RefreshCatalogsCoroutine(
            CatalogInvalidationMode mode
            = CatalogInvalidationMode.Aggressive)
        {
            string[] cachedIds = CreateCachedLevelIdSnapshot();
            if (mode == CatalogInvalidationMode.Aggressive)
            {
                AdvanceGeneration();
                CancelPendingLoads(new OperationCanceledException(
                    "Puzzle level catalogs were refreshed."));
                ClearManagerCache();
            }

            Exception invalidationException = null;
            yield return RunGuardedCoroutine(
                InvalidateProviderCatalogsCoroutine(mode),
                exception => invalidationException = exception);

            if (mode == CatalogInvalidationMode.Aggressive)
            {
                AdvanceGeneration();
                CancelPendingLoads(
                    new OperationCanceledException(
                        "Puzzle level loads started during catalog refresh were canceled."
                    )
                );
                ClearManagerCache();
            }

            if (invalidationException != null)
            {
                throw invalidationException;
            }

            if (mode != CatalogInvalidationMode.Aggressive)
            {
                yield break;
            }

            foreach (string id in cachedIds)
            {
                ResourceLoadingHandler<IPuzzleLevelData> handler
                    = GetLevelAsync(id);
                while (!handler.IsCompleted)
                {
                    yield return null;
                }

                if (handler.ResourceStatus != ResourceStatus.Loaded)
                {
                    QuickLog.Warning<PuzzleLevelManager>(
                        "Failed to restore cached level '{0}' after refresh: {1}",
                        id,
                        handler.Exception?.Message ?? "Unknown error");
                }
            }
        }

        #endregion

        #region Private Methods — Loading

        private IEnumerator PerformGetLevelCoroutine(
            LevelRequest request,
            ResourceLoadingHandler<IPuzzleLevelData> handler
        )
        {
            handler.LoadingStatus = LoadingStatus.Initiating;
            handler.ResourceStatus = ResourceStatus.Unknown;

            if (handler.IsCancellationRequested)
            {
                handler.Cancel();
                yield break;
            }

            if (!_allowLoadLevels)
            {
                FailHandler(
                    handler,
                    new InvalidOperationException(
                        $"Level load blocked: AllowLoadLevels is false."));
                QuickLog.Warning<PuzzleLevelManager>(
                    "Level '{0}' load blocked (AllowLoadLevels = false).",
                    request.LevelId);
                yield break;
            }

            PuzzleLevelOverrideRegistry registry = GetOverrideRegistry();
            IPuzzleLevelData overrideLevel
                = registry ? registry.TryGet(request.LevelId) : null;
            if (overrideLevel != null)
            {
                CompleteHandler(handler, overrideLevel, "Override");
                LogVerbose(
                    this,
                    "Override hit for level '{0}'.",
                    request.LevelId);
                yield break;
            }

            if (_cache.TryGetValue(
                    request.CacheKey,
                    out CachedLevel cachedLevel))
            {
                TouchLru(request.CacheKey);
                LogVerbose(
                    this,
                    "Preloader cache hit for level '{0}'.",
                    request.LevelId);
                CompleteHandler(handler, cachedLevel.Data, "Preloader");
                yield break;
            }

            if (_pendingLoads.TryGetValue(
                    request.CacheKey,
                    out PendingLevelLoad pending))
            {
                pending.Subscribers.Add(handler);
                LogVerbose(
                    this,
                    "In-flight load found for '{0}', waiting...",
                    request.LevelId);

                yield return WaitForPendingLoad(pending, handler);
                yield break;
            }

            pending = new PendingLevelLoad(request.Generation);
            pending.Subscribers.Add(handler);
            _pendingLoads[request.CacheKey] = pending;

            IEnumerator operation = LoadViaProviderChain(request, pending);
            while (true)
            {
                bool hasNext;
                object current = null;
                try
                {
                    hasNext = operation.MoveNext();
                    if (hasNext)
                    {
                        current = operation.Current;
                    }
                }
                catch (Exception exception)
                {
                    FailHandler(pending.Source, exception);
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return current;
            }

            if (_pendingLoads.TryGetValue(
                    request.CacheKey,
                    out PendingLevelLoad registered)
                && ReferenceEquals(registered, pending))
            {
                _pendingLoads.Remove(request.CacheKey);
            }

            if (pending.Source.ResourceStatus == ResourceStatus.Loaded
                && pending.Source.Resouce != null
                && request.Generation == _requestGeneration)
            {
                AddToCache(
                    request.CacheKey,
                    request.LevelId,
                    pending.Source.Resouce);
            }

            CompleteSubscribers(pending);
        }

        private IEnumerator LoadViaProviderChain(
            LevelRequest request,
            PendingLevelLoad pending
        )
        {
            ResourceLoadingHandler<IPuzzleLevelData> handler = pending.Source;
            ResourceLoadingHandler<TextAsset> assetHandler
                = LoadResouceAsync(request.ResourceId);

            while (!assetHandler.IsCompleted)
            {
                RemoveCanceledSubscribers(pending);
                if (pending.Subscribers.Count == 0)
                {
                    assetHandler.Cancel();
                    handler.Cancel();
                    yield break;
                }

                yield return null;
            }

            if (assetHandler.ResourceStatus == ResourceStatus.Canceled)
            {
                handler.Cancel();
                yield break;
            }

            if (assetHandler.ResourceStatus != ResourceStatus.Loaded
                || assetHandler.Resouce == null)
            {
                FailHandler(
                    handler,
                    assetHandler.Exception
                    ?? new InvalidOperationException(
                        $"Failed to load level '{request.LevelId}'."));
                yield break;
            }

            TextAsset sourceAsset = assetHandler.Resouce;
            PuzzleLevelData levelData;
            try
            {
                DataType dataType = ResolveDataType(
                    request.LevelId,
                    request.ResourceId,
                    assetHandler.Provider);
                levelData = new PuzzleLevelData(
                    request.LevelId,
                    sourceAsset,
                    dataType);
            }
            finally
            {
                if (assetHandler.Provider
                    is IAsyncResourceReleaseProvider<TextAsset> releaseProvider)
                {
                    releaseProvider.ReleaseResource(sourceAsset);
                }
            }

            CompleteHandler(
                handler,
                levelData,
                assetHandler.ProviderSource,
                assetHandler.Provider);
        }

        private static DataType ResolveDataType(
            string levelId,
            IAsyncResourceId resourceId,
            IAsyncResourceProvider provider)
        {
            DataType dataType = provider is IAsyncResourceDataTypeResolver resolver
                ? resolver.GetDataType(resourceId)
                : DataType.Unknown;
            if (dataType == DataType.Unknown
                && provider is ICatalogAwareAsyncResourceProvider catalogProvider)
            {
                dataType = catalogProvider.GetDataType(levelId);
            }

            if (dataType != DataType.Unknown)
            {
                return EnsureSupportedDataType(
                    provider,
                    dataType,
                    levelId);
            }

            return EnsureSupportedDataType(
                provider,
                DataType.Text,
                levelId);
        }

        private static DataType EnsureSupportedDataType(
            IAsyncResourceProvider provider,
            DataType dataType,
            string levelId)
        {
            if (provider is IAsyncResourceDataTypePolicy policy
                && !policy.SupportsDataType(dataType))
            {
                throw new InvalidDataException(
                    $"Provider '{provider.GetType().Name}' cannot load level "
                    + $"'{levelId}' as {dataType}. TextAsset providers only "
                    + "support UTF-8 text payloads.");
            }

            return dataType;
        }

        #endregion

        #region Private Methods — Handler Helpers

        private static void CompleteHandler(
            ResourceLoadingHandler<IPuzzleLevelData> handler,
            IPuzzleLevelData data,
            string providerSource,
            IAsyncResourceProvider provider = null
        )
        {
            if (handler.IsCancellationRequested)
            {
                handler.Cancel();
                return;
            }

            handler.Resouce = data;
            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Loaded;
            handler.ProviderSource = providerSource;
            handler.Provider = provider;
            handler.Progress = 1f;
        }

        private static void FailHandler(
            ResourceLoadingHandler<IPuzzleLevelData> handler,
            Exception exception
        )
        {
            if (exception is OperationCanceledException)
            {
                handler.Cancel();
                return;
            }

            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Failed;
            handler.Exception = exception;
        }

        private static void CopyHandlerResult(
            ResourceLoadingHandler<IPuzzleLevelData> source,
            ResourceLoadingHandler<IPuzzleLevelData> destination)
        {
            if (destination.IsCancellationRequested)
            {
                destination.Cancel();
                return;
            }

            if (source.ResourceStatus == ResourceStatus.Loaded
                && source.Resouce != null)
            {
                CompleteHandler(
                    destination,
                    source.Resouce,
                    source.ProviderSource,
                    source.Provider);
                return;
            }

            if (source.ResourceStatus == ResourceStatus.Canceled)
            {
                destination.Cancel();
                return;
            }

            FailHandler(
                destination,
                source.Exception
                ?? new InvalidOperationException(
                    "Puzzle level load completed without a result."));
        }

        private IEnumerator WaitForPendingLoad(
            PendingLevelLoad pending,
            ResourceLoadingHandler<IPuzzleLevelData> subscriber)
        {
            while (!pending.Source.IsCompleted)
            {
                if (subscriber.IsCancellationRequested)
                {
                    subscriber.Cancel();
                    pending.Subscribers.Remove(subscriber);
                    if (pending.Subscribers.Count == 0)
                    {
                        pending.Source.Cancel();
                    }

                    yield break;
                }

                yield return null;
            }

            pending.Subscribers.Remove(subscriber);
            CopyHandlerResult(pending.Source, subscriber);
        }

        private static void CompleteSubscribers(PendingLevelLoad pending)
        {
            for (int i = 0; i < pending.Subscribers.Count; i++)
            {
                CopyHandlerResult(
                    pending.Source,
                    pending.Subscribers[i]);
            }

            pending.Subscribers.Clear();
        }

        private static void RemoveCanceledSubscribers(PendingLevelLoad pending)
        {
            for (int i = pending.Subscribers.Count - 1; i >= 0; i--)
            {
                ResourceLoadingHandler<IPuzzleLevelData> subscriber
                    = pending.Subscribers[i];
                if (!subscriber.IsCancellationRequested)
                {
                    continue;
                }

                subscriber.Cancel();
                pending.Subscribers.RemoveAt(i);
            }
        }

        #endregion

        #region Private Methods — Preloader Cache

        private void AddToCache(
            string cacheKey,
            string levelId,
            IPuzzleLevelData data)
        {
            if (_lruNodes.TryGetValue(
                    cacheKey,
                    out LinkedListNode<string> existingNode))
            {
                _lruOrder.Remove(existingNode);
            }

            LinkedListNode<string> node = _lruOrder.AddLast(cacheKey);
            _lruNodes[cacheKey] = node;
            _cache[cacheKey] = new CachedLevel(levelId, data);

            LogVerbose(
                this,
                "Cached preloaded level '{0}'. Cache size: {1}",
                levelId, _cache.Count);

            while (
                _lruOrder.Count > Mathf.Max(1, _maxCachedLevels) &&
                _lruOrder.First != null
            )
            {
                string evicted = _lruOrder.First.Value;
                _lruOrder.RemoveFirst();
                _lruNodes.Remove(evicted);
                string evictedLevelId = _cache.TryGetValue(
                    evicted,
                    out CachedLevel cachedLevel)
                        ? cachedLevel.LevelId
                        : evicted;
                _cache.Remove(evicted);

                LogVerbose(
                    this,
                    "LRU evicted preloaded level '{0}'.",
                    evictedLevelId);
            }
        }

        private void TouchLru(string cacheKey)
        {
            if (!_lruNodes.TryGetValue(
                    cacheKey,
                    out LinkedListNode<string> node))
            {
                return;
            }

            _lruOrder.Remove(node);
            _lruOrder.AddLast(node);
        }

        private string[] CreateCachedLevelIdSnapshot()
        {
            if (_lruOrder.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> levelIds = new List<string>(_lruOrder.Count);
            HashSet<string> uniqueIds = new HashSet<string>();
            foreach (string cacheKey in _lruOrder)
            {
                if (_cache.TryGetValue(cacheKey, out CachedLevel cachedLevel)
                    && uniqueIds.Add(cachedLevel.LevelId))
                {
                    levelIds.Add(cachedLevel.LevelId);
                }
            }

            return levelIds.ToArray();
        }

        private void ClearManagerCache()
        {
            _lruOrder.Clear();
            _lruNodes.Clear();
            _cache.Clear();
        }

        private void ClearProviderCaches()
        {
            foreach (IAsyncResourceProvider<TextAsset> provider in Providers)
            {
                if (provider is IAsyncResourceCache cache)
                {
                    cache.ClearCache();
                }
            }
        }

        private void CancelPendingLoads(Exception exception)
        {
            foreach (PendingLevelLoad pending in _pendingLoads.Values)
            {
                pending.Source.Cancel();
                for (int i = 0; i < pending.Subscribers.Count; i++)
                {
                    ResourceLoadingHandler<IPuzzleLevelData> subscriber
                        = pending.Subscribers[i];
                    if (subscriber.IsCancellationRequested)
                    {
                        subscriber.Cancel();
                        continue;
                    }

                    subscriber.LoadingStatus = LoadingStatus.Completed;
                    subscriber.ResourceStatus = ResourceStatus.Canceled;
                    subscriber.Exception = exception;
                }

                pending.Subscribers.Clear();
            }

            _pendingLoads.Clear();
        }

        protected override void HandleResetting()
        {
            AdvanceGeneration();
            CancelPendingLoads(new OperationCanceledException(
                "Puzzle level manager was reset."));
            ClearManagerCache();
        }

        private LevelRequest CreateRequest(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId))
            {
                throw new ArgumentException(
                    "Puzzle level ID cannot be null, empty, or whitespace.",
                    nameof(levelId));
            }

            Dictionary<string, string> tagSnapshot
                = new Dictionary<string, string>(_customTagLookup.Count);
            foreach (KeyValuePair<string, string> tag in _customTagLookup)
            {
                if (!string.IsNullOrWhiteSpace(tag.Key))
                {
                    tagSnapshot[tag.Key] = tag.Value ?? string.Empty;
                }
            }

            PuzzleLevelId resourceId = new PuzzleLevelId
            {
                ResourceId = levelId,
                CustomTags = tagSnapshot
            };

            return new LevelRequest(
                levelId,
                resourceId,
                BuildRequestKey(levelId, tagSnapshot, _requestGeneration),
                _requestGeneration);
        }

        private static string BuildRequestKey(
            string levelId,
            IReadOnlyDictionary<string, string> tags,
            int generation)
        {
            StringBuilder builder = new StringBuilder(levelId.Length + 32);
            builder.Append(generation).Append('|').Append(levelId);

            if (tags.Count == 0)
            {
                return builder.ToString();
            }

            List<string> keys = new List<string>(tags.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                string value = tags[key] ?? string.Empty;
                builder.Append('|')
                    .Append(key.Length)
                    .Append(':')
                    .Append(key)
                    .Append('=')
                    .Append(value.Length)
                    .Append(':')
                    .Append(value);
            }

            return builder.ToString();
        }

        private void HandleRequestIdentityChanged()
        {
            AdvanceGeneration();
            CancelPendingLoads(new OperationCanceledException(
                "Puzzle level request identity changed."));
            ClearManagerCache();
            ClearProviderCaches();
        }

        private void AdvanceGeneration()
        {
            unchecked
            {
                _requestGeneration++;
            }
        }

        #endregion

        #region Private Methods — Logging

        private void SyncCustomTags()
        {
            if (_customTagLookup == null)
            {
                _customTagLookup = new Dictionary<string, string>();
                _customTagsView = null;
                return;
            }

            _customTagLookup.Clear();

            if (_customTags == null)
            {
                return;
            }

            foreach (CustomTagEntry entry in _customTags)
            {
                if (string.IsNullOrEmpty(entry.key))
                {
                    continue;
                }

                _customTagLookup[entry.key] = entry.value ?? string.Empty;
            }
        }

#if UNITY_EDITOR
        private void ValidateSerializedTags()
        {
            if (_customTags == null)
            {
                return;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _customTags.Length; i++)
            {
                string key = _customTags[i].key;
                if (string.IsNullOrWhiteSpace(key))
                {
                    QuickLog.Warning<PuzzleLevelManager>(
                        "Custom tag entry {0} has an empty key.",
                        i);
                    continue;
                }

                if (!keys.Add(key))
                {
                    QuickLog.Error<PuzzleLevelManager>(
                        "Duplicate custom tag key '{0}' at entry {1}.",
                        key,
                        i);
                }
            }
        }

        private static bool AreTagsEqual(
            IReadOnlyDictionary<string, string> first,
            IReadOnlyDictionary<string, string> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, string> entry in first)
            {
                if (!second.TryGetValue(entry.Key, out string secondValue)
                    || !string.Equals(
                        entry.Value,
                        secondValue,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
#endif

        private void PropagateInterpolationTagsToProviders()
        {
            if (_customTagLookup == null)
            {
                return;
            }

            foreach (IAsyncResourceProvider<TextAsset> provider in Providers)
            {
                if (provider is IAsyncResourceInterpolationTagReceiver receiver)
                {
                    receiver.SetInterpolationTags(_customTagLookup);
                }
            }
        }

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

        private sealed class CachedLevel
        {
            public string LevelId { get; }
            public IPuzzleLevelData Data { get; }

            public CachedLevel(string levelId, IPuzzleLevelData data)
            {
                LevelId = levelId;
                Data = data;
            }
        }

        private sealed class PendingLevelLoad
        {
            public ResourceLoadingHandler<IPuzzleLevelData> Source { get; }
                = new ResourceLoadingHandler<IPuzzleLevelData>();

            public List<ResourceLoadingHandler<IPuzzleLevelData>> Subscribers
                { get; } = new List<ResourceLoadingHandler<IPuzzleLevelData>>();

            public int Generation { get; }

            public PendingLevelLoad(int generation)
            {
                Generation = generation;
            }
        }

        private readonly struct LevelRequest
        {
            public string LevelId { get; }
            public PuzzleLevelId ResourceId { get; }
            public string CacheKey { get; }
            public int Generation { get; }

            public LevelRequest(
                string levelId,
                PuzzleLevelId resourceId,
                string cacheKey,
                int generation)
            {
                LevelId = levelId;
                ResourceId = resourceId;
                CacheKey = cacheKey;
                Generation = generation;
            }
        }

        [System.Serializable]
        internal struct CustomTagEntry
        {
#if UNITY_EDITOR
            [Tooltip("Tag key used in format strings as {key}.")]
#endif
            [SerializeField]
            public string key;

#if UNITY_EDITOR
            [Tooltip("Value that replaces {key} in provider paths/URLs.")]
#endif
            [SerializeField]
            public string value;
        }

        #endregion
    }
}
