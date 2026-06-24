namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public interface IAsyncResourceReferenceTable<ResourceType>
        where ResourceType : UnityEngine.Object
    {
        ResourceType RequestResourceById(string id);
    }
}