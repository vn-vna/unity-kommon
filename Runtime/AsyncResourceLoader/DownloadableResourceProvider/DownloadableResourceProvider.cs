using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [ResourceProvider(
        "Downloadable",
        "Downloads via HTTP with in-memory + disk caching. Override ConvertResource(byte[])."
    )]
    public abstract class DownloadableResourceProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>,
        IDownloadableResourceProvider<ResourceType>,
        ICatalogAwareAsyncResourceProvider,
        IInvalidatableCatalog,
        ISelectiveAsyncResourceCache,
        IAsyncResourceCacheKeyProvider,
        IAsyncResourceInterpolationTagReceiver,
        IAsyncResourceDataTypeResolver,
        IAsyncResourceInitializationStatus,
        IAsyncResourceReleaseProvider<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        [SerializeField]
        private int priority;

        [SerializeField]
        [Min(0.1f)]
        private float timeout = 35f;

        [SerializeField]
        private float requestTimeout = 30f;

        [SerializeField]
        [Min(1)]
        private int maxConcurrentDownloads = 5;

        [SerializeField]
        [Min(1)]
        private int maxCacheEntries = 50;

        [SerializeField]
        private float cacheTTL;

        [SerializeField]
        private CacheBasePathType cacheBasePath = CacheBasePathType.PersistentDataPath;

        [SerializeField]
        private string cacheSubFolder = "downloadable_cache";

        [SerializeField]
        private CustomDownloadHeader[] headers;

        [SerializeField]
        [Tooltip("Base URL prefix (e.g. \"https://cdn.example.com/\"). Combined with catalog RelativePath to form the download URL.")]
        private string _baseUrl = "";

        [SerializeField]
        [Tooltip("Format string for resolving resource ID to a URL path when no catalog entry is found.\n{0} = ResourceId (e.g. \"puzzles/level_{0}.json\")")]
        private string _urlFormat = "{0}";

        [SerializeField]
        [Tooltip(
            "When enabled, loads a catalog JSON file to determine which "
            + "resources this provider can serve.")]
        private bool _useCatalog;

        [SerializeField]
        [Tooltip(
            "File name or relative path of the catalog JSON file. "
            + "Combined with the base URL to form the full catalog URL.")]
        private string _catalogFileName;

        [SerializeField]
        [Tooltip(
            "When enabled with a catalog, the provider rejects ALL resource "
            + "requests until the catalog is fully loaded. HasResource returns "
            + "false and TryLoadResource fails immediately.")]
        private bool _forceRequiredCatalog;

        private volatile bool _isInitialized;
        private string _cachedDiskBasePath;
        private CatalogData _catalogData;
        private readonly Dictionary<string, (byte[] data, DateTime timestamp)> _memoryCache = new();
        private readonly LinkedList<string> _lruList = new();
        private readonly Dictionary<string, ActiveDownload> _activeDownloads = new();
        private readonly Queue<DownloadRequest> _pendingQueue = new();
        private readonly Dictionary<ResourceType, int> _resourceLeaseCounts
            = new Dictionary<ResourceType, int>();
        private readonly Dictionary<string, HashSet<string>> _cacheKeysByResourceId
            = new();
        private readonly object _lock = new();
        private IReadOnlyDictionary<string, string> _catalogInterpolationTags;
        private uint _catalogGeneration;
        private UnityWebRequest _activeCatalogRequest;

        private class DownloadRequest
        {
            public string Url;
            public string ResourceId;
            public string CacheKey;
            public ResourceLoadingHandler<ResourceType> Handler;
        }

        private class ActiveDownload
        {
            public UnityWebRequest Request;
            public List<ResourceLoadingHandler<ResourceType>> Handlers;
            public float StartTime;
            public float LastProgress;
            public string Url;
            public string ResourceId;
            public string CacheKey;
            public volatile bool IsCanceled;
        }

        public int Priority => priority;
        public bool IsInitialized => _isInitialized;
        public Exception InitializationException { get; private set; }
        public float ResourceLoadingTimeout => timeout > 0f ? timeout : 35f;
        public float RequestTimeout => requestTimeout;
        public float CacheTTL => cacheTTL;
        public int MaxConcurrentDownloads => Mathf.Max(1, maxConcurrentDownloads);
        public int MaxCacheEntries => Mathf.Max(1, maxCacheEntries);
        public string CacheSubFolder => cacheSubFolder;
        public CacheBasePathType CacheBasePath => cacheBasePath;
        public CustomDownloadHeader[] Headers => headers;
        public string BaseUrl => AcquireBaseUrl();
        public string UrlFormat => _urlFormat;

        protected abstract ResourceType ConvertResource(byte[] data);

        public virtual void Initialize()
        {
            uint generation = ++_catalogGeneration;
            AbortActiveCatalogRequest();
            CancelAllRequests(new OperationCanceledException(
                "Download provider was reinitialized."));
            _isInitialized = false;
            InitializationException = null;
            _catalogData = new CatalogData();
            _cachedDiskBasePath = cacheBasePath == CacheBasePathType.PersistentDataPath
                ? Application.persistentDataPath
                : Application.temporaryCachePath;

            if (_forceRequiredCatalog
                && (!_useCatalog || string.IsNullOrWhiteSpace(_catalogFileName)))
            {
                HandleInitializationFailure(
                    generation,
                    new InvalidOperationException(
                        "A required catalog must be enabled and have a file name."
                    ));
                return;
            }

            if (_useCatalog && !string.IsNullOrEmpty(_catalogFileName))
            {
                string catalogUrl = ResolveCatalogUrl(_catalogFileName, BaseUrl);
                QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                    "Catalog load dispatched [gen={0}] URL: {1}",
                    generation, catalogUrl
                );

                bool dispatched = Dispatcher.TryDispatchCoroutine(
                    CoroutineExceptionGuard.Run(
                        LoadCatalogCoroutine(
                            catalogUrl,
                            generation,
                            _forceRequiredCatalog),
                        exception => HandleInitializationFailure(
                            generation,
                            exception)),
                    out _);
                if (!dispatched)
                {
                    HandleInitializationFailure(
                        generation,
                        new InvalidOperationException(
                            "Catalog initialization requires a Dispatcher instance."
                        ));
                    return;
                }
            }

            _isInitialized = !_forceRequiredCatalog
                || !_useCatalog
                || string.IsNullOrEmpty(_catalogFileName);

            QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                "Downloadable resource provider initialized."
            );
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

            if (!_isInitialized)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new InvalidOperationException(
                    "Downloadable resource provider is not initialized."
                );
                return;
            }

            if (_forceRequiredCatalog
                && (_catalogData == null || !_catalogData.IsLoaded))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new InvalidOperationException(
                    "Catalog is required but not loaded. "
                    + "All resource requests are rejected until the catalog "
                    + "is available."
                );
                return;
            }

            if (id is not IDownloadableAsyncResourceId downloadableId)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentException(
                    "Invalid resource ID type. Expected IDownloadableAsyncResourceId."
                );
                return;
            }

            if (string.IsNullOrWhiteSpace(id.ResourceId))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentException(
                    "Resource ID cannot be null, empty, or whitespace.",
                    nameof(id));
                return;
            }

            string url = ResolveDownloadUrl(id.ResourceId, downloadableId);
            if (string.IsNullOrEmpty(url))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentException(
                    "URL returned by resource ID is null or empty."
                );
                return;
            }

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;

            string resourceId = id.ResourceId;
            string cacheKey = BuildCacheKey(resourceId, url);
            RegisterCacheKey(resourceId, cacheKey);

            byte[] memoryData = null;
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(
                        cacheKey,
                        out (byte[] data, DateTime timestamp) cached))
                {
                    if (IsCacheValid(cached.timestamp))
                    {
                        TouchLru(cacheKey);
                        memoryData = cached.data;
                    }
                    else
                    {
                        EvictFromMemory(cacheKey);
                    }
                }
            }

            if (memoryData != null)
            {
                Exception cacheException;
                if (TryValidateContentHash(
                        resourceId,
                        memoryData,
                        out cacheException)
                    && TryCompleteFromCachedBytes(
                        handler,
                        memoryData,
                        url,
                        out cacheException))
                {
                    return;
                }

                QuickLog.Warning<DownloadableResourceProvider<ResourceType>>(
                    "Discarding invalid memory cache entry for '{0}': {1}",
                    resourceId,
                    cacheException?.Message ?? "Unknown cache conversion error");
                lock (_lock)
                {
                    EvictFromMemory(cacheKey);
                }

                DeleteDiskCacheEntry(cacheKey);
            }

            if (TryLoadFromDiskCache(cacheKey, resourceId, handler))
            {
                return;
            }

            lock (_lock)
            {
                if (_activeDownloads.TryGetValue(cacheKey, out ActiveDownload existing))
                {
                    existing.Handlers.Add(handler);
                    return;
                }

                if (_activeDownloads.Count >= MaxConcurrentDownloads)
                {
                    _pendingQueue.Enqueue(new DownloadRequest
                    {
                        Url = url,
                        ResourceId = resourceId,
                        CacheKey = cacheKey,
                        Handler = handler
                    });
                    return;
                }
            }

            if (!StartDownload(url, resourceId, cacheKey, handler))
            {
                ProcessQueue();
            }
        }

        public IReadOnlyCollection<string> CatalogedIds =>
            _catalogData?.CatalogedIds ?? Array.Empty<string>();

        public bool HasResource(IAsyncResourceId resourceId)
        {
            if (resourceId == null)
            {
                return false;
            }

            if (_catalogData == null || !_catalogData.IsLoaded)
            {
                return !_forceRequiredCatalog;
            }

            return _catalogData.HasResource(resourceId.ResourceId);
        }

        public DataType GetDataType(string resourceId) =>
            _catalogData?.GetDataType(resourceId) ?? DataType.Unknown;

        public DataType GetDataType(IAsyncResourceId resourceId)
        {
            return GetDataType(resourceId?.ResourceId);
        }

        public string GetCacheKey(IAsyncResourceId resourceId)
        {
            if (resourceId is not IDownloadableAsyncResourceId downloadableId)
            {
                return resourceId?.ResourceId;
            }

            string url = ResolveDownloadUrl(
                resourceId.ResourceId,
                downloadableId);
            return BuildCacheKey(resourceId.ResourceId, url);
        }

        public void SetCatalogInterpolationTags(
            IReadOnlyDictionary<string, string> tags)
        {
            if (tags == null)
            {
                _catalogInterpolationTags = null;
                return;
            }

            Dictionary<string, string> snapshot
                = new Dictionary<string, string>(tags.Count);
            foreach (KeyValuePair<string, string> tag in tags)
            {
                snapshot[tag.Key] = tag.Value ?? string.Empty;
            }

            _catalogInterpolationTags = snapshot;
        }

        public void SetInterpolationTags(
            IReadOnlyDictionary<string, string> tags)
        {
            SetCatalogInterpolationTags(tags);
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
                QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                    "Catalog invalidation requires a Dispatcher instance.");
            }
        }

        public IEnumerator InvalidateCatalogCoroutine(
            CatalogInvalidationMode mode)
        {
            QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                "Catalog invalidation (coroutine) requested [mode={0}].",
                mode
            );

            _catalogData?.Reset();

            AbortActiveCatalogRequest();

            if (mode == CatalogInvalidationMode.Aggressive)
            {
                ClearCache();

                QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                    "Aggressive mode — memory and disk caches cleared."
                );
            }

            uint gen = ++_catalogGeneration;
            if (_useCatalog && !string.IsNullOrEmpty(_catalogFileName))
            {
                string catalogUrl = ResolveCatalogUrl(_catalogFileName, BaseUrl);

                QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                    "Catalog re-fetch dispatched [gen={0}] URL: {1}",
                    gen, catalogUrl
                );

                yield return LoadCatalogCoroutine(catalogUrl, gen, true);
            }
        }

        protected virtual string AcquireBaseUrl()
            => _baseUrl;

        protected virtual string ResolveDownloadUrl(
            string resourceId,
            IDownloadableAsyncResourceId downloadableId
        )
        {
            if (_catalogData != null && _catalogData.IsLoaded)
            {
                string relativePath = _catalogData.GetRelativePath(resourceId);
                if (!string.IsNullOrEmpty(relativePath))
                {
                    return ApplyInterpolationTags(
                        CombineUrl(AcquireBaseUrl(), relativePath));
                }
            }

            string fallback = downloadableId.GetUrl(this);
            if (!string.IsNullOrEmpty(fallback))
            {
                return ApplyInterpolationTags(fallback);
            }

            return ApplyInterpolationTags(
                CombineUrl(
                    AcquireBaseUrl(),
                    string.Format(_urlFormat, resourceId)));
        }

        protected virtual string ResolveCatalogUrl(
            string catalogFileName,
            string baseUrl)
        {
            return ApplyInterpolationTags(
                CombineUrl(baseUrl, catalogFileName));
        }

        private string ApplyInterpolationTags(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            if (_catalogInterpolationTags != null)
            {
                foreach (KeyValuePair<string, string> kvp
                         in _catalogInterpolationTags)
                {
                    url = url.Replace($"{{{kvp.Key}}}", kvp.Value);
                }
            }

            int unresolvedTagStart = url.IndexOf('{');
            if (unresolvedTagStart >= 0 && url.IndexOf('}', unresolvedTagStart) > 0)
            {
                throw new FormatException(
                    $"URL contains an unresolved interpolation tag: '{url}'.");
            }

            return url;
        }

        private static string CombineUrl(string baseUrl, string relativePath)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                return relativePath;
            }

            if (Uri.TryCreate(relativePath, UriKind.Absolute, out Uri absoluteUri))
            {
                return absoluteUri.AbsoluteUri;
            }

            return baseUrl.TrimEnd('/')
                + "/"
                + (relativePath ?? string.Empty).TrimStart('/');
        }


        private IEnumerator LoadCatalogCoroutine(string url)
        {
            uint gen = ++_catalogGeneration;
            yield return LoadCatalogCoroutine(url, gen);
        }

        private IEnumerator LoadCatalogCoroutine(
            string url,
            uint generation,
            bool throwOnFailure = false)
        {
            QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                "Catalog download started [gen={0}] URL: {1}",
                generation, url
            );

            using UnityWebRequest webRequest = UnityWebRequest.Get(url);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeCatalogRequest = webRequest;

            if (headers != null)
            {
                foreach (CustomDownloadHeader header in headers)
                {
                    webRequest.SetRequestHeader(header.key, header.value);
                }
            }

            yield return webRequest.SendWebRequest();
            if (ReferenceEquals(_activeCatalogRequest, webRequest))
            {
                _activeCatalogRequest = null;
            }

            if (generation != _catalogGeneration)
            {
                QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                    "Catalog load aborted superseded by newer request. [gen={0}]",
                    generation
                );
                yield break;
            }

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                byte[] data = webRequest.downloadHandler.data;

                QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                    "Catalog downloaded successfully [gen={0}], {1} bytes.",
                    generation, data?.Length ?? 0
                );

                _catalogData.LoadFromBytes(data);

                if (_forceRequiredCatalog)
                {
                    _isInitialized = _catalogData.IsLoaded;
                }

                QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                    "Catalog parsed [gen={0}] — {1} entries loaded.",
                    generation,
                    _catalogData?.CatalogedIds?.Count ?? 0
                );

                if (throwOnFailure)
                {
                    _catalogData.ThrowIfFailed(
                        $"Downloadable catalog refresh failed for '{url}'.");
                }
            }
            else
            {
                if (_forceRequiredCatalog)
                {
                    _isInitialized = false;
                }

                QuickLog.Warning<DownloadableResourceProvider<ResourceType>>(
                    "Failed to download catalog [gen={0}] from '{1}': {2}",
                    generation, url, webRequest.error ?? "Unknown error"
                );

                if (throwOnFailure)
                {
                    throw new InvalidOperationException(
                        $"Failed to download catalog '{url}': "
                        + (webRequest.error ?? "Unknown error"));
                }
            }
        }

        private bool IsCacheValid(DateTime timestamp)
        {
            if (cacheTTL <= 0f)
            {
                return true;
            }

            return (DateTime.UtcNow - timestamp).TotalSeconds < cacheTTL;
        }

        private bool TryCompleteFromCachedBytes(
            ResourceLoadingHandler<ResourceType> handler,
            byte[] data,
            string source,
            out Exception exception)
        {
            try
            {
                ResourceType resource = ConvertResource(data);
                if (resource == null)
                {
                    throw new InvalidOperationException(
                        $"ConvertResource returned null for '{source}'.");
                }

                handler.Resouce = resource;
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Loaded;
                exception = null;
                return true;
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
                return false;
            }
        }

        private void TouchLru(string key)
        {
            _lruList.Remove(key);
            _lruList.AddLast(key);
        }

        private void EvictFromMemory(string key)
        {
            _memoryCache.Remove(key);
            _lruList.Remove(key);
        }

        private void AddToMemoryCache(string key, byte[] data)
        {
            while (_memoryCache.Count >= MaxCacheEntries
                && _lruList.Count > 0)
            {
                string oldest = _lruList.First.Value;
                _lruList.RemoveFirst();
                _memoryCache.Remove(oldest);
            }

            _memoryCache[key] = (data, DateTime.UtcNow);
            _lruList.AddLast(key);
        }

        private bool TryLoadFromDiskCache(
            string cacheKey,
            string resourceId,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            if (handler.IsCancellationRequested)
            {
                handler.Cancel();
                return true;
            }

            string filePath = GetDiskCachePath(cacheKey);
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);

                if (fileData.Length < 8)
                {
                    DeleteDiskCacheEntry(cacheKey);
                    return false;
                }

                long ticks = BitConverter.ToInt64(fileData, 0);
                DateTime timestamp = new DateTime(ticks, DateTimeKind.Utc);

                if (!IsCacheValid(timestamp))
                {
                    DeleteDiskCacheEntry(cacheKey);
                    return false;
                }

                byte[] data = new byte[fileData.Length - 8];
                Array.Copy(fileData, 8, data, 0, data.Length);

                if (!TryValidateContentHash(
                        resourceId,
                        data,
                        out Exception hashException))
                {
                    QuickLog.Warning<DownloadableResourceProvider<ResourceType>>(
                        "Discarding invalid disk cache entry for '{0}': {1}",
                        resourceId,
                        hashException.Message);
                    DeleteDiskCacheEntry(cacheKey);
                    return false;
                }

                lock (_lock)
                {
                    AddToMemoryCache(cacheKey, data);
                }

                ResourceType resource = ConvertResource(data);
                if (resource == null)
                {
                    DeleteDiskCacheEntry(cacheKey);
                    return false;
                }

                handler.Resouce = resource;
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Loaded;
                return true;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<DownloadableResourceProvider<ResourceType>>(
                    "Failed to load resource '{0}' from disk cache: {1}",
                    resourceId, ex.Message
                );
                DeleteDiskCacheEntry(cacheKey);
                return false;
            }
        }

        private bool StartDownload(
            string url,
            string resourceId,
            string cacheKey,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            ActiveDownload download = new()
            {
                Handlers = new List<ResourceLoadingHandler<ResourceType>> { handler },
                StartTime = Time.realtimeSinceStartup,
                Url = url,
                ResourceId = resourceId,
                CacheKey = cacheKey
            };

            lock (_lock)
            {
                _activeDownloads[cacheKey] = download;
            }

            bool dispatched = Dispatcher.TryDispatchCoroutine(
                CoroutineExceptionGuard.Run(
                    DownloadCoroutine(download),
                    exception => HandleDownloadCoroutineFailure(
                        download,
                        exception)),
                out _);
            if (dispatched)
            {
                return true;
            }

            lock (_lock)
            {
                _activeDownloads.Remove(cacheKey);
            }

            FailHandlers(
                download,
                new InvalidOperationException(
                    "Download requires a Dispatcher instance."));
            return false;
        }

        private IEnumerator DownloadCoroutine(ActiveDownload download)
        {
            using UnityWebRequest webRequest = UnityWebRequest.Get(download.Url);
            webRequest.downloadHandler = new DownloadHandlerBuffer();

            if (headers != null)
            {
                foreach (CustomDownloadHeader header in headers)
                {
                    webRequest.SetRequestHeader(header.key, header.value);
                }
            }

            download.Request = webRequest;

            UnityWebRequestAsyncOperation operation = webRequest.SendWebRequest();

            while (!operation.isDone)
            {
                if (download.IsCanceled)
                {
                    webRequest.Abort();
                    yield break;
                }

                RemoveCanceledHandlers(download);
                if (download.Handlers.Count == 0)
                {
                    webRequest.Abort();
                    lock (_lock)
                    {
                        _activeDownloads.Remove(download.CacheKey);
                    }

                    ProcessQueue();
                    yield break;
                }

                float elapsed = Time.realtimeSinceStartup - download.StartTime;
                download.LastProgress = webRequest.downloadProgress;

                foreach (ResourceLoadingHandler<ResourceType> h in download.Handlers)
                {
                    h.Progress = download.LastProgress;
                }

                if (requestTimeout > 0f && elapsed > requestTimeout && download.LastProgress <= 0f)
                {
                    webRequest.Abort();
                    break;
                }

                yield return null;
            }

            if (download.IsCanceled)
            {
                yield break;
            }

            foreach (ResourceLoadingHandler<ResourceType> h in download.Handlers)
            {
                h.Progress = webRequest.downloadProgress;
            }

            bool timedOut = requestTimeout > 0f
                && (Time.realtimeSinceStartup - download.StartTime) > requestTimeout
                && download.LastProgress <= 0f;

            ProcessDownloadResult(download, webRequest, timedOut);
        }

        private void ProcessDownloadResult(
            ActiveDownload download,
            UnityWebRequest webRequest,
            bool timedOut
        )
        {
            lock (_lock)
            {
                _activeDownloads.Remove(download.CacheKey);
            }

            if (download.IsCanceled)
            {
                ProcessQueue();
                return;
            }

            if (timedOut)
            {
                QuickLog.Warning<DownloadableResourceProvider<ResourceType>>(
                    "Download timed out for '{0}': no response within {1}s.",
                    download.Url, requestTimeout
                );

                FailHandlers(download, new TimeoutException(
                    $"Download timed out for '{download.Url}' after {requestTimeout}s."
                ));
            }
            else if (webRequest.result == UnityWebRequest.Result.Success)
            {
                byte[] data = webRequest.downloadHandler.data;

                if (!TryValidateContentHash(
                        download.ResourceId,
                        data,
                        out Exception hashException))
                {
                    FailHandlers(download, hashException);
                    ProcessQueue();
                    return;
                }

                ResourceType resource;
                try
                {
                    resource = ConvertResource(data);
                    if (resource == null)
                    {
                        throw new InvalidOperationException(
                            $"ConvertResource returned null for "
                            + $"'{download.Url}'.");
                    }
                }
                catch (Exception ex)
                {
                    QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                        "Failed to convert downloaded resource '{0}': {1}",
                        download.Url,
                        ex.Message);
                    FailHandlers(download, ex);
                    ProcessQueue();
                    return;
                }

                try
                {
                    CacheToDisk(
                        download.ResourceId,
                        download.CacheKey,
                        data);
                }
                catch (Exception ex)
                {
                    QuickLog.Warning<DownloadableResourceProvider<ResourceType>>(
                        "Failed to cache resource '{0}' to disk: {1}",
                        download.ResourceId, ex.Message
                    );
                }

                lock (_lock)
                {
                    AddToMemoryCache(download.CacheKey, data);
                }

                CompleteHandlers(download, resource);
            }
            else
            {
                string error = webRequest.error ?? "Unknown network error";

                QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                    "Download failed for '{0}': {1}",
                    download.Url, error
                );

                FailHandlers(download, new InvalidOperationException(
                    $"Download failed for '{download.Url}': {error}"
                ));
            }

            ProcessQueue();
        }

        private void HandleDownloadCoroutineFailure(
            ActiveDownload download,
            Exception exception)
        {
            lock (_lock)
            {
                _activeDownloads.Remove(download.CacheKey);
            }

            download.Request = null;
            FailHandlers(
                download,
                new InvalidOperationException(
                    $"Download coroutine failed for '{download.Url}'.",
                    exception));
            ProcessQueue();
        }

        private void CacheToDisk(
            string resourceId,
            string cacheKey,
            byte[] data)
        {
            string filePath = GetDiskCachePath(cacheKey);
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] fileData = new byte[8 + data.Length];
            BitConverter.GetBytes(DateTime.UtcNow.Ticks).CopyTo(fileData, 0);
            data.CopyTo(fileData, 8);

            DiskCacheIdentity.Write(filePath, resourceId, fileData);
        }

        private void CompleteHandlers(ActiveDownload download, ResourceType resource)
        {
            if (resource == null)
            {
                FailHandlers(
                    download,
                    new InvalidOperationException(
                        $"ConvertResource returned null for '{download.Url}'."));
                return;
            }

            int completedHandlerCount = 0;
            foreach (ResourceLoadingHandler<ResourceType> h in download.Handlers)
            {
                if (h.IsCancellationRequested)
                {
                    h.Cancel();
                    continue;
                }

                h.Resouce = resource;
                h.LoadingStatus = LoadingStatus.Completed;
                h.ResourceStatus = ResourceStatus.Loaded;
                completedHandlerCount++;
            }

            if (completedHandlerCount == 0)
            {
                DestroyRuntimeResource(resource);
                return;
            }

            lock (_lock)
            {
                _resourceLeaseCounts.TryGetValue(
                    resource,
                    out int existingLeaseCount);
                _resourceLeaseCounts[resource]
                    = existingLeaseCount + completedHandlerCount;
            }
        }

        private void FailHandlers(ActiveDownload download, Exception exception)
        {
            foreach (ResourceLoadingHandler<ResourceType> h in download.Handlers)
            {
                if (h.IsCompleted)
                {
                    continue;
                }

                if (exception is OperationCanceledException)
                {
                    h.Cancel();
                    h.Exception = exception;
                    continue;
                }

                if (h.IsCancellationRequested)
                {
                    h.Cancel();
                    continue;
                }

                h.LoadingStatus = LoadingStatus.Completed;
                h.ResourceStatus = exception is OperationCanceledException
                    ? ResourceStatus.Canceled
                    : ResourceStatus.Failed;
                h.Exception = exception;
            }
        }

        private void ProcessQueue()
        {
            lock (_lock)
            {
                while (_pendingQueue.Count > 0
                    && _activeDownloads.Count < MaxConcurrentDownloads)
                {
                    DownloadRequest request = _pendingQueue.Dequeue();

                    if (request.Handler.IsCancellationRequested)
                    {
                        request.Handler.Cancel();
                        continue;
                    }

                    if (_activeDownloads.TryGetValue(
                            request.CacheKey,
                            out ActiveDownload existing))
                    {
                        existing.Handlers.Add(request.Handler);
                        continue;
                    }

                    StartDownload(
                        request.Url,
                        request.ResourceId,
                        request.CacheKey,
                        request.Handler);
                }
            }
        }

        private static void RemoveCanceledHandlers(ActiveDownload download)
        {
            for (int i = download.Handlers.Count - 1; i >= 0; i--)
            {
                ResourceLoadingHandler<ResourceType> handler
                    = download.Handlers[i];
                if (!handler.IsCancellationRequested)
                {
                    continue;
                }

                handler.Cancel();
                download.Handlers.RemoveAt(i);
            }
        }

        private string GetDiskCachePath(string resourceId)
        {
            string cacheRoot = GetDiskCacheRoot();
            string fileName = ComputeStableCacheFileName(resourceId);
            return Path.Combine(cacheRoot, fileName);
        }

        private string GetDiskCacheRoot()
        {
            string basePath = _cachedDiskBasePath
                ?? (cacheBasePath == CacheBasePathType.PersistentDataPath
                    ? Application.persistentDataPath
                    : Application.temporaryCachePath);
            string subFolder = string.IsNullOrWhiteSpace(cacheSubFolder)
                ? "downloadable_cache"
                : cacheSubFolder.Trim();
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
                    "Download cache subfolder must remain inside the selected "
                    + "cache base path.");
            }

            return cacheRoot;
        }

        private bool TryValidateContentHash(
            string resourceId,
            byte[] data,
            out Exception exception)
        {
            return CatalogContentHash.TryValidate(
                resourceId,
                data,
                _catalogData?.GetContentHash(resourceId),
                out exception);
        }

        private void DeleteDiskCacheEntry(string cacheKey)
        {
            try
            {
                string filePath = GetDiskCachePath(cacheKey);
                DiskCacheIdentity.Delete(filePath);
            }
            catch (Exception exception)
            {
                QuickLog.Warning<DownloadableResourceProvider<ResourceType>>(
                    "Failed to delete disk cache entry '{0}': {1}",
                    cacheKey,
                    exception.Message);
            }
        }

        private static string ComputeStableCacheFileName(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return "_empty";
            }

            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(cacheKey));
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private string BuildCacheKey(string resourceId, string url)
        {
            string contentHash = _catalogData?.GetContentHash(resourceId)
                ?? string.Empty;
            return $"{resourceId ?? string.Empty}\n{url ?? string.Empty}\n{contentHash}";
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

        public void ClearCache()
        {
            CancelAllRequests(
                new OperationCanceledException(
                    "Download requests were canceled because the cache was cleared."));

            lock (_lock)
            {
                _memoryCache.Clear();
                _lruList.Clear();
                _cacheKeysByResourceId.Clear();
            }

            try
            {
                string folder = GetDiskCacheRoot();

                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception ex)
            {
                QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                    "Failed to clear disk cache: {0}",
                    ex.Message
                );
            }

            QuickLog.Debug<DownloadableResourceProvider<ResourceType>>("Cache cleared.");
        }

        public void ClearCache(string resourceId)
        {
            CancelRequestsForResource(
                resourceId,
                new OperationCanceledException(
                    $"Download for '{resourceId}' was canceled because its "
                    + "cache entry was cleared."
                )
            );

            string[] cacheKeys;
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
                    EvictFromMemory(cacheKeys[i]);
                }
            }

            for (int i = 0; i < cacheKeys.Length; i++)
            {
                DeleteDiskCacheEntry(cacheKeys[i]);
            }

            DeletePersistedCacheEntries(resourceId);
        }

        public virtual void ReleaseResource(ResourceType resource)
        {
            if (resource == null)
            {
                return;
            }

            bool shouldDestroy = true;
            lock (_lock)
            {
                if (_resourceLeaseCounts.TryGetValue(
                        resource,
                        out int leaseCount))
                {
                    if (leaseCount > 1)
                    {
                        _resourceLeaseCounts[resource] = leaseCount - 1;
                        shouldDestroy = false;
                    }
                    else
                    {
                        _resourceLeaseCounts.Remove(resource);
                    }
                }
            }

            if (shouldDestroy)
            {
                DestroyRuntimeResource(resource);
            }
        }

        private void DeletePersistedCacheEntries(string resourceId)
        {
            try
            {
                string cacheRoot = GetDiskCacheRoot();
                if (!Directory.Exists(cacheRoot))
                {
                    return;
                }

                DiskCacheIdentity.DeleteByResourceId(
                    cacheRoot,
                    resourceId);
            }
            catch (Exception exception)
            {
                QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                    "Failed to clear persisted cache for '{0}': {1}",
                    resourceId,
                    exception.Message);
            }
        }

        protected virtual void OnDisable()
        {
            _catalogGeneration++;
            AbortActiveCatalogRequest();

            CancelAllRequests(
                new OperationCanceledException(
                    "Download provider was disabled."));
            ReleaseAllLeasedResources();
            _isInitialized = false;
            InitializationException = null;
        }

        private void HandleInitializationFailure(
            uint generation,
            Exception exception)
        {
            if (generation != _catalogGeneration)
            {
                return;
            }

            InitializationException = exception;
            _isInitialized = false;
            QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                "Downloadable provider initialization failed: {0}",
                exception);
        }

        private void AbortActiveCatalogRequest()
        {
            if (_activeCatalogRequest == null)
            {
                return;
            }

            QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                "Aborting in-flight catalog request."
            );
            try
            {
                _activeCatalogRequest.Abort();
            }
            catch
            {
            }

            _activeCatalogRequest = null;
        }

        private static void HandleCatalogInvalidationFailure(
            Exception exception)
        {
            QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                "Downloadable catalog invalidation failed: {0}",
                exception);
        }

        private static void DestroyRuntimeResource(ResourceType resource)
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

        private void ReleaseAllLeasedResources()
        {
            List<ResourceType> resources;
            lock (_lock)
            {
                resources = new List<ResourceType>(_resourceLeaseCounts.Keys);
                _resourceLeaseCounts.Clear();
            }

            for (int i = 0; i < resources.Count; i++)
            {
                DestroyRuntimeResource(resources[i]);
            }
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            timeout = Mathf.Max(0.1f, timeout);
            requestTimeout = Mathf.Max(0.1f, requestTimeout);
            maxConcurrentDownloads = Mathf.Max(1, maxConcurrentDownloads);
            maxCacheEntries = Mathf.Max(1, maxCacheEntries);
            cacheTTL = Mathf.Max(0f, cacheTTL);
            if (string.IsNullOrWhiteSpace(cacheSubFolder))
            {
                cacheSubFolder = "downloadable_cache";
            }

            if (string.IsNullOrWhiteSpace(_urlFormat))
            {
                _urlFormat = "{0}";
            }
        }
#endif

        private void CancelAllRequests(Exception exception)
        {
            List<ActiveDownload> activeDownloads;
            List<DownloadRequest> pendingRequests;

            lock (_lock)
            {
                activeDownloads = new List<ActiveDownload>(
                    _activeDownloads.Values);
                pendingRequests = new List<DownloadRequest>(_pendingQueue);
                _activeDownloads.Clear();
                _pendingQueue.Clear();
            }

            foreach (ActiveDownload download in activeDownloads)
            {
                download.IsCanceled = true;
                try
                {
                    download.Request?.Abort();
                }
                catch
                {
                }

                FailHandlers(download, exception);
            }

            foreach (DownloadRequest request in pendingRequests)
            {
                if (request.Handler.IsCancellationRequested)
                {
                    request.Handler.Cancel();
                    continue;
                }

                request.Handler.LoadingStatus = LoadingStatus.Completed;
                request.Handler.ResourceStatus = ResourceStatus.Canceled;
                request.Handler.Exception = exception;
            }
        }

        private void CancelRequestsForResource(
            string resourceId,
            Exception exception)
        {
            List<ActiveDownload> activeDownloads
                = new List<ActiveDownload>();
            List<DownloadRequest> pendingRequests
                = new List<DownloadRequest>();

            lock (_lock)
            {
                List<string> activeKeys = new List<string>();
                foreach (KeyValuePair<string, ActiveDownload> pair
                    in _activeDownloads)
                {
                    if (!string.Equals(
                            pair.Value.ResourceId,
                            resourceId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    pair.Value.IsCanceled = true;
                    activeDownloads.Add(pair.Value);
                    activeKeys.Add(pair.Key);
                }

                for (int i = 0; i < activeKeys.Count; i++)
                {
                    _activeDownloads.Remove(activeKeys[i]);
                }

                int pendingCount = _pendingQueue.Count;
                for (int i = 0; i < pendingCount; i++)
                {
                    DownloadRequest request = _pendingQueue.Dequeue();
                    if (string.Equals(
                            request.ResourceId,
                            resourceId,
                            StringComparison.Ordinal))
                    {
                        pendingRequests.Add(request);
                    }
                    else
                    {
                        _pendingQueue.Enqueue(request);
                    }
                }
            }

            foreach (ActiveDownload download in activeDownloads)
            {
                try
                {
                    download.Request?.Abort();
                }
                catch
                {
                }

                FailHandlers(download, exception);
            }

            foreach (DownloadRequest request in pendingRequests)
            {
                request.Handler.Cancel();
                request.Handler.Exception = exception;
            }

            ProcessQueue();
        }
    }
}
