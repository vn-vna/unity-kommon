namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public interface IReferenceTableAsyncResourceProvider
    {
        bool IsInitialized { get; }
        int Priority { get; }
        float ResourceLoadingTimeout { get; }
        void Initialize();
    }

    public interface IReferenceTableAsyncResourceProvider<ResourceType> :
        IReferenceTableAsyncResourceProvider
        where ResourceType : UnityEngine.Object
    {
        IAsyncResourceReferenceTable<ResourceType> ReferenceTable { get; }
        void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        );
    }
}
