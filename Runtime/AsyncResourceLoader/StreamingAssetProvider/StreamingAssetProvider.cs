using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [ResourceProvider(
        "Streaming Assets",
        "Loads from Application.streamingAssetsPath via UnityWebRequest. "
        + "Override ConvertResource(byte[])."
    )]
    public abstract class StreamingAssetProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>,
        IStreamingAssetProvider<ResourceType>,
        ICatalogAwareAsyncResourceProvider,
        IInvalidatableCatalog,
        IAsyncResourceCacheKeyProvider,
        IAsyncResourceDataTypeResolver,
        IAsyncResourceInitializationStatus,
        IAsyncResourceReleaseProvider<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        public int Priority => priority;
        public float ResourceLoadingTimeout => timeout > 0f ? timeout : 30f;
        public bool IsInitialized { get; private set; }
        public Exception InitializationException { get; private set; }
        public string SubFolder => subFolder;

        [SerializeField]
        [Tooltip("Subfolder relative to Application.streamingAssetsPath. "
            + "Leave empty to read from the root.")]
        private string subFolder;

        [SerializeField]
        [Tooltip("Lower values load first (cascade fallback).")]
        private int priority;

        [SerializeField]
        [Tooltip("Maximum time in seconds to wait for a single load request.")]
        private float timeout = 30f;

        [SerializeField]
        [Tooltip("Request timeout in seconds. Aborts if no progress is made "
            + "within this duration.")]
        private float requestTimeout = 15f;

        [SerializeField]
        [Tooltip("When enabled, loads a catalog JSON file to determine which resources this provider can serve.")]
        private CatalogConfig _catalogConfig = new CatalogConfig();

        private CatalogData _catalogData;
        private readonly object _operationLock = new object();
        private readonly HashSet<ResourceLoadingHandler<ResourceType>>
            _activeHandlers
                = new HashSet<ResourceLoadingHandler<ResourceType>>();
        private int _operationGeneration;

        /// <summary>
        /// Override to convert raw bytes into the target resource type.
        /// </summary>
        protected abstract ResourceType ConvertResource(byte[] data);

        public virtual void Initialize()
        {
            int operationGeneration = AdvanceOperationGeneration(
                "StreamingAsset provider was reinitialized.");
            IsInitialized = false;
            InitializationException = null;
            bool dispatched = Dispatcher.TryDispatchCoroutine(
                CoroutineExceptionGuard.Run(
                    InitializeCoroutine(operationGeneration),
                    exception => HandleInitializationFailure(
                        operationGeneration,
                        exception)),
                out _);
            if (!dispatched)
            {
                HandleInitializationFailure(
                    operationGeneration,
                    new InvalidOperationException(
                        "StreamingAsset provider initialization requires a Dispatcher."
                    ));
            }
        }

        private IEnumerator InitializeCoroutine(int operationGeneration)
        {
            CatalogData catalogData = new CatalogData();
            if (_catalogConfig.UseCatalog
                && !string.IsNullOrEmpty(_catalogConfig.CatalogFileName))
            {
                yield return catalogData.LoadFromStreamingAssetsCoroutine(
                    _catalogConfig.CatalogFileName);
                catalogData.ThrowIfFailed(
                    "StreamingAssets catalog initialization failed.");
            }

            if (!IsCurrentOperationGeneration(operationGeneration))
            {
                yield break;
            }

            string fullPath = BuildBasePath();
            if (!RequiresUnityWebRequest(fullPath)
                && !Directory.Exists(fullPath))
            {
                QuickLog.Warning<StreamingAssetProvider<ResourceType>>(
                    "StreamingAssets subfolder '{0}' not found at '{1}'. "
                    + "Provider initialized but may fail to find resources.",
                    subFolder, fullPath
                );
            }

            _catalogData = catalogData;
            IsInitialized = true;

            QuickLog.Debug<StreamingAssetProvider<ResourceType>>(
                "StreamingAsset provider initialized for '{0}'.",
                subFolder ?? "<root>"
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

            if (!IsInitialized)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new InvalidOperationException(
                    "StreamingAsset provider is not initialized."
                );
                return;
            }

            if (id is not IStreamingAssetId streamingId)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentException(
                    "Invalid resource ID type. Expected IStreamingAssetId."
                );
                return;
            }

            if (string.IsNullOrEmpty(id.ResourceId))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentException(
                    "Resource ID is null or empty.",
                    nameof(id)
                );
                return;
            }

            string filePath = ResolveRelativePath(id, streamingId);
            if (string.IsNullOrEmpty(filePath))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentException(
                    "File path returned by resource ID is null or empty.",
                    nameof(id)
                );
                return;
            }

            string fullPath = BuildFullPath(filePath);

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;
            int operationGeneration;
            lock (_operationLock)
            {
                operationGeneration = _operationGeneration;
                _activeHandlers.Add(handler);
            }

            QuickLog.Debug<StreamingAssetProvider<ResourceType>>(
                "Loading StreamingAsset '{0}'...", fullPath
            );

            bool dispatched = Dispatcher.TryDispatchCoroutine(
                CoroutineExceptionGuard.Run(
                    LoadFromFileCoroutine(
                        id.ResourceId,
                        fullPath,
                        handler,
                        operationGeneration),
                    exception => HandleLoadCoroutineFailure(
                        handler,
                        fullPath,
                        exception)),
                out _);
            if (!dispatched)
            {
                RemoveActiveHandler(handler);
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new InvalidOperationException(
                    "StreamingAsset loading requires a Dispatcher instance.");
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

            if (_catalogData != null && _catalogData.IsLoaded)
            {
                return _catalogData.HasResource(resourceId.ResourceId);
            }

            return true;
        }

        public DataType GetDataType(string resourceId) =>
            _catalogData?.GetDataType(resourceId) ?? DataType.Unknown;

        public DataType GetDataType(IAsyncResourceId resourceId)
        {
            return GetDataType(resourceId?.ResourceId);
        }

        public string GetCacheKey(IAsyncResourceId resourceId)
        {
            if (resourceId is not IStreamingAssetId streamingId)
            {
                return resourceId?.ResourceId;
            }

            string cacheKey = BuildFullPath(
                ResolveRelativePath(resourceId, streamingId));
            string contentHash = _catalogData?.GetContentHash(
                resourceId.ResourceId);
            return string.IsNullOrWhiteSpace(contentHash)
                ? cacheKey
                : $"{cacheKey}\n{contentHash}";
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
                QuickLog.Error<StreamingAssetProvider<ResourceType>>(
                    "Catalog invalidation requires a Dispatcher instance.");
            }
        }

        public IEnumerator InvalidateCatalogCoroutine(
            CatalogInvalidationMode mode)
        {
            int operationGeneration = AdvanceOperationGeneration(
                "StreamingAsset catalog was invalidated.");
            CatalogData catalogData = new CatalogData();

            if (_catalogConfig.UseCatalog
                && !string.IsNullOrEmpty(_catalogConfig.CatalogFileName))
            {
                yield return catalogData.LoadFromStreamingAssetsCoroutine(
                    _catalogConfig.CatalogFileName);
                catalogData.ThrowIfFailed(
                    "StreamingAssets catalog refresh failed.");
            }

            if (!IsCurrentOperationGeneration(operationGeneration))
            {
                yield break;
            }

            _catalogData = catalogData;
        }

        public virtual void ReleaseResource(ResourceType resource)
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

        private IEnumerator LoadFromFileCoroutine(
            string resourceId,
            string fullPath,
            ResourceLoadingHandler<ResourceType> handler,
            int operationGeneration
        )
        {
            IEnumerator operation = LoadFromFileInternalCoroutine(
                resourceId,
                fullPath,
                handler,
                operationGeneration);
            try
            {
                while (operation.MoveNext())
                {
                    yield return operation.Current;
                }
            }
            finally
            {
                (operation as IDisposable)?.Dispose();
                RemoveActiveHandler(handler);
            }
        }

        private IEnumerator LoadFromFileInternalCoroutine(
            string resourceId,
            string fullPath,
            ResourceLoadingHandler<ResourceType> handler,
            int operationGeneration)
        {
            using UnityWebRequest webRequest = UnityWebRequest.Get(fullPath);
            webRequest.downloadHandler = new DownloadHandlerBuffer();

            UnityWebRequestAsyncOperation operation =
                webRequest.SendWebRequest();

            float startTime = Time.realtimeSinceStartup;

            while (!operation.isDone)
            {
                if (handler.IsCancellationRequested
                    || !IsCurrentOperationGeneration(operationGeneration))
                {
                    webRequest.Abort();
                    handler.Cancel();
                    yield break;
                }

                handler.Progress = webRequest.downloadProgress;

                float elapsed = Time.realtimeSinceStartup - startTime;
                if (requestTimeout > 0f
                    && elapsed > requestTimeout
                    && webRequest.downloadProgress <= 0f)
                {
                    webRequest.Abort();
                    break;
                }

                yield return null;
            }

            if (handler.IsCancellationRequested
                || !IsCurrentOperationGeneration(operationGeneration))
            {
                webRequest.Abort();
                handler.Cancel();
                yield break;
            }

            handler.Progress = webRequest.downloadProgress;

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                byte[] data = webRequest.downloadHandler.data;

                if (!TryValidateContentHash(
                        resourceId,
                        data,
                        out Exception hashException))
                {
                    handler.LoadingStatus = LoadingStatus.Completed;
                    handler.ResourceStatus = ResourceStatus.Failed;
                    handler.Exception = hashException;
                    yield break;
                }

                try
                {
                    ResourceType resource = ConvertResource(data);

                    if (resource != null)
                    {
                        if (handler.IsCancellationRequested
                            || !IsCurrentOperationGeneration(
                                operationGeneration))
                        {
                            ReleaseResource(resource);
                            handler.Cancel();
                            yield break;
                        }

                        handler.Resouce = resource;
                        handler.LoadingStatus = LoadingStatus.Completed;
                        handler.ResourceStatus = ResourceStatus.Loaded;

                        QuickLog.Info<StreamingAssetProvider<ResourceType>>(
                            "StreamingAsset '{0}' loaded successfully.",
                            fullPath
                        );
                    }
                    else
                    {
                        handler.LoadingStatus = LoadingStatus.Completed;
                        handler.ResourceStatus = ResourceStatus.Failed;
                        handler.Exception = new InvalidOperationException(
                            $"ConvertResource returned null for '{fullPath}'. "
                            + "The data may be malformed or not of the "
                            + $"expected type ({typeof(ResourceType).Name})."
                        );

                        QuickLog.Warning<StreamingAssetProvider<ResourceType>>(
                            "ConvertResource returned null for '{0}'.",
                            fullPath
                        );
                    }
                }
                catch (Exception ex)
                {
                    handler.LoadingStatus = LoadingStatus.Completed;
                    handler.ResourceStatus = ResourceStatus.Failed;
                    handler.Exception = ex;

                    QuickLog.Error<StreamingAssetProvider<ResourceType>>(
                        "ConvertResource threw for '{0}': {1}",
                        fullPath, ex.Message
                    );
                }
            }
            else
            {
                string error = webRequest.error ?? "Unknown error";

                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new InvalidOperationException(
                    $"Failed to load StreamingAsset '{fullPath}': {error}"
                );

                QuickLog.Error<StreamingAssetProvider<ResourceType>>(
                    "Failed to load StreamingAsset '{0}': {1}",
                    fullPath, error
                );
            }
        }

        private string BuildBasePath()
        {
            string basePath = Application.streamingAssetsPath;
            if (string.IsNullOrEmpty(subFolder))
            {
                return basePath;
            }

            return CombinePath(basePath, subFolder);
        }

        private string BuildFullPath(string relativePath)
        {
            return CombinePath(BuildBasePath(), relativePath);
        }

        private void HandleInitializationFailure(
            int operationGeneration,
            Exception exception)
        {
            if (!IsCurrentOperationGeneration(operationGeneration))
            {
                return;
            }

            InitializationException = exception;
            IsInitialized = false;
            QuickLog.Error<StreamingAssetProvider<ResourceType>>(
                "StreamingAsset provider initialization failed: {0}",
                exception);
        }

        private void OnDisable()
        {
            AdvanceOperationGeneration(
                "StreamingAsset provider was disabled.");
            IsInitialized = false;
            InitializationException = null;
        }

        private int AdvanceOperationGeneration(string cancellationReason)
        {
            ResourceLoadingHandler<ResourceType>[] handlers;
            int operationGeneration;
            lock (_operationLock)
            {
                unchecked
                {
                    _operationGeneration++;
                }

                operationGeneration = _operationGeneration;
                handlers = new ResourceLoadingHandler<ResourceType>[
                    _activeHandlers.Count];
                _activeHandlers.CopyTo(handlers);
                _activeHandlers.Clear();
            }

            OperationCanceledException exception
                = new OperationCanceledException(cancellationReason);
            for (int i = 0; i < handlers.Length; i++)
            {
                if (!handlers[i].IsCompleted)
                {
                    handlers[i].Cancel();
                    handlers[i].Exception = exception;
                }
            }

            return operationGeneration;
        }

        private bool IsCurrentOperationGeneration(int operationGeneration)
        {
            lock (_operationLock)
            {
                return operationGeneration == _operationGeneration;
            }
        }

        private void RemoveActiveHandler(
            ResourceLoadingHandler<ResourceType> handler)
        {
            lock (_operationLock)
            {
                _activeHandlers.Remove(handler);
            }
        }

        private static void HandleLoadCoroutineFailure(
            ResourceLoadingHandler<ResourceType> handler,
            string fullPath,
            Exception exception)
        {
            if (handler.IsCompleted)
            {
                return;
            }

            if (handler.IsCancellationRequested)
            {
                handler.Cancel();
                return;
            }

            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Failed;
            handler.Exception = new InvalidOperationException(
                $"StreamingAsset load coroutine failed for '{fullPath}'.",
                exception);
        }

        private static void HandleCatalogInvalidationFailure(
            Exception exception)
        {
            QuickLog.Error<StreamingAssetProvider<ResourceType>>(
                "StreamingAssets catalog invalidation failed: {0}",
                exception);
        }

        private string ResolveRelativePath(
            IAsyncResourceId resourceId,
            IStreamingAssetId streamingId)
        {
            string catalogPath = _catalogData?.GetRelativePath(
                resourceId?.ResourceId);
            return string.IsNullOrWhiteSpace(catalogPath)
                ? streamingId.GetFilePath(this)
                : catalogPath;
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            timeout = Mathf.Max(0.1f, timeout);
            requestTimeout = Mathf.Max(0.1f, requestTimeout);
            subFolder = subFolder?.Trim().Trim('/', '\\');
        }
#endif

        private static bool RequiresUnityWebRequest(string path)
        {
            return path.IndexOf("://", StringComparison.Ordinal) >= 0;
        }

        private static string CombinePath(string basePath, string relativePath)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                return relativePath;
            }

            if (RequiresUnityWebRequest(basePath))
            {
                return basePath.TrimEnd('/')
                    + "/"
                    + (relativePath ?? string.Empty).TrimStart('/');
            }

            return Path.Combine(basePath, relativePath ?? string.Empty);
        }
    }
}
