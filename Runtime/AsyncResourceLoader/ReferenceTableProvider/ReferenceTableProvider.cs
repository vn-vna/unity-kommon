using System;
using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [ResourceProvider(
        "Reference Table",
        "Synchronous lookup from an IAsyncResourceReferenceTable asset. Override ReferenceTable property."
    )]
    public abstract class ReferenceTableAsyncResourceProvider<ResourceType> :
        ScriptableObject,
        IAsyncResourceProvider<ResourceType>,
        IReferenceTableAsyncResourceProvider<ResourceType>,
        IAsyncResourceCacheKeyProvider
        where ResourceType : UnityEngine.Object
    {
        public int Priority => priority;
        public bool IsInitialized { get; private set; }
        public float ResourceLoadingTimeout => timeout > 0f ? timeout : 1f;
        public abstract IAsyncResourceReferenceTable<ResourceType> ReferenceTable { get; }

        [SerializeField]
        private int priority;

        [SerializeField]
        [Min(0.1f)]
        private float timeout = 1f;

        public virtual void Initialize()
        {
            IsInitialized = ReferenceTable != null;

            if (!IsInitialized)
            {
                QuickLog.Warning<ReferenceTableAsyncResourceProvider<ResourceType>>(
                    "Reference table is not assigned. Provider will not initialize."
                );
            }
            else
            {
                QuickLog.Debug<ReferenceTableAsyncResourceProvider<ResourceType>>(
                    "Reference table provider initialized."
                );
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
                handler.ProviderSource = GetType().Name;
                handler.Exception = new InvalidOperationException(
                    "Reference table provider is not initialized."
                );
                return;
            }

            if (id is not IReferenceTableAsyncResourceId rtid)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = new ArgumentException(
                    "Invalid Id for Reference Table"
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
                return;
            }

            string resolvedId = rtid.GetResourceId(this);

            handler.LoadingStatus = LoadingStatus.Loading;
            handler.ResourceStatus = ResourceStatus.Unknown;
            handler.ProviderSource = GetType().Name;

            try
            {
                ResourceType resource = ReferenceTable.RequestResourceById(resolvedId);

                if (resource != null)
                {
                    handler.Resouce = resource;
                    handler.LoadingStatus = LoadingStatus.Completed;
                    handler.ResourceStatus = ResourceStatus.Loaded;

                    QuickLog.Debug<ReferenceTableAsyncResourceProvider<ResourceType>>(
                        "Resource '{0}' found in reference table.",
                        resolvedId
                    );
                }
                else
                {
                    handler.LoadingStatus = LoadingStatus.Completed;
                    handler.ResourceStatus = ResourceStatus.Failed;
                    handler.Exception = new InvalidOperationException(
                        $"Resource '{resolvedId}' not " +
                        "found in reference table."
                    );

                    QuickLog.Warning<ReferenceTableAsyncResourceProvider<ResourceType>>(
                        "Resource '{0}' not found in reference table.",
                        resolvedId
                    );
                }
            }
            catch (Exception ex)
            {
                handler.LoadingStatus = LoadingStatus.Completed;
                handler.ResourceStatus = ResourceStatus.Failed;
                handler.Exception = ex;

                QuickLog.Error<ReferenceTableAsyncResourceProvider<ResourceType>>(
                    "Exception while requesting resource '{0}' from " +
                    "reference table: {1}",
                    resolvedId, ex.Message
                );
            }
        }

        public string GetCacheKey(IAsyncResourceId resourceId)
        {
            return resourceId is IReferenceTableAsyncResourceId referenceId
                ? referenceId.GetResourceId(this)
                : resourceId?.ResourceId;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            timeout = Mathf.Max(0.1f, timeout);
        }
#endif
    }
}
