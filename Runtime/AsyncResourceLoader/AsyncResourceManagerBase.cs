using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public class AsyncResourceManagerBase<SelfType, ResourceType> :
        SingletonScriptableObject<SelfType>,
        IResourceManager<ResourceType>,
        IHasReset
        where SelfType : AsyncResourceManagerBase<SelfType, ResourceType>
        where ResourceType : UnityEngine.Object
    {
        private const float DefaultProviderTimeoutSeconds = 30f;

        public ResourceManagerStatus Status { get; internal set; }

        public Exception InitializationException { get; private set; }

        [SerializeField]
        [HideInInspector]
        private ScriptableObject[] initialProviders;

        private List<IAsyncResourceProvider<ResourceType>> _providers;

        protected IReadOnlyList<IAsyncResourceProvider<ResourceType>> Providers
        {
            get
            {
                _providers ??= new List<IAsyncResourceProvider<ResourceType>>();
                return _providers;
            }
        }

        public void Initialize(float timeout = DefaultProviderTimeoutSeconds)
        {
            if (Status == ResourceManagerStatus.Initialized
                || Status == ResourceManagerStatus.Initializing)
            {
                return;
            }

            bool dispatched = Dispatcher.TryDispatchCoroutine(
                InitializeCoroutine(timeout),
                out _);
            if (dispatched)
            {
                return;
            }

            InitializationException = new InvalidOperationException(
                "Resource manager initialization requires a Dispatcher instance.");
            Status = ResourceManagerStatus.Failed;

            QuickLog.Error<AsyncResourceManagerBase<SelfType, ResourceType>>(
                "Resource manager initialization could not be dispatched: {0}",
                InitializationException.Message
            );
        }

        public IEnumerator InitializeCoroutine(
            float timeout = DefaultProviderTimeoutSeconds)
        {
            timeout = NormalizeTimeout(timeout);
            if (Status == ResourceManagerStatus.Initialized)
            {
                yield break;
            }

            if (Status == ResourceManagerStatus.Initializing)
            {
                while (Status == ResourceManagerStatus.Initializing)
                {
                    yield return null;
                }

                yield break;
            }

            if (_providers == null || Status == ResourceManagerStatus.Failed)
            {
                Reset();
            }

            InitializationException = null;
            Status = ResourceManagerStatus.Initializing;

            if (_providers.Count == 0)
            {
                FailInitialization(new InvalidOperationException(
                    "No resource providers are configured."));
                yield break;
            }

            List<Exception> errors = new List<Exception>();
            int initializedProviderCount = 0;

            foreach (IAsyncResourceProvider<ResourceType> provider in _providers)
            {
                if (provider == null) continue;

                QuickLog.Debug<AsyncResourceManagerBase<SelfType, ResourceType>>(
                    "Initializing provider '{0}'...", provider.GetType().Name
                );

                try
                {
                    provider.Initialize();
                }
                catch (Exception exception)
                {
                    errors.Add(new InvalidOperationException(
                        $"Provider '{provider.GetType().Name}' failed to initialize.",
                        exception));
                    continue;
                }

                float startTime = Time.realtimeSinceStartup;
                while (!provider.IsInitialized
                    && GetInitializationException(provider) == null
                    && !HasTimedOut(startTime, timeout))
                {
                    yield return null;
                }

                if (!provider.IsInitialized)
                {
                    Exception initializationException
                        = GetInitializationException(provider);
                    if (initializationException != null)
                    {
                        errors.Add(new InvalidOperationException(
                            $"Provider '{provider.GetType().Name}' failed "
                            + "during initialization.",
                            initializationException));
                        continue;
                    }

                    TimeoutException timeoutException = new TimeoutException(
                        $"Provider '{provider.GetType().Name}' timed out "
                        + $"during initialization after {timeout:F1}s.");
                    errors.Add(timeoutException);

                    QuickLog.Warning<AsyncResourceManagerBase<SelfType, ResourceType>>(
                        "Provider '{0}' timed out during initialization after {1}s.",
                        provider.GetType().Name, timeout
                    );
                    continue;
                }

                initializedProviderCount++;
            }

            if (initializedProviderCount == 0)
            {
                FailInitialization(new AggregateException(
                    "No resource provider initialized successfully.",
                    errors));
                yield break;
            }

            Status = ResourceManagerStatus.Initialized;

            if (errors.Count > 0)
            {
                InitializationException = new AggregateException(
                    "One or more optional resource providers failed to initialize.",
                    errors);
            }

            QuickLog.Info<AsyncResourceManagerBase<SelfType, ResourceType>>(
                "Resource manager initialized with {0}/{1} provider(s).",
                initializedProviderCount,
                _providers.Count
            );
        }

        public ResourceLoadingHandler<ResourceType> LoadResouceAsync(
            IAsyncResourceId resouce
        )
        {
            if (Status != ResourceManagerStatus.Initialized)
            {
                QuickLog.Warning<AsyncResourceManagerBase<SelfType, ResourceType>>(
                    "Resource manager is not initialized. Load request for '{0}' may fail.",
                    resouce?.ResourceId ?? "<null>"
                );
            }

            ResourceLoadingHandler<ResourceType> handler = new ResourceLoadingHandler<ResourceType>();
            bool dispatched = Dispatcher.TryDispatchCoroutine(
                PerformLoadResourceCoroutine(resouce, handler),
                out _);
            if (!dispatched)
            {
                FailHandler(
                    handler,
                    new InvalidOperationException(
                        "Resource loading requires a Dispatcher instance."));
            }

            return handler;
        }

        protected void InvalidateProviderCatalogs(
            CatalogInvalidationMode mode)
        {
            if (_providers == null)
            {
                return;
            }

            foreach (IAsyncResourceProvider<ResourceType> provider in _providers)
            {
                if (provider is IInvalidatableCatalog inv)
                {
                    inv.InvalidateCatalog(mode);
                }
            }
        }

        protected IEnumerator InvalidateProviderCatalogsCoroutine(
            CatalogInvalidationMode mode)
        {
            if (_providers == null)
            {
                yield break;
            }

            List<Exception> invalidationErrors = new List<Exception>();
            foreach (IAsyncResourceProvider<ResourceType> provider in _providers)
            {
                if (provider is IInvalidatableCatalog inv)
                {
                    Exception invalidationError = null;
                    yield return RunGuardedCoroutine(
                        inv.InvalidateCatalogCoroutine(mode),
                        exception => invalidationError = exception);
                    if (invalidationError != null)
                    {
                        invalidationErrors.Add(new InvalidOperationException(
                            $"Provider '{provider.GetType().Name}' failed to "
                            + "invalidate its catalog.",
                            invalidationError));
                    }
                }
            }

            if (invalidationErrors.Count > 0)
            {
                throw new AggregateException(
                    "One or more provider catalogs failed to invalidate.",
                    invalidationErrors);
            }
        }


        public void Reset()
        {
            HandleResetting();
            Status = ResourceManagerStatus.Uninitialized;
            InitializationException = null;

            _providers = new List<IAsyncResourceProvider<ResourceType>>();

            if (initialProviders == null)
            {
                return;
            }

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

        protected virtual void HandleResetting()
        {
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Reset();
        }

        private IEnumerator PerformLoadResourceCoroutine(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        )
        {
            handler.LoadingStatus = LoadingStatus.Initiating;
            handler.ResourceStatus = ResourceStatus.Unknown;

            if (id == null || string.IsNullOrWhiteSpace(id.ResourceId))
            {
                FailHandler(
                    handler,
                    new ArgumentException(
                        "Resource ID cannot be null, empty, or whitespace.",
                        nameof(id)));
                yield break;
            }

            if (_providers == null || _providers.Count == 0)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new InvalidOperationException(
                    "No resource providers registered."
                );
                yield break;
            }

            List<Exception> providerErrors = new List<Exception>();

            foreach (IAsyncResourceProvider<ResourceType> provider in _providers)
            {
                if (provider == null) continue;

                if (handler.IsCancellationRequested)
                {
                    handler.Cancel();
                    yield break;
                }

                bool canProvide;
                try
                {
                    canProvide = provider
                        is not ICatalogAwareAsyncResourceProvider aware
                        || aware.HasResource(id);
                }
                catch (Exception exception)
                {
                    providerErrors.Add(new InvalidOperationException(
                        $"Provider '{provider.GetType().Name}' failed while "
                        + $"checking resource '{id.ResourceId}'.",
                        exception));
                    continue;
                }

                if (!canProvide)
                {
                    continue;
                }

                if (!TryValidateProviderDataType(
                        provider,
                        id,
                        out Exception dataTypeException))
                {
                    providerErrors.Add(dataTypeException);
                    continue;
                }

                ResourceLoadingHandler<ResourceType> providerResult =
                    new ResourceLoadingHandler<ResourceType>();

                try
                {
                    provider.TryLoadResource(id, providerResult);
                }
                catch (Exception exception)
                {
                    providerErrors.Add(new InvalidOperationException(
                        $"Provider '{provider.GetType().Name}' threw while "
                        + $"loading '{id.ResourceId}'.",
                        exception));
                    continue;
                }

                float providerTimeout = GetEffectiveProviderTimeout(provider);
                float startTime = Time.realtimeSinceStartup;
                while (!providerResult.IsCompleted
                    && !HasTimedOut(startTime, providerTimeout))
                {
                    if (handler.IsCancellationRequested)
                    {
                        providerResult.Cancel();
                        handler.Cancel();
                        yield break;
                    }

                    yield return null;
                }

                if (providerResult.IsCompleted)
                {
                    if (providerResult.ResourceStatus == ResourceStatus.Loaded
                        && providerResult.Resouce != null)
                    {
                        if (!TryValidateProviderDataType(
                                provider,
                                id,
                                out dataTypeException))
                        {
                            ReleaseProviderResource(
                                provider,
                                providerResult.Resouce);
                            providerErrors.Add(dataTypeException);
                            continue;
                        }

                        CompleteHandler(handler, providerResult, provider);
                        yield break;
                    }

                    if (providerResult.Exception != null)
                    {
                        providerErrors.Add(providerResult.Exception);
                    }

                    QuickLog.Debug<AsyncResourceManagerBase<SelfType, ResourceType>>(
                        "Provider '{0}' failed to load '{1}', trying next provider.",
                        provider.GetType().Name,
                        id.ResourceId
                    );
                    continue;
                }

                TimeoutException providerTimeoutException = new TimeoutException(
                    $"Provider '{provider.GetType().Name}' timed out loading "
                    + $"'{id.ResourceId}' after {providerTimeout:F1}s.");
                providerErrors.Add(providerTimeoutException);
                providerResult.Cancel();
            }

            FailHandler(
                handler,
                new AggregateException(
                    $"All providers failed to load resource '{id.ResourceId}'.",
                    providerErrors));

            QuickLog.Error<AsyncResourceManagerBase<SelfType, ResourceType>>(
                "All {0} provider(s) failed to load resource '{1}': Exception: {2}",
                _providers.Count, id.ResourceId, handler.Exception
            );
        }


        private int CompareAsyncResourceProvider(
            IAsyncResourceProvider asp1, IAsyncResourceProvider asp2
        ) => asp1.Priority.CompareTo(asp2.Priority);

        private static float GetEffectiveProviderTimeout(
            IAsyncResourceProvider provider)
        {
            float timeout = provider.ResourceLoadingTimeout;
            return timeout > 0f && !float.IsNaN(timeout)
                ? timeout
                : DefaultProviderTimeoutSeconds;
        }

        private static float NormalizeTimeout(float timeout)
        {
            return timeout > 0f && !float.IsNaN(timeout)
                ? timeout
                : DefaultProviderTimeoutSeconds;
        }

        private static Exception GetInitializationException(
            IAsyncResourceProvider provider)
        {
            return provider is IAsyncResourceInitializationStatus status
                ? status.InitializationException
                : null;
        }

        private static bool TryValidateProviderDataType(
            IAsyncResourceProvider<ResourceType> provider,
            IAsyncResourceId resourceId,
            out Exception exception)
        {
            exception = null;
            if (provider is not IAsyncResourceDataTypePolicy policy
                || provider is not IAsyncResourceDataTypeResolver resolver)
            {
                return true;
            }

            DataType dataType;
            try
            {
                dataType = resolver.GetDataType(resourceId);
            }
            catch (Exception resolverException)
            {
                exception = new InvalidOperationException(
                    $"Provider '{provider.GetType().Name}' failed to resolve "
                    + $"the data type for '{resourceId?.ResourceId}'.",
                    resolverException);
                return false;
            }

            if (dataType == DataType.Unknown || policy.SupportsDataType(dataType))
            {
                return true;
            }

            exception = new NotSupportedException(
                $"Provider '{provider.GetType().Name}' does not support "
                + $"{dataType} data for '{resourceId?.ResourceId}'.");
            return false;
        }

        protected static IEnumerator RunGuardedCoroutine(
            IEnumerator coroutine,
            Action<Exception> exceptionHandler)
        {
            IEnumerator guarded = CoroutineExceptionGuard.Run(
                coroutine,
                exceptionHandler);
            while (guarded.MoveNext())
            {
                yield return guarded.Current;
            }
        }

        private static bool HasTimedOut(float startTime, float timeout)
        {
            return timeout < float.MaxValue
                && Time.realtimeSinceStartup - startTime >= timeout;
        }

        private static void CompleteHandler(
            ResourceLoadingHandler<ResourceType> destination,
            ResourceLoadingHandler<ResourceType> source,
            IAsyncResourceProvider<ResourceType> provider)
        {
            if (destination.IsCancellationRequested)
            {
                ReleaseProviderResource(provider, source.Resouce);
                destination.Cancel();
                return;
            }

            destination.Resouce = source.Resouce;
            destination.LoadingStatus = LoadingStatus.Completed;
            destination.ResourceStatus = ResourceStatus.Loaded;
            destination.ProviderSource = source.ProviderSource
                ?? provider.GetType().Name;
            destination.Provider = provider;
            destination.Progress = 1f;
        }

        private static void ReleaseProviderResource(
            IAsyncResourceProvider provider,
            ResourceType resource)
        {
            if (resource != null
                && provider
                    is IAsyncResourceReleaseProvider<ResourceType> releaseProvider)
            {
                releaseProvider.ReleaseResource(resource);
            }
        }

        private static void FailHandler(
            ResourceLoadingHandler<ResourceType> handler,
            Exception exception)
        {
            if (handler.IsCancellationRequested)
            {
                handler.Cancel();
                return;
            }

            handler.LoadingStatus = LoadingStatus.Completed;
            handler.ResourceStatus = ResourceStatus.Failed;
            handler.Exception = exception;
        }

        private void FailInitialization(Exception exception)
        {
            InitializationException = exception;
            Status = ResourceManagerStatus.Failed;

            QuickLog.Error<AsyncResourceManagerBase<SelfType, ResourceType>>(
                "Resource manager initialization failed: {0}",
                exception
            );
        }

    }
}
