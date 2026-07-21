namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IStreamingAssetProvider
    {
        bool IsInitialized { get; }
        int Priority { get; }
        float ResourceLoadingTimeout { get; }
        string SubFolder { get; }
        void Initialize();
    }

    public interface IStreamingAssetProvider<ResourceType> :
        IStreamingAssetProvider
        where ResourceType : UnityEngine.Object
    {
        void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        );
    }
}
