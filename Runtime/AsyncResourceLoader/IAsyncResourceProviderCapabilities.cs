namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IAsyncResourceCache
    {
        void ClearCache();
    }

    public interface ISelectiveAsyncResourceCache : IAsyncResourceCache
    {
        void ClearCache(string resourceId);
    }

    public interface IAsyncResourceReleaseProvider<in ResourceType>
        where ResourceType : UnityEngine.Object
    {
        void ReleaseResource(ResourceType resource);
    }

    public interface IAsyncResourceCacheKeyProvider
    {
        string GetCacheKey(IAsyncResourceId resourceId);
    }

    public interface IAsyncResourceInitializationStatus
    {
        System.Exception InitializationException { get; }
    }

    public interface IAsyncResourceInterpolationTagReceiver
    {
        void SetInterpolationTags(
            System.Collections.Generic.IReadOnlyDictionary<string, string> tags);
    }

    public interface IAsyncResourceDataTypePolicy
    {
        bool SupportsDataType(DataType dataType);
    }

    public interface IAsyncResourceDataTypeResolver
    {
        DataType GetDataType(IAsyncResourceId resourceId);
    }
}
