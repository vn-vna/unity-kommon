using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration
{

    public interface IResourceManager<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        ResourceManagerStatus Status { get; }
        void Initialize(float timeout = float.MaxValue);
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