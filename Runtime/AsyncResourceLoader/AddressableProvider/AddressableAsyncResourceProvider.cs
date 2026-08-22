#if UNITY_ADDRESSABLES
using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [ResourceProvider(
        "Addressable",
        "Loads via Unity Addressables.",
        RequiredDefines = new[] { "UNITY_ADDRESSABLES" }
    )]
    public class AddressableAsyncResourceProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>,
        IAddressableAsyncResourceProvider<ResourceType>,
        ICatalogAwareAsyncResourceProvider,
        IInvalidatableCatalog,
        IAsyncResourceCache,
        IAsyncResourceCacheKeyProvider,
        IAsyncResourceDataTypeResolver,
        IAsyncResourceInitializationStatus,
        IAsyncResourceReleaseProvider<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        public int Priority => priority;
        public bool IsInitialized { get; private set; }
        public Exception InitializationException { get; private set; }
        public float ResourceLoadingTimeout => timeout > 0f ? timeout : 30f;

        [SerializeField]
        private int priority;

        [SerializeField]
        [Min(0.1f)]
        private float timeout = 30f;

        [SerializeField]
        [Tooltip("When enabled, loads a catalog JSON file to determine which resources this provider can serve.")]
        private CatalogConfig _catalogConfig = new CatalogConfig();

        private CatalogData _catalogData;
        private readonly List<AsyncOperationHandle<ResourceType>> _loadHandles
            = new List<AsyncOperationHandle<ResourceType>>();
        private int _loadGeneration;

        public void Initialize()
        {
            ClearCache();
            int initializationGeneration = _loadGeneration;
            IsInitialized = false;
            InitializationException = null;

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
                        "Addressables initialization requires a Dispatcher."
                    ));
            }
        }

        private IEnumerator InitializeCoroutine(int initializationGeneration)
        {
            CatalogData catalogData = new CatalogData();
            if (_catalogConfig.UseCatalog
                && !string.IsNullOrEmpty(_catalogConfig.CatalogFileName))
            {
                yield return catalogData.LoadFromStreamingAssetsCoroutine(
                    _catalogConfig.CatalogFileName);
                catalogData.ThrowIfFailed(
                    "Addressables catalog initialization failed.");
            }

            if (initializationGeneration != _loadGeneration)
            {
                yield break;
            }

            QuickLog.Debug<AddressableAsyncResourceProvider<ResourceType>>(
                "Initializing Addressables system..."
            );

            AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator>
                initHandle = Addressables.InitializeAsync(false);
            try
            {
                yield return initHandle;

                if (initializationGeneration != _loadGeneration)
                {
                    yield break;
                }

                if (initHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    _catalogData = catalogData;
                    IsInitialized = true;

                    QuickLog.Info<AddressableAsyncResourceProvider<ResourceType>>(
                        "Addressables system initialized successfully."
                    );
                    yield break;
                }

                HandleInitializationFailure(
                    initializationGeneration,
                    initHandle.OperationException
                    ?? new InvalidOperationException(
                        "Addressables initialization failed without an exception."));
            }
            finally
            {
                if (initHandle.IsValid())
                {
                    Addressables.Release(initHandle);
                }
            }
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
                handler.Exception = new InvalidOperationException(
                    "Addressables system is not initialized. Cannot load resource."
                );
                handler.ProviderSource = GetType().Name;

                QuickLog.Error<AddressableAsyncResourceProvider<ResourceType>>(
                    "Addressables system is not initialized."
                );
                return;
            }

            if (id is not IAddressableAsyncResourceId addrId)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new ArgumentException(
                    "Invalid Id for Addressable"
                );
                return;
            }

            if (id == null || string.IsNullOrEmpty(id.ResourceId))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.ProviderSource = GetType().Name;
                handler.Exception = new ArgumentNullException(
                    nameof(id), "Resource ID is null or empty."
                );

                QuickLog.Error<AddressableAsyncResourceProvider<ResourceType>>(
                    "Resource ID is null or empty."
                );
                return;
            }

            string resolvedKey = ResolveAddressableKey(id, addrId);
            if (string.IsNullOrWhiteSpace(resolvedKey))
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new ArgumentException(
                    "Addressable key cannot be null, empty, or whitespace.",
                    nameof(id));
                return;
            }

            QuickLog.Debug<AddressableAsyncResourceProvider<ResourceType>>(
                "Attempting to load addressable resource '{0}'.", resolvedKey
            );

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;

            try
            {
                int loadGeneration = _loadGeneration;
                var loadHandle = Addressables.LoadAssetAsync<ResourceType>(
                    resolvedKey
                );
                loadHandle.Completed += handle =>
                {
                    if (loadGeneration != _loadGeneration)
                    {
                        Addressables.Release(handle);
                        handler.Cancel();
                        handler.Exception = new OperationCanceledException(
                            $"Addressable load '{resolvedKey}' was invalidated."
                        );
                        return;
                    }

                    if (handler.IsCancellationRequested)
                    {
                        Addressables.Release(handle);
                        handler.Cancel();
                        return;
                    }

                    if (handle.Status == AsyncOperationStatus.Succeeded &&
                        handle.Result != null)
                    {
                        _loadHandles.Add(handle);
                        handler.Resouce = handle.Result;
                        handler.LoadingStatus = LoadingStatus.Completed;
                        handler.ResourceStatus = ResourceStatus.Loaded;

                        QuickLog.Info<AddressableAsyncResourceProvider<ResourceType>>(
                            "Addressable resource '{0}' loaded successfully.",
                            resolvedKey
                        );
                    }
                    else
                    {
                        handler.LoadingStatus = LoadingStatus.Completed;
                        handler.ResourceStatus = ResourceStatus.Failed;
                        handler.Exception =
                            handle.OperationException ??
                            new InvalidOperationException(
                                $"Failed to load addressable resource " +
                                $"'{resolvedKey}'. The asset may not exist " +
                                $"or may not be of the expected type " +
                                $"({typeof(ResourceType).Name})."
                            );

                        QuickLog.Error<AddressableAsyncResourceProvider<ResourceType>>(
                            "Failed to load addressable resource '{0}': {1}",
                            resolvedKey, handler.Exception.Message
                        );

                        Addressables.Release(handle);
                    }
                };
            }
            catch (Exception ex)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = ex;

                QuickLog.Error<AddressableAsyncResourceProvider<ResourceType>>(
                    "Exception while starting async load for addressable " +
                    "resource '{0}': {1}",
                    resolvedKey, ex.Message
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
            string cacheKey = resourceId is IAddressableAsyncResourceId addressableId
                ? ResolveAddressableKey(resourceId, addressableId)
                : resourceId?.ResourceId;
            string contentHash = _catalogData?.GetContentHash(
                resourceId?.ResourceId);
            return string.IsNullOrWhiteSpace(contentHash)
                ? cacheKey
                : $"{cacheKey ?? string.Empty}\n{contentHash}";
        }

        public void ClearCache()
        {
            _loadGeneration++;
        }

        private void ReleaseAllHandles()
        {
            for (int i = 0; i < _loadHandles.Count; i++)
            {
                AsyncOperationHandle<ResourceType> handle = _loadHandles[i];
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            _loadHandles.Clear();
        }

        public void ReleaseResource(ResourceType resource)
        {
            if (resource == null)
            {
                return;
            }

            for (int i = _loadHandles.Count - 1; i >= 0; i--)
            {
                AsyncOperationHandle<ResourceType> handle = _loadHandles[i];
                if (!handle.IsValid()
                    || !ReferenceEquals(handle.Result, resource))
                {
                    continue;
                }

                Addressables.Release(handle);
                _loadHandles.RemoveAt(i);
                return;
            }
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
                QuickLog.Error<AddressableAsyncResourceProvider<ResourceType>>(
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
                    "Addressables catalog refresh failed.");
            }
        }

        private void OnDisable()
        {
            ClearCache();
            ReleaseAllHandles();
            IsInitialized = false;
            InitializationException = null;
        }

        private void HandleInitializationFailure(
            int initializationGeneration,
            Exception exception)
        {
            if (initializationGeneration != _loadGeneration)
            {
                return;
            }

            InitializationException = exception;
            IsInitialized = false;
            QuickLog.Error<AddressableAsyncResourceProvider<ResourceType>>(
                "Addressables initialization failed: {0}",
                exception);
        }

        private static void HandleCatalogInvalidationFailure(
            Exception exception)
        {
            QuickLog.Error<AddressableAsyncResourceProvider<ResourceType>>(
                "Addressables catalog invalidation failed: {0}",
                exception);
        }

        private string ResolveAddressableKey(
            IAsyncResourceId resourceId,
            IAddressableAsyncResourceId addressableId)
        {
            string catalogKey = _catalogData?.GetRelativePath(
                resourceId?.ResourceId);
            return string.IsNullOrWhiteSpace(catalogKey)
                ? addressableId.GetAddressableKey(this)
                : catalogKey;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            timeout = Mathf.Max(0.1f, timeout);
        }
#endif
    }
}
#endif
