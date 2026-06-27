using Com.Hapiga.Scheherazade.Common.Integration;

namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public interface IDownloadableAsyncResourceId : IAsyncResourceId
    {
        string GetUrl(IDownloadableResourceProvider provider);
    }
}
