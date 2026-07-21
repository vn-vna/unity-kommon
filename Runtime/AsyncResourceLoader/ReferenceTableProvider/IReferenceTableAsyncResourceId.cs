namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IReferenceTableAsyncResourceId
    {
        string GetResourceId(
            IReferenceTableAsyncResourceProvider provider
        );
    }
}
