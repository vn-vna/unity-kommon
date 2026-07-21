namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IDownloadableResourceProvider
    {
        float RequestTimeout { get; }
        float CacheTTL { get; }
        int MaxConcurrentDownloads { get; }
        int MaxCacheEntries { get; }
        string CacheSubFolder { get; }
        CacheBasePathType CacheBasePath { get; }
        CustomDownloadHeader[] Headers { get; }
        void ClearCache();
        void ClearCache(string resourceId);
    }

    public interface IDownloadableResourceProvider<ResourceType> :
        IDownloadableResourceProvider
        where ResourceType : UnityEngine.Object
    {
        void TryLoadResource(
            IAsyncResourceId id,
            ResourceLoadingHandler<ResourceType> handler
        );
    }
}
