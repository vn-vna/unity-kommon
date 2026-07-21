namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IStreamingAssetId : IAsyncResourceId
    {
        string GetFilePath(IStreamingAssetProvider provider);
    }
}
