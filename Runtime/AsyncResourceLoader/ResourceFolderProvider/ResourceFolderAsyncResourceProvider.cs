using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
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
        ICatalogAwareAsyncResourceProvider
        where ResourceType : UnityEngine.Object
    {
        public bool IsInitialized => _isInitialized;
        public int Priority => priority;
        public float ResourceLoadingTimeout => timeout;
        public string FolderName => folderName;

        [SerializeField]
        private string folderName;

        [SerializeField]
        private int priority;

        [SerializeField]
        private float timeout;

        [SerializeField]
        [Tooltip("When enabled, loads a catalog JSON file to determine which resources this provider can serve.")]
        private CatalogConfig _catalogConfig = new CatalogConfig();

        private volatile bool _isInitialized;
        private CatalogData _catalogData;

        public void Initialize()
        {
            _catalogData = new CatalogData();
            if (_catalogConfig.UseCatalog
                && !string.IsNullOrEmpty(_catalogConfig.CatalogFileName))
            {
                _catalogData.LoadFromStreamingAssets(_catalogConfig.CatalogFileName);
            }

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

            string fullPath = rfarid.GetResourcePath(this);

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

            QuickLog.Debug<ResourceFolderAsyncResourceProvider<ResourceType>>(
                "Attempting to load resource '{0}' from folder '{1}'.",
                fullPath, folderName
            );

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;

            try
            {
                ResourceRequest request = Resources.LoadAsync<ResourceType>(fullPath);
                request.completed += _ =>
                {
                    ResourceType resource = request.asset as ResourceType;
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
            if (_catalogData != null && _catalogData.IsLoaded)
            {
                return _catalogData.HasResource(resourceId.ResourceId);
            }

            return true;
        }

        public DataType GetDataType(string resourceId) =>
            _catalogData?.GetDataType(resourceId) ?? DataType.Unknown;

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
