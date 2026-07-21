using System;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public class ResourceLoadingHandler<ResourceType>
        where ResourceType : class
    {
        public ResourceType Resouce { get; internal set; }
        public LoadingStatus LoadingStatus { get; internal set; }
        public ResourceStatus ResourceStatus { get; internal set; }
        public string ProviderSource { get; internal set; }
        public Exception Exception { get; internal set; }
        public float Progress { get; internal set; }
    }
}