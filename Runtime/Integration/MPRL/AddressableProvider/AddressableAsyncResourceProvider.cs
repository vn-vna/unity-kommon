#if UNITY_ADDRESSABLES
using System;
using Com.Hapiga.Scheherazade.Common.Integration;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public class AddressableAsyncResourceProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>,
        IAddressableAsyncResourceProvider<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        public int Priority => priority;
        public bool IsInitialized { get; private set; }
        public float ResourceLoadingTimeout => timeout;

        [SerializeField]
        private int priority;

        [SerializeField]
        private float timeout;

        public void Initialize()
        {
            IsInitialized = false;

            QuickLog.Debug<AddressableAsyncResourceProvider<ResourceType>>(
                "Initializing Addressables system..."
            );

            var initHandle = Addressables.InitializeAsync();
            initHandle.Completed += handle =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    IsInitialized = true;

                    QuickLog.Info<AddressableAsyncResourceProvider<ResourceType>>(
                        "Addressables system initialized successfully."
                    );
                }
                else
                {
                    QuickLog.Error<AddressableAsyncResourceProvider<ResourceType>>(
                        "Failed to initialize Addressables system: {0}",
                        handle.OperationException?.Message ?? "Unknown error"
                    );
                }
            };
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

            string resolvedKey = addrId.GetAddressableKey(this);

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

            QuickLog.Debug<AddressableAsyncResourceProvider<ResourceType>>(
                "Attempting to load addressable resource '{0}'.", resolvedKey
            );

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;

            try
            {
                var loadHandle = Addressables.LoadAssetAsync<ResourceType>(
                    resolvedKey
                );
                loadHandle.Completed += handle =>
                {
                    if (handle.Status == AsyncOperationStatus.Succeeded &&
                        handle.Result != null)
                    {
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
    }
}
#endif