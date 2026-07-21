namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IResourceFolderAsyncResourceId
    {
        string GetResourcePath(IResourceFolderAsyncResourceProvider provider);
    }
}