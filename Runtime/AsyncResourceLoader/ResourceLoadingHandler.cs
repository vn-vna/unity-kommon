using System;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public class ResourceLoadingHandler<ResourceType>
        where ResourceType : class
    {
        public ResourceType Resouce { get; set; }
        public LoadingStatus LoadingStatus { get; set; }
        public ResourceStatus ResourceStatus { get; set; }
        public string ProviderSource { get; set; }
        public Exception Exception { get; set; }
        public float Progress { get; set; }
    }
}