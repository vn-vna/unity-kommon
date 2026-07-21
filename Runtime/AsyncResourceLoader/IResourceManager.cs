using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IResourceManager
    {
        ResourceManagerStatus Status { get; }
        void Initialize(float timeout = float.MaxValue);
    }

    public interface IResourceManager<ResourceType> : IResourceManager
        where ResourceType : UnityEngine.Object
    {
        IEnumerator InitializeCoroutine(float timeout = float.MaxValue);
        ResourceLoadingHandler<ResourceType> LoadResouceAsync(IAsyncResourceId resouce);
    }

    public enum ResourceManagerStatus
    {
        Uninitialized,
        Initializing,
        Initialized
    }

}