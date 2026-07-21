namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IResourceIdResolver<ResourceId>
    {
        ResourceId TransformId(IAsyncResourceId id);
    }
}