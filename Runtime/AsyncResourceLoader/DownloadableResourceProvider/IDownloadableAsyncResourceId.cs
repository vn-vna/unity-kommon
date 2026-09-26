using Com.Scheherazade.Common.AsyncResourceLoader;

namespace Com.Scheherazade.Common.AsyncResourceLoader
{
    public interface IDownloadableAsyncResourceId : IAsyncResourceId
    {
        string GetUrl(IDownloadableResourceProvider provider);
    }
}
