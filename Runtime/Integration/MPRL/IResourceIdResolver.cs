namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public interface IResourceIdResolver<ResourceId>
    {
        ResourceId TransformId(IAsyncResourceId id);
    }
}