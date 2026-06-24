namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public interface IReferenceTableAsyncResourceId
    {
        string GetResourceId(
            IReferenceTableAsyncResourceProvider provider
        );
    }
}
