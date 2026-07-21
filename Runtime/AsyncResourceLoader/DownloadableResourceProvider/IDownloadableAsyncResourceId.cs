using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IDownloadableAsyncResourceId : IAsyncResourceId
    {
        string GetUrl(IDownloadableResourceProvider provider);
    }
}
