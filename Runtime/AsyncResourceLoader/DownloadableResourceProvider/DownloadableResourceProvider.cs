using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        IDownloadableResourceProvider<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        [SerializeField]
        private int priority;

        [SerializeField]
        private float timeout;

        [SerializeField]
        private float requestTimeout = 30f;

        [SerializeField]
        private int maxConcurrentDownloads = 5;

        [SerializeField]
        private int maxCacheEntries = 50;

        [SerializeField]
        private float cacheTTL;

        [SerializeField]
        private CacheBasePathType cacheBasePath = CacheBasePathType.PersistentDataPath;

        [SerializeField]
        private string cacheSubFolder = "downloadable_cache";

        [SerializeField]
        private CustomDownloadHeader[] headers;

        private volatile bool _isInitialized;
        private string _cachedDiskBasePath;
        private readonly Dictionary<string, (byte[] data, DateTime timestamp)> _memoryCache = new();
        private readonly LinkedList<string> _lruList = new();
        private readonly Dictionary<string, ActiveDownload> _activeDownloads = new();
        private readonly Queue<DownloadRequest> _pendingQueue = new();
        private readonly object _lock = new();

        private class DownloadRequest
        {
            public string Url;
            public string ResourceId;
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
        }

        public int Priority => priority;
        public bool IsInitialized => _isInitialized;
        public float ResourceLoadingTimeout => timeout;
        public float RequestTimeout => requestTimeout;
        public float CacheTTL => cacheTTL;
        public int MaxConcurrentDownloads => maxConcurrentDownloads;
        public int MaxCacheEntries => maxCacheEntries;
        public string CacheSubFolder => cacheSubFolder;
        public CacheBasePathType CacheBasePath => cacheBasePath;
        public CustomDownloadHeader[] Headers => headers;

        protected abstract ResourceType ConvertResource(byte[] data);

        public virtual void Initialize()
        {
            _cachedDiskBasePath = cacheBasePath == CacheBasePathType.PersistentDataPath
                ? Application.persistentDataPath
                : Application.temporaryCachePath;
            _isInitialized = true;

            QuickLog.Debug<DownloadableResourceProvider<ResourceType>>(
                "Downloadable resource provider initialized."
            );
        }

        public void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
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

            string url = downloadableId.GetUrl(this);
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

            lock (_lock)
            {
                if (_memoryCache.TryGetValue(resourceId, out var cached))
                {
                    if (IsCacheValid(cached.timestamp))
                    {
                        TouchLru(resourceId);

                        ResourceType resource = ConvertResource(cached.data);
                        handler.Resouce = resource;
                        handler.LoadingStatus = LoadingStatus.Completed;
                        handler.ResourceStatus = ResourceStatus.Loaded;
                        return;
                    }

                    EvictFromMemory(resourceId);
                }
            }

            if (TryLoadFromDiskCache(resourceId, handler))
            {
                return;
            }

            lock (_lock)
            {
                if (_activeDownloads.TryGetValue(resourceId, out var existing))
                {
                    existing.Handlers.Add(handler);
                    return;
                }

                if (_activeDownloads.Count >= maxConcurrentDownloads)
                {
                    _pendingQueue.Enqueue(new DownloadRequest
                    {
                        Url = url,
                        ResourceId = resourceId,
                        Handler = handler
                    });
                    return;
                }
            }

            StartDownload(url, resourceId, handler);
        }

        private bool IsCacheValid(DateTime timestamp)
        {
            if (cacheTTL <= 0f)
            {
                return true;
            }

            return (DateTime.UtcNow - timestamp).TotalSeconds < cacheTTL;
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
            while (_memoryCache.Count >= maxCacheEntries && _lruList.Count > 0)
            {
                string oldest = _lruList.First.Value;
                _lruList.RemoveFirst();
                _memoryCache.Remove(oldest);
            }

            _memoryCache[key] = (data, DateTime.UtcNow);
            _lruList.AddLast(key);
        }

        private bool TryLoadFromDiskCache(
            string resourceId,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            string filePath = GetDiskCachePath(resourceId);
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);

                if (fileData.Length < 8)
                {
                    File.Delete(filePath);
                    return false;
                }

                long ticks = BitConverter.ToInt64(fileData, 0);
                DateTime timestamp = new DateTime(ticks, DateTimeKind.Utc);

                if (!IsCacheValid(timestamp))
                {
                    File.Delete(filePath);
                    return false;
                }

                byte[] data = new byte[fileData.Length - 8];
                Array.Copy(fileData, 8, data, 0, data.Length);

                lock (_lock)
                {
                    AddToMemoryCache(resourceId, data);
                }

                ResourceType resource = ConvertResource(data);
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
                try { File.Delete(filePath); } catch { }
                return false;
            }
        }

        private void StartDownload(
            string url,
            string resourceId,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            ActiveDownload download = new()
            {
                Handlers = new List<ResourceLoadingHandler<ResourceType>> { handler },
                StartTime = Time.realtimeSinceStartup,
                Url = url,
                ResourceId = resourceId
            };

            lock (_lock)
            {
                _activeDownloads[resourceId] = download;
            }

            Dispatcher.DispatchCoroutine(DownloadCoroutine(download));
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
                _activeDownloads.Remove(download.ResourceId);
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

                try
                {
                    CacheToDisk(download.ResourceId, data);
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
                    AddToMemoryCache(download.ResourceId, data);
                }

                try
                {
                    ResourceType resource = ConvertResource(data);
                    CompleteHandlers(download, resource);
                }
                catch (Exception ex)
                {
                    QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                        "Failed to convert downloaded resource '{0}': {1}",
                        download.Url, ex.Message
                    );

                    FailHandlers(download, ex);
                }
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

        private void CacheToDisk(string resourceId, byte[] data)
        {
            string filePath = GetDiskCachePath(resourceId);
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] fileData = new byte[8 + data.Length];
            BitConverter.GetBytes(DateTime.UtcNow.Ticks).CopyTo(fileData, 0);
            data.CopyTo(fileData, 8);

            File.WriteAllBytes(filePath, fileData);
        }

        private void CompleteHandlers(ActiveDownload download, ResourceType resource)
        {
            foreach (ResourceLoadingHandler<ResourceType> h in download.Handlers)
            {
                h.Resouce = resource;
                h.LoadingStatus = LoadingStatus.Completed;
                h.ResourceStatus = ResourceStatus.Loaded;
            }
        }

        private void FailHandlers(ActiveDownload download, Exception exception)
        {
            foreach (ResourceLoadingHandler<ResourceType> h in download.Handlers)
            {
                h.LoadingStatus = LoadingStatus.Completed;
                h.ResourceStatus = ResourceStatus.Failed;
                h.Exception = exception;
            }
        }

        private void ProcessQueue()
        {
            lock (_lock)
            {
                while (_pendingQueue.Count > 0 && _activeDownloads.Count < maxConcurrentDownloads)
                {
                    DownloadRequest request = _pendingQueue.Dequeue();

                    if (_activeDownloads.TryGetValue(request.ResourceId, out var existing))
                    {
                        existing.Handlers.Add(request.Handler);
                        continue;
                    }

                    StartDownload(request.Url, request.ResourceId, request.Handler);
                }
            }
        }

        private string GetDiskCachePath(string resourceId)
        {
            string basePath = _cachedDiskBasePath
                ?? (cacheBasePath == CacheBasePathType.PersistentDataPath
                    ? Application.persistentDataPath
                    : Application.temporaryCachePath);

            string sanitized = SanitizeFileName(resourceId);
            return Path.Combine(basePath, cacheSubFolder, sanitized);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "_empty";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        public void ClearCache()
        {
            lock (_lock)
            {
                _memoryCache.Clear();
                _lruList.Clear();
            }

            try
            {
                string folder = Path.Combine(
                    _cachedDiskBasePath
                        ?? (cacheBasePath == CacheBasePathType.PersistentDataPath
                            ? Application.persistentDataPath
                            : Application.temporaryCachePath),
                    cacheSubFolder
                );

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
            lock (_lock)
            {
                EvictFromMemory(resourceId);
            }

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
                QuickLog.Error<DownloadableResourceProvider<ResourceType>>(
                    "Failed to clear cached resource '{0}': {1}",
                    resourceId, ex.Message
                );
            }
        }

        protected virtual void OnDisable()
        {
            lock (_lock)
            {
                foreach (ActiveDownload download in _activeDownloads.Values)
                {
                    try
                    {
                        download.Request?.Abort();
                    }
                    catch
                    {
                    }
                }

                _activeDownloads.Clear();
                _pendingQueue.Clear();
            }
        }
    }
}
