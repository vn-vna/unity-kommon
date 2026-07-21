namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IAddressableAsyncResourceProvider
    {
        bool IsInitialized { get; }
        int Priority { get; }
        float ResourceLoadingTimeout { get; }
        void Initialize();
    }

    public interface IAddressableAsyncResourceProvider<ResourceType> :
        IAddressableAsyncResourceProvider
        where ResourceType : UnityEngine.Object
    {
        void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        );
    }
}
