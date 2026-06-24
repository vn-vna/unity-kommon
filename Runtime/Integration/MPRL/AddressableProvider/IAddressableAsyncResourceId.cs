namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    public interface IAddressableAsyncResourceId
    {
        string GetAddressableKey(
            IAddressableAsyncResourceProvider provider
        );
    }
}
