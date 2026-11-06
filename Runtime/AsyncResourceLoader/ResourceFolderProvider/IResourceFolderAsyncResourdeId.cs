namespace Com.Scheherazade.Common.AsyncResourceLoader
{
    public interface IResourceFolderAsyncResourceId
    {
        string GetResourcePath(IResourceFolderAsyncResourceProvider provider);
    }
}