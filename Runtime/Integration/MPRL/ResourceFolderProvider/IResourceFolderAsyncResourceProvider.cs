namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public interface IResourceFolderAsyncResourceProvider
    {
        bool IsInitialized { get; }
        int Priority { get; }
        float ResourceLoadingTimeout { get; }
        string FolderName { get; }
        void Initialize();
    }

    public interface IResourceFolderAsyncResourceProvider<ResourceType> :
        IResourceFolderAsyncResourceProvider
        where ResourceType : UnityEngine.Object
    {
        void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        );
    }

}
