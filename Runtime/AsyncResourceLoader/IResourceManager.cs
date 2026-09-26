using System.Collections;

namespace Com.Scheherazade.Common.AsyncResourceLoader
{
    public interface IResourceManager
    {
        ResourceManagerStatus Status { get; }
        void Initialize(float timeout = 30f);
    }

    public interface IResourceManager<ResourceType> : IResourceManager
        where ResourceType : UnityEngine.Object
    {
        IEnumerator InitializeCoroutine(float timeout = 30f);
        ResourceLoadingHandler<ResourceType> LoadResouceAsync(IAsyncResourceId resouce);
    }

    public enum ResourceManagerStatus
    {
        Uninitialized,
        Initializing,
        Initialized,
        Failed
    }

}
