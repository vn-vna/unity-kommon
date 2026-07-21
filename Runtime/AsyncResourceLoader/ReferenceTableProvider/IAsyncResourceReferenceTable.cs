namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IAsyncResourceReferenceTable<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        ResourceType RequestResourceById(string id);
    }
}