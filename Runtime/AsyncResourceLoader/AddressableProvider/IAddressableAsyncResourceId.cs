namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public interface IAddressableAsyncResourceId
    {
        string GetAddressableKey(
            IAddressableAsyncResourceProvider provider
        );
    }
}
