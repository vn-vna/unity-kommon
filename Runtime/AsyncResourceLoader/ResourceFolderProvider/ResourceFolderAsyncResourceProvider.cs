using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [ResourceProvider(
        "Resources Folder",
        "Loads from Unity Resources using Resources.LoadAsync."
    )]
    public class ResourceFolderAsyncResourceProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>, 
        IResourceFolderAsyncResourceProvider<ResourceType>,
        ICatalogAwareAsyncResourceProvider,
        IInvalidatableCatalog,
        IAsyncResourceCacheKeyProvider,
        IAsyncResourceDataTypeResolver,
        IAsyncResourceInitializationStatus
        where ResourceType : UnityEngine.Object
    {
        public bool IsInitialized => _isInitialized;
        public Exception InitializationException { get; private set; }
        public int Priority => priority;
        public float ResourceLoadingTimeout => timeout > 0f ? timeout : 30f;
        public string FolderName => folderName;

        [SerializeField]
        private string folderName;

        [SerializeField]
        private int priority;

        [SerializeField]
        [Min(0.1f)]
        private float timeout = 30f;

        [SerializeField]
        [Tooltip("When enabled, loads a catalog JSON file to determine which resources this provider can serve.")]
        private CatalogConfig _catalogConfig = new CatalogConfig();

        private volatile bool _isInitialized;
        private CatalogData _catalogData;
        private int _operationGeneration;

        public void Initialize()
        {
            int operationGeneration = AdvanceOperationGeneration();
            _isInitialized = false;
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
                        "Resource folder provider initialization requires a Dispatcher."
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
                    "Resources catalog initialization failed.");
            }

            if (!IsCurrentOperationGeneration(operationGeneration))
            {
                yield break;
            }

            _catalogData = catalogData;
            _isInitialized = true;

            QuickLog.Debug<ResourceFolderAsyncResourceProvider<ResourceType>>(
                "Resource folder provider initialized for folder '{0}'.",
                folderName
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
                    "Resource folder provider is not initialized."
                );
                return;
            }

            if (id is not IResourceFolderAsyncResourceId rfarid)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new ArgumentException(
                    "Invalid Id for Resource File"
                );
                return;
            }

            if (id == null || string.IsNullOrEmpty(id.ResourceId))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new ArgumentNullException(
                    nameof(id), "Resource ID is null or empty."
                );
                handler.ProviderSource = GetType().Name;

                QuickLog.Error<ResourceFolderAsyncResourceProvider<ResourceType>>(
                    "Resource ID is null or empty. Cannot load resource from folder '{0}'.",
                    folderName
                );
                return;
            }

            string fullPath = ResolveResourcePath(id, rfarid);

            QuickLog.Debug<ResourceFolderAsyncResourceProvider<ResourceType>>(
                "Attempting to load resource '{0}' from folder '{1}'.",
                fullPath, folderName
            );

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;
            int operationGeneration = _operationGeneration;

            try
            {
                ResourceRequest request = Resources.LoadAsync<ResourceType>(fullPath);
                request.completed += _ =>
                {
                    ResourceType resource = request.asset as ResourceType;
                    if (handler.IsCancellationRequested
                        || !IsCurrentOperationGeneration(operationGeneration))
                    {
                        handler.Cancel();
                        return;
                    }

                    if (resource != null)
                    {
                        HandleResourceFound(handler, fullPath, resource);
                    }
                    else
                    {
                        HandleResourceNotFound(handler, fullPath);
                    }
                };
            }
            catch (Exception ex)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = ex;

                QuickLog.Error<ResourceFolderAsyncResourceProvider<ResourceType>>(
                    "Exception while starting async load for '{0}': {1}",
                    fullPath, ex.Message
                );
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
            string cacheKey = resourceId is IResourceFolderAsyncResourceId folderId
                ? ResolveResourcePath(resourceId, folderId)
                : resourceId?.ResourceId;
            string contentHash = _catalogData?.GetContentHash(
                resourceId?.ResourceId);
            return string.IsNullOrWhiteSpace(contentHash)
                ? cacheKey
                : $"{cacheKey ?? string.Empty}\n{contentHash}";
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
                QuickLog.Error<ResourceFolderAsyncResourceProvider<ResourceType>>(
                    "Catalog invalidation requires a Dispatcher instance.");
            }
        }

        public IEnumerator InvalidateCatalogCoroutine(
            CatalogInvalidationMode mode)
        {
            int operationGeneration = AdvanceOperationGeneration();
            CatalogData catalogData = new CatalogData();

            if (_catalogConfig.UseCatalog
                && !string.IsNullOrEmpty(_catalogConfig.CatalogFileName))
            {
                yield return catalogData.LoadFromStreamingAssetsCoroutine(
                    _catalogConfig.CatalogFileName);
                catalogData.ThrowIfFailed(
                    "Resources catalog refresh failed.");
            }

            if (IsCurrentOperationGeneration(operationGeneration))
            {
                _catalogData = catalogData;
            }
        }

        private void HandleResourceFound(ResourceLoadingHandler<ResourceType> handler, string fullPath, ResourceType resource)
        {
            handler.Resouce = resource;
            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Loaded;

            QuickLog.Info<ResourceFolderAsyncResourceProvider<ResourceType>>(
                "Resource '{0}' loaded successfully from folder '{1}'.",
                fullPath, folderName
            );
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
            _isInitialized = false;
            QuickLog.Error<ResourceFolderAsyncResourceProvider<ResourceType>>(
                "Resource folder provider initialization failed: {0}",
                exception);
        }

        private void OnDisable()
        {
            AdvanceOperationGeneration();
            _isInitialized = false;
            InitializationException = null;
        }

        private int AdvanceOperationGeneration()
        {
            unchecked
            {
                _operationGeneration++;
            }

            return _operationGeneration;
        }

        private bool IsCurrentOperationGeneration(int operationGeneration)
        {
            return operationGeneration == _operationGeneration;
        }

        private static void HandleCatalogInvalidationFailure(
            Exception exception)
        {
            QuickLog.Error<ResourceFolderAsyncResourceProvider<ResourceType>>(
                "Resources catalog invalidation failed: {0}",
                exception);
        }

        private string ResolveResourcePath(
            IAsyncResourceId resourceId,
            IResourceFolderAsyncResourceId folderId)
        {
            string relativePath = _catalogData?.GetRelativePath(
                resourceId?.ResourceId);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = folderId.GetResourcePath(this);
            }

            relativePath = NormalizeResourcePath(relativePath);

            if (string.IsNullOrWhiteSpace(folderName))
            {
                return relativePath?.TrimStart('/');
            }

            return folderName.Trim('/')
                + "/"
                + (relativePath ?? string.Empty).TrimStart('/');
        }

        private static string NormalizeResourcePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string normalized = path.Replace('\\', '/').Trim('/');
            string extension = System.IO.Path.GetExtension(normalized);
            if (!IsRecognizedAssetExtension(extension))
            {
                return normalized;
            }

            return normalized.Substring(0, normalized.Length - extension.Length);
        }

        private static bool IsRecognizedAssetExtension(string extension)
        {
            switch (extension?.ToLowerInvariant())
            {
                case ".asset":
                case ".json":
                case ".txt":
                case ".bytes":
                case ".bin":
                case ".prefab":
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".mat":
                case ".controller":
                case ".anim":
                    return true;
                default:
                    return false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            timeout = Mathf.Max(0.1f, timeout);
            folderName = folderName?.Trim().Trim('/');
        }
#endif

        private void HandleResourceNotFound(ResourceLoadingHandler<ResourceType> handler, string fullPath)
        {
            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Failed;
            handler.Exception = new InvalidOperationException(
                $"Resources.LoadAsync returned null for " +
                $"'{fullPath}'. The asset may not exist or " +
                $"may not be of the expected type " +
                $"({typeof(ResourceType).Name})."
            );

            QuickLog.Warning<ResourceFolderAsyncResourceProvider<ResourceType>>(
                "Resources.LoadAsync returned null for '{0}'. " +
                "Expected type: {1}.",
                fullPath, typeof(ResourceType).Name
            );
        }
    }

}
