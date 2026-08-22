using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [ResourceProvider(
        "Cached (LRU)",
        "Wraps another provider with an LRU memory cache and optional disk cache."
    )]
    public class CachedAsyncResourceProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>,
        ICatalogAwareAsyncResourceProvider,
        IInvalidatableCatalog,
        ISelectiveAsyncResourceCache,
        IAsyncResourceCacheKeyProvider,
        IAsyncResourceInterpolationTagReceiver,
        IAsyncResourceDataTypePolicy,
        IAsyncResourceDataTypeResolver,
        IAsyncResourceInitializationStatus,
        IAsyncResourceReleaseProvider<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Lower values are tried first in the provider cascade.")]
#endif
        [SerializeField]
        private int _priority;

#if UNITY_EDITOR
        [Tooltip("Maximum time (seconds) to wait for the wrapped provider to load a resource.")]
#endif
        [SerializeField]
        private float _timeout = 30f;

#if UNITY_EDITOR
        [Tooltip("Maximum number of resources held in the memory cache. When exceeded, the least recently used entry is evicted.")]
#endif
        [SerializeField]
        private int _maxCacheEntries = 64;

#if UNITY_EDITOR
        [Tooltip("Time-to-live in seconds for disk-cached resources. Resources older than this are considered stale.")]
#endif
        [SerializeField]
        private float _cacheTTL = 3600f;

#if UNITY_EDITOR
        [Tooltip("Which base path to use for the disk cache.")]
#endif
        [SerializeField]
        private CacheBasePathType _cacheBasePath = CacheBasePathType.PersistentDataPath;

#if UNITY_EDITOR
        [Tooltip("Subfolder name under the cache base path for storing cached files.")]
#endif
        [SerializeField]
        private string _cacheSubFolder = "CachedResources";

#if UNITY_EDITOR
        [Tooltip("The provider to wrap. Resources not found in cache are delegated to this provider.")]
#endif
        [SerializeField]
        private ScriptableObject _wrappedProviderAsset;

#if UNITY_EDITOR
        [Tooltip("When enabled, loads a catalog JSON file to determine which resources this provider can serve. When merged with the wrapped provider's catalog, resources from either are considered available.")]
#endif
        [SerializeField]
        private CatalogConfig _catalogConfig = new CatalogConfig();

        #endregion

        #region Interfaces & Properties

        public int Priority => _priority;

        public bool IsInitialized { get; private set; }

        public Exception InitializationException { get; private set; }

        public float ResourceLoadingTimeout => _timeout > 0f ? _timeout : 30f;

        public IReadOnlyCollection<string> CatalogedIds
        {
            get
            {
                var ownIds = _catalogData?.CatalogedIds
                    ?? Array.Empty<string>();

                if (_wrappedProvider is ICatalogAwareAsyncResourceProvider cp)
                {
                    var merged = new HashSet<string>(ownIds);
                    merged.UnionWith(cp.CatalogedIds);
                    return merged;
                }

                return ownIds;
            }
        }

        protected IAsyncResourceProvider<ResourceType> WrappedProvider
        {
            get
            {
                if (_wrappedProvider == null)
                {
                    _wrappedProvider = _wrappedProviderAsset as IAsyncResourceProvider<ResourceType>;
                }

                return _wrappedProvider;
            }
        }

        #endregion

        #region Private Fields

        private readonly object _lock = new object();
        private readonly Dictionary<string, LinkedListNode<CacheEntry>> _memoryCache
            = new Dictionary<string, LinkedListNode<CacheEntry>>();
        private readonly LinkedList<CacheEntry> _lruList = new LinkedList<CacheEntry>();
        private readonly Dictionary<string, PendingRequestState> _pendingRequests
            = new Dictionary<string, PendingRequestState>();
        private readonly Dictionary<string, HashSet<string>> _cacheKeysByResourceId
            = new Dictionary<string, HashSet<string>>();
        private readonly List<CacheEntry> _retiredEntries
            = new List<CacheEntry>();
        private readonly Dictionary<ResourceType, int> _callerLeaseCounts
            = new Dictionary<ResourceType, int>(
                ResourceReferenceComparer.Instance);

        private IAsyncResourceProvider<ResourceType> _wrappedProvider;
        private string _diskCacheRoot;
        private CatalogData _catalogData;
        private int _nextRequestGeneration;
        private int _initializationGeneration;

        #endregion

        #region Nested Types

        private sealed class CacheEntry
        {
            public string ResourceId;
            public ResourceType Resource;
            public DateTime CachedAt;
            public bool OwnsResource;
            public IAsyncResourceReleaseProvider<ResourceType> ReleaseProvider;
            public bool IsRetired;
        }

        private sealed class PendingRequestState
        {
            public int Generation;
            public ResourceLoadingHandler<ResourceType> OriginalHandler;
            public readonly List<ResourceLoadingHandler<ResourceType>> Subscribers
                = new List<ResourceLoadingHandler<ResourceType>>();
        }

        private sealed class ResourceReferenceComparer :
            IEqualityComparer<ResourceType>
        {
            public static readonly ResourceReferenceComparer Instance
                = new ResourceReferenceComparer();

            public bool Equals(ResourceType first, ResourceType second)
            {
                return ReferenceEquals(first, second);
            }

            public int GetHashCode(ResourceType resource)
            {
                return resource == null
                    ? 0
                    : RuntimeHelpers.GetHashCode(resource);
            }
        }

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            IsInitialized = false;
            InitializationException = null;
        }

        private void OnDisable()
        {
            List<CacheEntry> entriesToRelease = new List<CacheEntry>();
            lock (_lock)
            {
                _initializationGeneration++;
                RetireAllPendingRequestsLocked(new OperationCanceledException(
                    "Cached provider was disabled."));
                foreach (LinkedListNode<CacheEntry> node
                         in _memoryCache.Values)
                {
                    if (node.Value.Resource != null)
                    {
                        entriesToRelease.Add(node.Value);
                    }
                }

                _memoryCache.Clear();
                _lruList.Clear();
                _cacheKeysByResourceId.Clear();
                entriesToRelease.AddRange(_retiredEntries);
                _retiredEntries.Clear();
                _callerLeaseCounts.Clear();
                IsInitialized = false;
                InitializationException = null;
            }

            ReleaseCacheEntriesImmediately(entriesToRelease);
            _wrappedProvider = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _timeout = Mathf.Max(0.1f, _timeout);
            _maxCacheEntries = Mathf.Max(1, _maxCacheEntries);
            _cacheTTL = Mathf.Max(0f, _cacheTTL);
            if (string.IsNullOrWhiteSpace(_cacheSubFolder))
            {
                _cacheSubFolder = "CachedResources";
            }
        }
#endif

        #endregion

        #region Public Methods

        public void Initialize()
        {
            InitializationException = null;
            int initializationGeneration;
            List<CacheEntry> entriesToRelease = new List<CacheEntry>();
            lock (_lock)
            {
                initializationGeneration = ++_initializationGeneration;
                RetireAllPendingRequestsLocked(new OperationCanceledException(
                    "Cached provider was reinitialized."));
                foreach (LinkedListNode<CacheEntry> node
                         in _memoryCache.Values)
                {
                    if (node.Value.Resource != null)
                    {
                        entriesToRelease.Add(node.Value);
                    }
                }

                _memoryCache.Clear();
                _lruList.Clear();
                IsInitialized = false;
            }

            RetireCacheEntries(entriesToRelease);

            bool dispatched = Dispatcher.TryDispatchCoroutine(
                CoroutineExceptionGuard.Run(
                    InitializeCoroutine(initializationGeneration),
                    exception => HandleInitializationFailure(
                        initializationGeneration,
                        exception)),
                out _);
            if (!dispatched)
            {
                HandleInitializationFailure(
                    initializationGeneration,
                    new InvalidOperationException(
                        "Cached provider initialization requires a Dispatcher."
                    ));
            }
        }

        private IEnumerator InitializeCoroutine(int initializationGeneration)
        {
            IAsyncResourceProvider<ResourceType> wrappedProvider
                = _wrappedProviderAsset as IAsyncResourceProvider<ResourceType>;
            if (wrappedProvider == null || ReferenceEquals(wrappedProvider, this))
            {
                HandleInitializationFailure(
                    initializationGeneration,
                    new InvalidOperationException(
                        "Cached provider requires a different wrapped provider."
                    ));
                yield break;
            }

            try
            {
                wrappedProvider.Initialize();
            }
            catch (Exception exception)
            {
                HandleInitializationFailure(
                    initializationGeneration,
                    new InvalidOperationException(
                        $"Wrapped provider '{wrappedProvider.GetType().Name}' "
                        + "threw during initialization.",
                        exception));
                yield break;
            }

            float initializationStart = Time.realtimeSinceStartup;
            float wrappedInitializationTimeout
                = wrappedProvider.ResourceLoadingTimeout;
            if (wrappedInitializationTimeout <= 0f
                || float.IsNaN(wrappedInitializationTimeout))
            {
                wrappedInitializationTimeout = ResourceLoadingTimeout;
            }

            while (!wrappedProvider.IsInitialized
                && GetWrappedInitializationException(wrappedProvider) == null
                && Time.realtimeSinceStartup - initializationStart
                < wrappedInitializationTimeout)
            {
                yield return null;
            }

            if (!IsCurrentInitialization(initializationGeneration))
            {
                yield break;
            }

            if (!wrappedProvider.IsInitialized)
            {
                HandleInitializationFailure(
                    initializationGeneration,
                    new InvalidOperationException(
                        $"Wrapped provider '{wrappedProvider.GetType().Name}' "
                        + "failed to initialize.",
                        GetWrappedInitializationException(wrappedProvider)));
                yield break;
            }

            CatalogData catalogData = new CatalogData();
            if (_catalogConfig.UseCatalog
                && !string.IsNullOrEmpty(_catalogConfig.CatalogFileName))
            {
                yield return catalogData.LoadFromStreamingAssetsCoroutine(
                    _catalogConfig.CatalogFileName);
                catalogData.ThrowIfFailed(
                    "Cached provider catalog initialization failed.");
            }

            if (!IsCurrentInitialization(initializationGeneration))
            {
                yield break;
            }

            try
            {
                _diskCacheRoot = ResolveDiskCacheRoot();

                if (!Directory.Exists(_diskCacheRoot))
                {
                    Directory.CreateDirectory(_diskCacheRoot);
                }
            }
            catch (Exception exception)
            {
                HandleInitializationFailure(
                    initializationGeneration,
                    new InvalidOperationException(
                        "Failed to initialize disk cache.",
                        exception));
                yield break;
            }

            _wrappedProvider = wrappedProvider;
            _catalogData = catalogData;
            IsInitialized = true;

            QuickLog.Info<CachedAsyncResourceProvider<ResourceType>>(
                "Cached provider initialized. Memory limit: {0}, Disk TTL: {1}s, Wrapped: {2}",
                _maxCacheEntries,
                _cacheTTL,
                _wrappedProvider?.GetType().Name ?? "<none>"
            );
        }

        public bool HasResource(IAsyncResourceId resourceId)
        {
            if (!IsInitialized || resourceId == null)
            {
                return false;
            }

            string cacheKey = GetCacheKey(resourceId);
            lock (_lock)
            {
                if (_memoryCache.ContainsKey(cacheKey))
                {
                    return true;
                }
            }

            if (SupportsDiskCache(resourceId)
                && DiskCacheHasResource(cacheKey))
            {
                return true;
            }

            if (_catalogData != null && _catalogData.IsLoaded)
            {
                if (_catalogData.HasResource(resourceId.ResourceId))
                {
                    return true;
                }

                if (_wrappedProvider is ICatalogAwareAsyncResourceProvider cp)
                {
                    return cp.HasResource(resourceId);
                }

                return false;
            }

            if (_wrappedProvider is ICatalogAwareAsyncResourceProvider catalogProvider)
            {
                return catalogProvider.HasResource(resourceId);
            }

            return true;
        }

        public DataType GetDataType(string resourceId)
        {
            if (_catalogData != null && _catalogData.IsLoaded)
            {
                DataType ownType = _catalogData.GetDataType(resourceId);
                if (ownType != DataType.Unknown)
                {
                    return ownType;
                }
            }

            if (_wrappedProvider is ICatalogAwareAsyncResourceProvider cp)
            {
                return cp.GetDataType(resourceId);
            }

            return DataType.Unknown;
        }

        public DataType GetDataType(IAsyncResourceId resourceId)
        {
            DataType ownType = _catalogData?.GetDataType(
                resourceId?.ResourceId) ?? DataType.Unknown;
            if (ownType != DataType.Unknown)
            {
                return ownType;
            }

            if (WrappedProvider is IAsyncResourceDataTypeResolver resolver)
            {
                return resolver.GetDataType(resourceId);
            }

            return GetDataType(resourceId?.ResourceId);
        }

        public void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            if (handler.IsCancellationRequested)
            {
                handler.Cancel();
                return;
            }

            if (!IsInitialized)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new InvalidOperationException(
                    "Cached provider is not initialized.");
                return;
            }

            if (id == null || string.IsNullOrEmpty(id.ResourceId))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new ArgumentNullException(nameof(id));
                return;
            }

            string resourceId = GetCacheKey(id);
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                resourceId = id.ResourceId;
            }

            RegisterCacheKey(id.ResourceId, resourceId);

            int requestGeneration;
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(resourceId, out LinkedListNode<CacheEntry> node))
                {
                    if (node.Value.Resource == null)
                    {
                        _lruList.Remove(node);
                        _memoryCache.Remove(resourceId);
                    }
                    else
                    {
                        MoveToFront(node);
                        RetainCallerLeaseLocked(node.Value.Resource);

                        handler.Resouce = node.Value.Resource;
                        handler.LoadingStatus = LoadingStatus.Completed;
                        handler.ResourceStatus = ResourceStatus.Loaded;
                        handler.ProviderSource = GetType().Name;

                        QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                            "Memory cache hit for '{0}'.", resourceId
                        );
                        return;
                    }
                }

                if (_pendingRequests.TryGetValue(
                        resourceId,
                        out PendingRequestState pendingRequest))
                {
                    handler.LoadingStatus = LoadingStatus.Loading;
                    handler.ResourceStatus = ResourceStatus.Unknown;
                    handler.ProviderSource = GetType().Name;
                    pendingRequest.Subscribers.Add(handler);
                    QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                        "Request for '{0}' already in-flight. Added to pending list ({1} total).",
                        resourceId, pendingRequest.Subscribers.Count
                    );
                    return;
                }

                requestGeneration = NextRequestGenerationLocked();
                _pendingRequests[resourceId] = new PendingRequestState
                {
                    Generation = requestGeneration,
                    OriginalHandler = handler
                };
            }

            if (TryLoadFromDisk(
                    id,
                    resourceId,
                    out ResourceType diskResource))
            {
                AddToMemoryCache(resourceId, diskResource, true, null);
                CompletePendingRequests(
                    resourceId,
                    diskResource,
                    null,
                    requestGeneration);

                RetainCallerLease(resourceId, diskResource);
                handler.Resouce = diskResource;
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Loaded;
                handler.ProviderSource = GetType().Name;

                QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                    "Disk cache hit for '{0}'.", resourceId
                );
                return;
            }

            IAsyncResourceProvider<ResourceType> wrappedProvider
                = _wrappedProvider;
            if (wrappedProvider == null)
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "No wrapped provider configured. Cannot load '{0}'.", resourceId
                );
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new InvalidOperationException(
                    "No wrapped provider configured."
                );
                RemovePendingRequest(resourceId, requestGeneration);
                return;
            }

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;

            bool dispatched = Dispatcher.TryDispatchCoroutine(
                CoroutineExceptionGuard.Run(
                    LoadFromProviderCoroutine(
                        id,
                        handler,
                        resourceId,
                        requestGeneration,
                        wrappedProvider),
                    exception => HandleLoadCoroutineFailure(
                        handler,
                        resourceId,
                        requestGeneration,
                        exception)),
                out _);
            if (!dispatched)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new InvalidOperationException(
                    "Cached provider loading requires a Dispatcher.");

                lock (_lock)
                {
                    RemovePendingRequestLocked(
                        resourceId,
                        requestGeneration);
                }
            }
        }

        public string GetCacheKey(IAsyncResourceId resourceId)
        {
            string cacheKey;
            if (WrappedProvider is IAsyncResourceCacheKeyProvider keyProvider)
            {
                cacheKey = keyProvider.GetCacheKey(resourceId);
            }
            else
            {
                cacheKey = resourceId?.ResourceId;
            }

            string contentHash = _catalogData?.GetContentHash(
                resourceId?.ResourceId);
            return string.IsNullOrWhiteSpace(contentHash)
                ? cacheKey
                : $"{cacheKey ?? string.Empty}\n{contentHash}";
        }

        public void SetInterpolationTags(
            IReadOnlyDictionary<string, string> tags)
        {
            if (WrappedProvider
                is IAsyncResourceInterpolationTagReceiver receiver)
            {
                receiver.SetInterpolationTags(tags);
            }
        }

        public bool SupportsDataType(DataType dataType)
        {
            return WrappedProvider is not IAsyncResourceDataTypePolicy policy
                || policy.SupportsDataType(dataType);
        }

        public void InvalidateCatalog(CatalogInvalidationMode mode)
        {
            bool dispatched = Dispatcher.TryDispatchCoroutine(
                CoroutineExceptionGuard.Run(
                    InvalidateCatalogCoroutine(mode),
                    HandleCatalogInvalidationFailure),
                out _);
            if (!dispatched)
            {
                QuickLog.Error<CachedAsyncResourceProvider<ResourceType>>(
                    "Catalog invalidation requires a Dispatcher instance.");
            }
        }

        public IEnumerator InvalidateCatalogCoroutine(
            CatalogInvalidationMode mode)
        {
            if (mode == CatalogInvalidationMode.Aggressive)
            {
                ClearCache();
            }

            _catalogData ??= new CatalogData();
            _catalogData.Reset();

            if (_catalogConfig.UseCatalog
                && !string.IsNullOrEmpty(_catalogConfig.CatalogFileName))
            {
                yield return _catalogData.LoadFromStreamingAssetsCoroutine(
                    _catalogConfig.CatalogFileName);
                _catalogData.ThrowIfFailed(
                    "Cached provider catalog refresh failed.");
            }

            if (WrappedProvider is IInvalidatableCatalog invalidatable)
            {
                yield return invalidatable.InvalidateCatalogCoroutine(mode);
            }
        }

        public void ClearCache()
        {
            List<CacheEntry> entriesToRelease = new List<CacheEntry>();
            lock (_lock)
            {
                RetireAllPendingRequestsLocked(new OperationCanceledException(
                    "Cached provider requests were canceled because the cache "
                    + "was cleared."));
                foreach (LinkedListNode<CacheEntry> node
                         in _memoryCache.Values)
                {
                    if (node.Value.Resource != null)
                    {
                        entriesToRelease.Add(node.Value);
                    }
                }

                _memoryCache.Clear();
                _lruList.Clear();
                _cacheKeysByResourceId.Clear();
            }

            RetireCacheEntries(entriesToRelease);
            ClearDiskCache();

            if (WrappedProvider is IAsyncResourceCache wrappedCache)
            {
                wrappedCache.ClearCache();
            }

            QuickLog.Info<CachedAsyncResourceProvider<ResourceType>>(
                "Cache cleared (memory + disk)."
            );
        }

        public void ClearCache(string resourceId)
        {
            string[] cacheKeys;
            List<CacheEntry> entriesToRelease = new List<CacheEntry>();
            lock (_lock)
            {
                if (!_cacheKeysByResourceId.TryGetValue(
                        resourceId,
                        out HashSet<string> registeredKeys))
                {
                    cacheKeys = Array.Empty<string>();
                }
                else
                {
                    cacheKeys = new string[registeredKeys.Count];
                    registeredKeys.CopyTo(cacheKeys);
                    _cacheKeysByResourceId.Remove(resourceId);
                }

                for (int i = 0; i < cacheKeys.Length; i++)
                {
                    if (_pendingRequests.TryGetValue(
                            cacheKeys[i],
                            out PendingRequestState pendingRequest))
                    {
                        RetirePendingRequestLocked(
                            cacheKeys[i],
                            pendingRequest,
                            new OperationCanceledException(
                                $"Cached provider request for '{resourceId}' "
                                + "was canceled because its cache was cleared."));
                    }

                    if (!_memoryCache.TryGetValue(
                            cacheKeys[i],
                            out LinkedListNode<CacheEntry> node))
                    {
                        continue;
                    }

                    if (node.Value.Resource != null)
                    {
                        entriesToRelease.Add(node.Value);
                    }

                    _lruList.Remove(node);
                    _memoryCache.Remove(cacheKeys[i]);
                }
            }

            RetireCacheEntries(entriesToRelease);
            for (int i = 0; i < cacheKeys.Length; i++)
            {
                DeleteDiskCacheEntry(cacheKeys[i]);
            }

            DeletePersistedCacheEntries(resourceId);

            if (WrappedProvider is ISelectiveAsyncResourceCache selectiveCache)
            {
                selectiveCache.ClearCache(resourceId);
            }
        }

        public void ReleaseResource(ResourceType resource)
        {
            if (resource == null)
            {
                return;
            }

            List<CacheEntry> entriesToRelease = null;
            bool isManagedResource;
            lock (_lock)
            {
                isManagedResource = IsResourceCachedLocked(resource)
                    || HasRetiredResourceLocked(resource);
                if (_callerLeaseCounts.TryGetValue(
                        resource,
                        out int callerLeaseCount))
                {
                    isManagedResource = true;
                    if (callerLeaseCount > 1)
                    {
                        _callerLeaseCounts[resource] = callerLeaseCount - 1;
                    }
                    else
                    {
                        _callerLeaseCounts.Remove(resource);
                        entriesToRelease = RemoveRetiredEntriesLocked(resource);
                    }
                }
                else if (isManagedResource)
                {
                    entriesToRelease = RemoveRetiredEntriesLocked(resource);
                }
            }

            ReleaseCacheEntriesImmediately(entriesToRelease);
            if (!isManagedResource)
            {
                ReleaseWrappedResource(resource);
            }
        }

        #endregion

        #region Private Methods — Coroutine

        private static Exception GetWrappedInitializationException(
            IAsyncResourceProvider<ResourceType> wrappedProvider)
        {
            return wrappedProvider
                is IAsyncResourceInitializationStatus initializationStatus
                    ? initializationStatus.InitializationException
                    : null;
        }

        private void HandleInitializationFailure(
            int initializationGeneration,
            Exception exception)
        {
            if (!IsCurrentInitialization(initializationGeneration))
            {
                return;
            }

            InitializationException = exception;
            IsInitialized = false;
            QuickLog.Error<CachedAsyncResourceProvider<ResourceType>>(
                "Cached provider initialization failed: {0}",
                exception);
        }

        private bool IsCurrentInitialization(int initializationGeneration)
        {
            lock (_lock)
            {
                return initializationGeneration == _initializationGeneration;
            }
        }

        private static void HandleCatalogInvalidationFailure(
            Exception exception)
        {
            QuickLog.Error<CachedAsyncResourceProvider<ResourceType>>(
                "Cached provider catalog invalidation failed: {0}",
                exception);
        }

        private void HandleLoadCoroutineFailure(
            ResourceLoadingHandler<ResourceType> originalHandler,
            string resourceId,
            int requestGeneration,
            Exception exception)
        {
            InvalidOperationException loadException
                = new InvalidOperationException(
                    $"Cached provider load coroutine failed for "
                    + $"'{resourceId}'.",
                    exception);
            CompletePendingRequests(
                resourceId,
                null,
                loadException,
                requestGeneration);
            if (originalHandler.IsCompleted)
            {
                return;
            }

            if (originalHandler.IsCancellationRequested)
            {
                originalHandler.Cancel();
                return;
            }

            originalHandler.LoadingStatus = LoadingStatus.Completed;
            originalHandler.ResourceStatus = ResourceStatus.Failed;
            originalHandler.Exception = loadException;
        }

        private IEnumerator LoadFromProviderCoroutine(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> originalHandler,
            string resourceId,
            int requestGeneration,
            IAsyncResourceProvider<ResourceType> wrappedProvider
        )
        {
            ResourceLoadingHandler<ResourceType> wrapperHandler
                = new ResourceLoadingHandler<ResourceType>();
            bool originalDetached = false;

            try
            {
                wrappedProvider.TryLoadResource(id, wrapperHandler);
            }
            catch (Exception exception)
            {
                CompletePendingRequests(
                    resourceId,
                    null,
                    exception,
                    requestGeneration);
                originalHandler.LoadingStatus = LoadingStatus.Completed;
                originalHandler.ResourceStatus = ResourceStatus.Failed;
                originalHandler.Exception = exception;
                yield break;
            }

            float startTime = Time.realtimeSinceStartup;
            float wrappedTimeout = wrappedProvider.ResourceLoadingTimeout;
            if (wrappedTimeout <= 0f || float.IsNaN(wrappedTimeout))
            {
                wrappedTimeout = ResourceLoadingTimeout;
            }

            while (wrapperHandler.LoadingStatus != LoadingStatus.Completed
                   && Time.realtimeSinceStartup - startTime
                   < wrappedTimeout)
            {
                if (!originalDetached
                    && originalHandler.IsCancellationRequested)
                {
                    originalHandler.Cancel();
                    originalDetached = true;

                    if (!HasActivePendingRequest(
                            resourceId,
                            requestGeneration))
                    {
                        wrapperHandler.Cancel();
                        CompletePendingRequests(
                            resourceId,
                            null,
                            originalHandler.Exception,
                            requestGeneration);
                        yield break;
                    }
                }

                yield return null;
            }

            if (wrapperHandler.LoadingStatus == LoadingStatus.Completed
                && wrapperHandler.ResourceStatus == ResourceStatus.Loaded
                && wrapperHandler.Resouce != null)
            {
                bool isCurrentRequest = IsCurrentPendingRequest(
                    resourceId,
                    requestGeneration);
                if (isCurrentRequest)
                {
                    AddToMemoryCache(
                        resourceId,
                        wrapperHandler.Resouce,
                        false,
                        wrappedProvider
                            as IAsyncResourceReleaseProvider<ResourceType>);
                    SaveToDisk(id, resourceId, wrapperHandler.Resouce);
                    QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                        "Loaded and cached '{0}' from wrapped provider.",
                        resourceId
                    );
                }

                CompletePendingRequests(
                    resourceId,
                    wrapperHandler.Resouce,
                    null,
                    requestGeneration);

                if (!originalDetached
                    && originalHandler.LoadingStatus != LoadingStatus.Completed)
                {
                    RetainCallerLease(
                        resourceId,
                        wrapperHandler.Resouce);
                    originalHandler.Resouce = wrapperHandler.Resouce;
                    originalHandler.LoadingStatus = LoadingStatus.Completed;
                    originalHandler.ResourceStatus = ResourceStatus.Loaded;
                    originalHandler.ProviderSource = GetType().Name;
                }

                if (!isCurrentRequest)
                {
                    ReleaseWrappedResource(
                        wrapperHandler.Resouce,
                        wrappedProvider
                            as IAsyncResourceReleaseProvider<ResourceType>);
                }
            }
            else
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "Wrapped provider failed to load '{0}'.", resourceId
                );

                Exception failException = wrapperHandler.Exception
                    ?? (wrapperHandler.IsCompleted
                        ? new InvalidOperationException(
                            $"Wrapped provider failed to load '{resourceId}'.")
                        : new TimeoutException(
                            $"Wrapped provider timed out loading '{resourceId}' "
                            + $"after {wrappedTimeout:F1}s."));
                if (!wrapperHandler.IsCompleted)
                {
                    wrapperHandler.Cancel();
                }

                CompletePendingRequests(
                    resourceId,
                    null,
                    failException,
                    requestGeneration);

                if (originalHandler.LoadingStatus != LoadingStatus.Completed)
                {
                    originalHandler.LoadingStatus = LoadingStatus.Completed;
                    originalHandler.ResourceStatus
                        = failException is OperationCanceledException
                            ? ResourceStatus.Canceled
                            : ResourceStatus.Failed;
                    originalHandler.Exception = failException;
                }


                if (wrapperHandler.Resouce != null)
                {
                    ReleaseWrappedResource(
                        wrapperHandler.Resouce,
                        wrappedProvider
                            as IAsyncResourceReleaseProvider<ResourceType>);
                }
            }
        }

        #endregion

        #region Private Methods — Memory Cache

        private void AddToMemoryCache(
            string resourceId,
            ResourceType resource,
            bool ownsResource,
            IAsyncResourceReleaseProvider<ResourceType> releaseProvider)
        {
            List<CacheEntry> entriesToRelease = null;
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(resourceId, out LinkedListNode<CacheEntry> existing))
                {
                    if (existing.Value.Resource != null)
                    {
                        entriesToRelease = new List<CacheEntry>
                        {
                            existing.Value
                        };
                    }

                    _lruList.Remove(existing);
                    _memoryCache.Remove(resourceId);
                }

                CacheEntry entry = new CacheEntry
                {
                    ResourceId = resourceId,
                    Resource = resource,
                    CachedAt = DateTime.UtcNow,
                    OwnsResource = ownsResource,
                    ReleaseProvider = releaseProvider
                };

                LinkedListNode<CacheEntry> node = _lruList.AddFirst(entry);
                _memoryCache[resourceId] = node;

                while (_lruList.Count > _maxCacheEntries && _lruList.Last != null)
                {
                    LinkedListNode<CacheEntry> evicted = _lruList.Last;
                    _lruList.RemoveLast();
                    _memoryCache.Remove(evicted.Value.ResourceId);

                    if (evicted.Value.Resource != null)
                    {
                        entriesToRelease ??= new List<CacheEntry>();
                        entriesToRelease.Add(evicted.Value);
                    }

                    QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                        "LRU evicted '{0}' from memory cache.", evicted.Value.ResourceId
                    );
                }
            }

            RetireCacheEntries(entriesToRelease);
        }

        private void RetireCacheEntries(List<CacheEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            List<CacheEntry> entriesToRelease = null;
            lock (_lock)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    CacheEntry entry = entries[i];
                    if (entry == null || entry.IsRetired)
                    {
                        continue;
                    }

                    entry.IsRetired = true;
                    if (HasCallerLeaseLocked(entry.Resource))
                    {
                        _retiredEntries.Add(entry);
                    }
                    else
                    {
                        entriesToRelease ??= new List<CacheEntry>();
                        entriesToRelease.Add(entry);
                    }
                }
            }

            ReleaseCacheEntriesImmediately(entriesToRelease);
        }

        private void RetainCallerLease(
            string resourceId,
            ResourceType resource)
        {
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(
                        resourceId,
                        out LinkedListNode<CacheEntry> node)
                    && ReferenceEquals(node.Value.Resource, resource))
                {
                    RetainCallerLeaseLocked(resource);
                }
            }
        }

        private void RetainCallerLeaseLocked(ResourceType resource)
        {
            if (resource == null)
            {
                return;
            }

            _callerLeaseCounts.TryGetValue(
                resource,
                out int callerLeaseCount);
            _callerLeaseCounts[resource] = callerLeaseCount + 1;
        }

        private bool HasCallerLeaseLocked(ResourceType resource)
        {
            return resource != null
                && _callerLeaseCounts.TryGetValue(
                    resource,
                    out int callerLeaseCount)
                && callerLeaseCount > 0;
        }

        private bool IsResourceCachedLocked(ResourceType resource)
        {
            foreach (LinkedListNode<CacheEntry> node in _memoryCache.Values)
            {
                if (ReferenceEquals(node.Value.Resource, resource))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRetiredResourceLocked(ResourceType resource)
        {
            for (int i = 0; i < _retiredEntries.Count; i++)
            {
                if (ReferenceEquals(_retiredEntries[i].Resource, resource))
                {
                    return true;
                }
            }

            return false;
        }

        private List<CacheEntry> RemoveRetiredEntriesLocked(
            ResourceType resource)
        {
            List<CacheEntry> entries = null;
            for (int i = _retiredEntries.Count - 1; i >= 0; i--)
            {
                CacheEntry entry = _retiredEntries[i];
                if (!ReferenceEquals(entry.Resource, resource))
                {
                    continue;
                }

                entries ??= new List<CacheEntry>();
                entries.Add(entry);
                _retiredEntries.RemoveAt(i);
            }

            return entries;
        }

        private static void ReleaseCacheEntriesImmediately(
            List<CacheEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                CacheEntry entry = entries[i];
                if (entry.OwnsResource)
                {
                    DestroyOwnedResource(entry.Resource);
                }
                else
                {
                    ReleaseWrappedResource(
                        entry.Resource,
                        entry.ReleaseProvider);
                }
            }
        }

        private static void DestroyOwnedResource(ResourceType resource)
        {
            if (resource == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(resource);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(resource);
            }
        }

        private void ReleaseWrappedResource(ResourceType resource)
        {
            ReleaseWrappedResource(
                resource,
                WrappedProvider
                    as IAsyncResourceReleaseProvider<ResourceType>);
        }

        private static void ReleaseWrappedResource(
            ResourceType resource,
            IAsyncResourceReleaseProvider<ResourceType> releaseProvider)
        {
            if (resource != null && releaseProvider != null)
            {
                releaseProvider.ReleaseResource(resource);
            }
        }

        private void RegisterCacheKey(string resourceId, string cacheKey)
        {
            lock (_lock)
            {
                if (!_cacheKeysByResourceId.TryGetValue(
                        resourceId,
                        out HashSet<string> keys))
                {
                    keys = new HashSet<string>(StringComparer.Ordinal);
                    _cacheKeysByResourceId[resourceId] = keys;
                }

                keys.Add(cacheKey);
            }
        }

        private void MoveToFront(LinkedListNode<CacheEntry> node)
        {
            lock (_lock)
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
        }

        private void CompletePendingRequests(
            string resourceId,
            ResourceType resource,
            Exception exception,
            int requestGeneration
        )
        {
            lock (_lock)
            {
                if (!_pendingRequests.TryGetValue(
                        resourceId,
                        out PendingRequestState pendingRequest)
                    || pendingRequest.Generation != requestGeneration)
                {
                    return;
                }

                foreach (ResourceLoadingHandler<ResourceType> pending
                         in pendingRequest.Subscribers)
                {
                    if (pending.IsCancellationRequested)
                    {
                        pending.Cancel();
                        continue;
                    }

                    if (resource != null)
                    {
                        if (IsResourceCachedLocked(resource))
                        {
                            RetainCallerLeaseLocked(resource);
                        }

                        pending.Resouce = resource;
                        pending.LoadingStatus = LoadingStatus.Completed;
                        pending.ResourceStatus = ResourceStatus.Loaded;
                        pending.ProviderSource = GetType().Name;
                    }
                    else
                    {
                        pending.LoadingStatus = LoadingStatus.Completed;
                        pending.ResourceStatus
                            = exception is OperationCanceledException
                                ? ResourceStatus.Canceled
                                : ResourceStatus.Failed;
                        pending.Exception = exception;
                    }
                }

                _pendingRequests.Remove(resourceId);
            }
        }

        private void RemovePendingRequest(
            string resourceId,
            int requestGeneration)
        {
            lock (_lock)
            {
                RemovePendingRequestLocked(resourceId, requestGeneration);
            }
        }

        private void RemovePendingRequestLocked(
            string resourceId,
            int requestGeneration)
        {
            if (_pendingRequests.TryGetValue(
                    resourceId,
                    out PendingRequestState pendingRequest)
                && pendingRequest.Generation == requestGeneration)
            {
                _pendingRequests.Remove(resourceId);
            }
        }

        private void RetireAllPendingRequestsLocked(Exception exception)
        {
            foreach (KeyValuePair<string, PendingRequestState> pair
                     in _pendingRequests)
            {
                CancelPendingRequest(pair.Value, exception);
            }

            _pendingRequests.Clear();
        }

        private void RetirePendingRequestLocked(
            string resourceId,
            PendingRequestState pendingRequest,
            Exception exception)
        {
            CancelPendingRequest(pendingRequest, exception);
            if (_pendingRequests.TryGetValue(
                    resourceId,
                    out PendingRequestState registered)
                && ReferenceEquals(registered, pendingRequest))
            {
                _pendingRequests.Remove(resourceId);
            }
        }

        private static void CancelPendingRequest(
            PendingRequestState pendingRequest,
            Exception exception)
        {
            CancelHandler(pendingRequest.OriginalHandler, exception);
            for (int i = 0; i < pendingRequest.Subscribers.Count; i++)
            {
                CancelHandler(pendingRequest.Subscribers[i], exception);
            }
        }

        private static void CancelHandler(
            ResourceLoadingHandler<ResourceType> handler,
            Exception exception)
        {
            if (handler == null || handler.IsCompleted)
            {
                return;
            }

            handler.Cancel();
            handler.Exception = exception;
        }

        private bool HasActivePendingRequest(
            string resourceId,
            int requestGeneration)
        {
            lock (_lock)
            {
                if (!_pendingRequests.TryGetValue(
                        resourceId,
                        out PendingRequestState pendingRequest)
                    || pendingRequest.Generation != requestGeneration)
                {
                    return false;
                }

                for (int i = 0; i < pendingRequest.Subscribers.Count; i++)
                {
                    if (!pendingRequest.Subscribers[i].IsCancellationRequested)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private bool IsCurrentPendingRequest(
            string resourceId,
            int requestGeneration)
        {
            lock (_lock)
            {
                return _pendingRequests.TryGetValue(
                        resourceId,
                        out PendingRequestState pendingRequest)
                    && pendingRequest.Generation == requestGeneration;
            }
        }

        private int NextRequestGenerationLocked()
        {
            unchecked
            {
                _nextRequestGeneration++;
                if (_nextRequestGeneration == 0)
                {
                    _nextRequestGeneration++;
                }
            }

            return _nextRequestGeneration;
        }

        #endregion

        #region Private Methods — Disk Cache

        private string ResolveDiskCacheRoot()
        {
            string basePath = _cacheBasePath == CacheBasePathType.PersistentDataPath
                ? Application.persistentDataPath
                : Application.temporaryCachePath;
            string subFolder = string.IsNullOrWhiteSpace(_cacheSubFolder)
                ? "CachedResources"
                : _cacheSubFolder.Trim();
            string fullBasePath = Path.GetFullPath(basePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string cacheRoot = Path.GetFullPath(
                Path.Combine(fullBasePath, subFolder));
            string requiredPrefix = fullBasePath + Path.DirectorySeparatorChar;
            if (!cacheRoot.StartsWith(
                    requiredPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Cache subfolder must remain inside the selected cache base path.");
            }

            return cacheRoot;
        }

        private string GetDiskCachePath(string resourceId)
        {
            string sanitized = SanitizeFileName(resourceId);
            return Path.Combine(_diskCacheRoot, sanitized);
        }

        private bool DiskCacheHasResource(string resourceId)
        {
            string filePath = GetDiskCachePath(resourceId);
            if (!File.Exists(filePath))
            {
                return false;
            }

            if (IsDiskEntryStale(filePath))
            {
                return false;
            }

            return true;
        }

        private bool TryLoadFromDisk(
            IAsyncResourceId id,
            string resourceId,
            out ResourceType resource)
        {
            resource = null;

            if (!SupportsDiskCache(id))
            {
                DeleteDiskCacheEntry(resourceId);
                return false;
            }

            string filePath = GetDiskCachePath(resourceId);
            if (!File.Exists(filePath))
            {
                return false;
            }

            if (IsDiskEntryStale(filePath))
            {
                DeleteDiskCacheEntry(resourceId);
                return false;
            }

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                if (data.Length < 8)
                {
                    DeleteDiskCacheEntry(resourceId);
                    return false;
                }

                byte[] resourceData = new byte[data.Length - 8];
                Array.Copy(data, 8, resourceData, 0, resourceData.Length);

                resource = ConvertFromBytes(resourceData);
                return resource != null;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "Failed to load cached resource '{0}' from disk: {1}",
                    resourceId, ex.Message
                );
                DeleteDiskCacheEntry(resourceId);
                return false;
            }
        }

        private void SaveToDisk(
            IAsyncResourceId id,
            string resourceId,
            ResourceType resource)
        {
            if (_cacheTTL <= 0f || string.IsNullOrEmpty(_diskCacheRoot))
            {
                return;
            }

            if (!SupportsDiskCache(id))
            {
                DeleteDiskCacheEntry(resourceId);
                return;
            }

            try
            {
                if (!Directory.Exists(_diskCacheRoot))
                {
                    Directory.CreateDirectory(_diskCacheRoot);
                }

                byte[] resourceData = ConvertToBytes(resource);
                if (resourceData == null)
                {
                    return;
                }

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                byte[] timestampBytes = BitConverter.GetBytes(timestamp);

                byte[] fileData = new byte[timestampBytes.Length + resourceData.Length];
                Array.Copy(timestampBytes, 0, fileData, 0, timestampBytes.Length);
                Array.Copy(resourceData, 0, fileData, timestampBytes.Length, resourceData.Length);

                string filePath = GetDiskCachePath(resourceId);
                DiskCacheIdentity.Write(
                    filePath,
                    id.ResourceId,
                    fileData);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "Failed to save resource '{0}' to disk cache: {1}",
                    resourceId, ex.Message
                );
            }
        }

        private bool IsDiskEntryStale(string filePath)
        {
            try
            {
                using (FileStream fs = File.OpenRead(filePath))
                {
                    byte[] timestampBytes = new byte[8];
                    if (fs.Read(timestampBytes, 0, 8) < 8)
                    {
                        return true;
                    }

                    long cachedTimestamp = BitConverter.ToInt64(timestampBytes, 0);
                    DateTime cachedTime = DateTimeOffset.FromUnixTimeSeconds(cachedTimestamp)
                        .UtcDateTime;
                    TimeSpan age = DateTime.UtcNow - cachedTime;

                    return age.TotalSeconds > _cacheTTL;
                }
            }
            catch
            {
                return true;
            }
        }

        private void DeleteDiskCacheEntry(string resourceId)
        {
            try
            {
                string filePath = GetDiskCachePath(resourceId);
                DiskCacheIdentity.Delete(filePath);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "Failed to delete cached file for '{0}': {1}",
                    resourceId, ex.Message
                );
            }
        }

        private void ClearDiskCache()
        {
            if (string.IsNullOrEmpty(_diskCacheRoot) || !Directory.Exists(_diskCacheRoot))
            {
                return;
            }

            try
            {
                string[] files = Directory.GetFiles(_diskCacheRoot);
                foreach (string file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Best effort
                    }
                }
            }
            catch (Exception ex)
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "Failed to clear disk cache: {0}", ex.Message
                );
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "_empty";
            }

            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(name));
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private void DeletePersistedCacheEntries(string resourceId)
        {
            if (string.IsNullOrEmpty(_diskCacheRoot)
                || !Directory.Exists(_diskCacheRoot))
            {
                return;
            }

            try
            {
                DiskCacheIdentity.DeleteByResourceId(
                    _diskCacheRoot,
                    resourceId);
            }
            catch (Exception exception)
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "Failed to clear persisted cache for '{0}': {1}",
                    resourceId,
                    exception.Message);
            }
        }

        private bool SupportsDiskCache(IAsyncResourceId id)
        {
            if (typeof(ResourceType) != typeof(TextAsset))
            {
                return true;
            }

            return GetDataType(id) != DataType.Binary;
        }

        #endregion

        #region Private Methods — Resource Conversion

        private static byte[] ConvertToBytes(ResourceType resource)
        {
            if (resource is TextAsset textAsset)
            {
                return textAsset.bytes;
            }

            if (resource is Texture2D texture)
            {
                return texture.EncodeToPNG();
            }

            return null;
        }

        private static ResourceType ConvertFromBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            if (typeof(ResourceType) == typeof(Texture2D))
            {
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(data);
                return texture as ResourceType;
            }

            if (typeof(ResourceType) == typeof(TextAsset))
            {
                TextAsset textAsset = new TextAsset(
                    Encoding.UTF8.GetString(data));
                return textAsset as ResourceType;
            }

            // Most Unity objects cannot be reconstructed from raw bytes.
            return null;
        }

        #endregion
    }
}
