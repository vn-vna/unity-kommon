using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        ICatalogAwareAsyncResourceProvider
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

        public float ResourceLoadingTimeout => _timeout;

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

        private IAsyncResourceProvider<ResourceType> WrappedProvider
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
        private readonly Dictionary<string, List<ResourceLoadingHandler<ResourceType>>> _pendingRequests
            = new Dictionary<string, List<ResourceLoadingHandler<ResourceType>>>();

        private IAsyncResourceProvider<ResourceType> _wrappedProvider;
        private string _diskCacheRoot;
        private CatalogData _catalogData;

        #endregion

        #region Structs

        private struct CacheEntry
        {
            public string ResourceId;
            public ResourceType Resource;
            public DateTime CachedAt;
        }

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            IsInitialized = false;
        }

        #endregion

        #region Public Methods

        public void Initialize()
        {
            lock (_lock)
            {
                _memoryCache.Clear();
                _lruList.Clear();
                _pendingRequests.Clear();
                IsInitialized = false;
            }

            _wrappedProvider = _wrappedProviderAsset as IAsyncResourceProvider<ResourceType>;
            if (_wrappedProvider != null)
            {
                _wrappedProvider.Initialize();
            }

            _catalogData = new CatalogData();
            if (_catalogConfig.UseCatalog
                && !string.IsNullOrEmpty(_catalogConfig.CatalogFileName))
            {
                _catalogData.LoadFromStreamingAssets(_catalogConfig.CatalogFileName);
            }

            _diskCacheRoot = ResolveDiskCacheRoot();

            if (!string.IsNullOrEmpty(_diskCacheRoot) && !Directory.Exists(_diskCacheRoot))
            {
                Directory.CreateDirectory(_diskCacheRoot);
            }

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
            lock (_lock)
            {
                if (_memoryCache.ContainsKey(resourceId.ResourceId))
                {
                    return true;
                }
            }

            if (DiskCacheHasResource(resourceId.ResourceId))
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

        public void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            if (id == null || string.IsNullOrEmpty(id.ResourceId))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new ArgumentNullException(nameof(id));
                return;
            }

            string resourceId = id.ResourceId;

            lock (_lock)
            {
                if (_memoryCache.TryGetValue(resourceId, out LinkedListNode<CacheEntry> node))
                {
                    MoveToFront(node);

                    handler.Resouce = node.Value.Resource;
                    handler.LoadingStatus = LoadingStatus.Completed;
                    handler.ResourceStatus = ResourceStatus.Loaded;
                    handler.ProviderSource = GetType().Name;

                    QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                        "Memory cache hit for '{0}'.", resourceId
                    );
                    return;
                }

                if (_pendingRequests.TryGetValue(resourceId, out List<ResourceLoadingHandler<ResourceType>> pendingList))
                {
                    pendingList.Add(handler);
                    QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                        "Request for '{0}' already in-flight. Added to pending list ({1} total).",
                        resourceId, pendingList.Count
                    );
                    return;
                }

                _pendingRequests[resourceId] = new List<ResourceLoadingHandler<ResourceType>>();
            }

            if (TryLoadFromDisk(resourceId, out ResourceType diskResource))
            {
                AddToMemoryCache(resourceId, diskResource);
                CompletePendingRequests(resourceId, diskResource, null);

                handler.Resouce = diskResource;
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Loaded;
                handler.ProviderSource = GetType().Name;

                QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                    "Disk cache hit for '{0}'.", resourceId
                );
                return;
            }

            if (_wrappedProvider == null)
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "No wrapped provider configured. Cannot load '{0}'.", resourceId
                );
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new InvalidOperationException(
                    "No wrapped provider configured."
                );
                lock (_lock) { _pendingRequests.Remove(resourceId); }
                return;
            }

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;

            Dispatcher.DispatchCoroutine(
                LoadFromProviderCoroutine(id, handler, resourceId)
            );
        }

        public void ClearCache()
        {
            lock (_lock)
            {
                _memoryCache.Clear();
                _lruList.Clear();
            }

            ClearDiskCache();

            QuickLog.Info<CachedAsyncResourceProvider<ResourceType>>(
                "Cache cleared (memory + disk)."
            );
        }

        public void ClearCache(string resourceId)
        {
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(resourceId, out LinkedListNode<CacheEntry> node))
                {
                    _lruList.Remove(node);
                    _memoryCache.Remove(resourceId);
                }
            }

            DeleteDiskCacheEntry(resourceId);
        }

        #endregion

        #region Private Methods — Coroutine

        private IEnumerator LoadFromProviderCoroutine(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> originalHandler,
            string resourceId
        )
        {
            ResourceLoadingHandler<ResourceType> wrapperHandler
                = new ResourceLoadingHandler<ResourceType>();

            _wrappedProvider.TryLoadResource(id, wrapperHandler);

            float timer = 0f;
            while (wrapperHandler.LoadingStatus != LoadingStatus.Completed
                   && timer < _wrappedProvider.ResourceLoadingTimeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            if (wrapperHandler.LoadingStatus == LoadingStatus.Completed
                && wrapperHandler.ResourceStatus == ResourceStatus.Loaded
                && wrapperHandler.Resouce != null)
            {
                AddToMemoryCache(resourceId, wrapperHandler.Resouce);
                SaveToDisk(resourceId, wrapperHandler.Resouce);

                QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                    "Loaded and cached '{0}' from wrapped provider.", resourceId
                );

                lock (_lock)
                {
                    if (_pendingRequests.TryGetValue(resourceId,
                            out List<ResourceLoadingHandler<ResourceType>> pendingList))
                    {
                        foreach (ResourceLoadingHandler<ResourceType> pending in pendingList)
                        {
                            pending.Resouce = wrapperHandler.Resouce;
                            pending.LoadingStatus = LoadingStatus.Completed;
                            pending.ResourceStatus = ResourceStatus.Loaded;
                            pending.ProviderSource = GetType().Name;
                        }

                        _pendingRequests.Remove(resourceId);
                    }
                }

                if (originalHandler.LoadingStatus != LoadingStatus.Completed)
                {
                    originalHandler.Resouce = wrapperHandler.Resouce;
                    originalHandler.LoadingStatus = LoadingStatus.Completed;
                    originalHandler.ResourceStatus = ResourceStatus.Loaded;
                    originalHandler.ProviderSource = GetType().Name;
                }
            }
            else
            {
                QuickLog.Warning<CachedAsyncResourceProvider<ResourceType>>(
                    "Wrapped provider failed to load '{0}'.", resourceId
                );

                Exception failException = wrapperHandler.Exception
                    ?? new InvalidOperationException(
                        $"Wrapped provider failed to load '{resourceId}'."
                    );

                lock (_lock)
                {
                    if (_pendingRequests.TryGetValue(resourceId,
                            out List<ResourceLoadingHandler<ResourceType>> pendingList))
                    {
                        foreach (ResourceLoadingHandler<ResourceType> pending in pendingList)
                        {
                            pending.LoadingStatus = LoadingStatus.Completed;
                            pending.ResourceStatus = ResourceStatus.Failed;
                            pending.Exception = failException;
                        }

                        _pendingRequests.Remove(resourceId);
                    }
                }

                if (originalHandler.LoadingStatus != LoadingStatus.Completed)
                {
                    originalHandler.LoadingStatus = LoadingStatus.Completed;
                    originalHandler.ResourceStatus = ResourceStatus.Failed;
                    originalHandler.Exception = failException;
                }
            }
        }

        #endregion

        #region Private Methods — Memory Cache

        private void AddToMemoryCache(string resourceId, ResourceType resource)
        {
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(resourceId, out LinkedListNode<CacheEntry> existing))
                {
                    _lruList.Remove(existing);
                    _memoryCache.Remove(resourceId);
                }

                CacheEntry entry = new CacheEntry
                {
                    ResourceId = resourceId,
                    Resource = resource,
                    CachedAt = DateTime.UtcNow
                };

                LinkedListNode<CacheEntry> node = _lruList.AddFirst(entry);
                _memoryCache[resourceId] = node;

                while (_lruList.Count > _maxCacheEntries && _lruList.Last != null)
                {
                    LinkedListNode<CacheEntry> evicted = _lruList.Last;
                    _lruList.RemoveLast();
                    _memoryCache.Remove(evicted.Value.ResourceId);

                    QuickLog.Debug<CachedAsyncResourceProvider<ResourceType>>(
                        "LRU evicted '{0}' from memory cache.", evicted.Value.ResourceId
                    );
                }
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
            Exception exception
        )
        {
            lock (_lock)
            {
                if (!_pendingRequests.TryGetValue(resourceId,
                        out List<ResourceLoadingHandler<ResourceType>> pendingList))
                {
                    return;
                }

                foreach (ResourceLoadingHandler<ResourceType> pending in pendingList)
                {
                    if (resource != null)
                    {
                        pending.Resouce = resource;
                        pending.LoadingStatus = LoadingStatus.Completed;
                        pending.ResourceStatus = ResourceStatus.Loaded;
                        pending.ProviderSource = GetType().Name;
                    }
                    else
                    {
                        pending.LoadingStatus = LoadingStatus.Completed;
                        pending.ResourceStatus = ResourceStatus.Failed;
                        pending.Exception = exception;
                    }
                }

                _pendingRequests.Remove(resourceId);
            }
        }

        #endregion

        #region Private Methods — Disk Cache

        private string ResolveDiskCacheRoot()
        {
            string basePath = _cacheBasePath == CacheBasePathType.PersistentDataPath
                ? Application.persistentDataPath
                : Application.temporaryCachePath;

            if (string.IsNullOrEmpty(_cacheSubFolder))
            {
                return basePath;
            }

            return Path.Combine(basePath, _cacheSubFolder);
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

        private bool TryLoadFromDisk(string resourceId, out ResourceType resource)
        {
            resource = null;

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

        private void SaveToDisk(string resourceId, ResourceType resource)
        {
            if (_cacheTTL <= 0f || string.IsNullOrEmpty(_diskCacheRoot))
            {
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
                File.WriteAllBytes(filePath, fileData);
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
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
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
            char[] invalid = Path.GetInvalidFileNameChars();
            string sanitized = new string(
                name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()
            );
            return sanitized;
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

            // TextAsset and most Unity objects cannot be reconstructed
            // from raw bytes at runtime. Disk cache is skipped for these types.
            return null;
        }

        #endregion
    }
}
