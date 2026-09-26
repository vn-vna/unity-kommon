namespace Com.Scheherazade.Common.AsyncResourceLoader
{
    public interface IReferenceTableAsyncResourceId
    {
        string GetResourceId(
            IReferenceTableAsyncResourceProvider provider
        );
    }
}
