using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Integration.MPRL;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration
{
    public class ResourceManagerBase<SelfType, ResourceType> :
        SingletonScriptableObject<SelfType>,
        IResourceManager<ResourceType>,
        IIntegrationModule,
        ITickableModule
        where SelfType : ResourceManagerBase<SelfType, ResourceType>
        where ResourceType : UnityEngine.Object
    {
        public ResourceManagerStatus Status { get; internal set; }

        [SerializeField]
        [HideInInspector]
        private ScriptableObject[] initialProviders;

        private List<IAsyncResourceProvider<ResourceType>> _providers;

        public void Initialize(float timeout = float.MaxValue)
        {
            if (Status == ResourceManagerStatus.Initialized)
            {
                return;
            }

            Reset();
            Status = ResourceManagerStatus.Initializing;

            QuickLog.Debug<ResourceManagerBase<SelfType, ResourceType>>(
                "Resource manager initializing with {0} provider(s).",
                _providers?.Count ?? 0
            );
        }

        public IEnumerator InitializeCoroutine(float timeout = float.MaxValue)
        {
            if (Status == ResourceManagerStatus.Initialized)
            {
                yield break;
            }

            if (_providers == null)
            {
                Reset();
            }

            Status = ResourceManagerStatus.Initializing;

            foreach (IAsyncResourceProvider<ResourceType> provider in _providers)
            {
                if (provider == null) continue;

                QuickLog.Debug<ResourceManagerBase<SelfType, ResourceType>>(
                    "Initializing provider '{0}'...", provider.GetType().Name
                );

                provider.Initialize();

                float timer = 0f;
                while (!provider.IsInitialized && timer < timeout)
                {
                    timer += Time.deltaTime;
                    yield return null;
                }

                if (!provider.IsInitialized)
                {
                    QuickLog.Warning<ResourceManagerBase<SelfType, ResourceType>>(
                        "Provider '{0}' timed out during initialization after {1}s.",
                        provider.GetType().Name, timeout
                    );
                }
            }

            Status = ResourceManagerStatus.Initialized;

            QuickLog.Info<ResourceManagerBase<SelfType, ResourceType>>(
                "Resource manager initialized with {0} provider(s).",
                _providers.Count
            );
        }

        public ResourceLoadingHandler<ResourceType> LoadResouceAsync(
            IAsyncResourceId resouce
        )
        {
            if (Status != ResourceManagerStatus.Initialized)
            {
                QuickLog.Warning<ResourceManagerBase<SelfType, ResourceType>>(
                    "Resource manager is not initialized. Load request for '{0}' may fail.",
                    resouce?.ResourceId ?? "<null>"
                );
            }

            ResourceLoadingHandler<ResourceType> handler = new ResourceLoadingHandler<ResourceType>();
            PerformLoadResourceCoroutine(resouce, handler).DispatchOnDispatcher();
            return handler;
        }


        public void Reset()
        {
            Status = ResourceManagerStatus.Uninitialized;

            _providers = new List<IAsyncResourceProvider<ResourceType>>();
            foreach (ScriptableObject providerCandidate in initialProviders)
            {
                if (providerCandidate is not IAsyncResourceProvider<ResourceType> provider)
                {
                    continue;
                }

                _providers.Add(provider);
            }

            _providers.Sort(CompareAsyncResourceProvider);
        }

        public void Tick(float deltaTime)
        { }

        private IEnumerator PerformLoadResourceCoroutine(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            handler.LoadingStatus = LoadingStatus.Initiating;
            handler.ResourceStatus = ResourceStatus.Unknown;

            if (_providers == null || _providers.Count == 0)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new InvalidOperationException(
                    "No resource providers registered."
                );
                yield break;
            }

            foreach (IAsyncResourceProvider<ResourceType> provider in _providers)
            {
                if (provider == null) continue;

                ResourceLoadingHandler<ResourceType> providerResult =
                    new ResourceLoadingHandler<ResourceType>();

                float timer = 0f;

                provider.TryLoadResource(id, providerResult);

                while (timer < provider.ResourceLoadingTimeout)
                {
                    timer += Time.deltaTime;

                    if (providerResult.LoadingStatus == LoadingStatus.Completed)
                    {
                        if (providerResult.ResourceStatus == ResourceStatus.Loaded)
                        {
                            handler.Resouce = providerResult.Resouce;
                            handler.LoadingStatus = LoadingStatus.Completed;
                            handler.ResourceStatus = ResourceStatus.Loaded;
                            handler.ProviderSource = providerResult.ProviderSource;
                            yield break;
                        }

                        if (providerResult.ResourceStatus == ResourceStatus.Failed)
                        {
                            QuickLog.Debug<ResourceManagerBase<SelfType, ResourceType>>(
                                "Provider '{0}' failed to load '{1}', trying next provider.",
                                provider.GetType().Name, id?.ResourceId
                            );
                            break;
                        }
                    }

                    yield return null;
                }

                if (providerResult.LoadingStatus == LoadingStatus.Completed
                    && providerResult.ResourceStatus == ResourceStatus.Loaded)
                {
                    handler.Resouce = providerResult.Resouce;
                    handler.LoadingStatus = LoadingStatus.Completed;
                    handler.ResourceStatus = ResourceStatus.Loaded;
                    handler.ProviderSource = providerResult.ProviderSource;
                    yield break;
                }
            }

            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Failed;
            handler.Exception = new InvalidOperationException(
                $"All providers failed to load resource '{id?.ResourceId ?? "<null>"}'."
            );

            QuickLog.Error<ResourceManagerBase<SelfType, ResourceType>>(
                "All {0} provider(s) failed to load resource '{1}'.",
                _providers.Count, id?.ResourceId ?? "<null>"
            );
        }


        private int CompareAsyncResourceProvider(
            IAsyncResourceProvider asp1, IAsyncResourceProvider asp2
        ) => asp1.Priority - asp2.Priority;

    }
}