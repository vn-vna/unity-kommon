namespace Com.Scheherazade.Common.AsyncResourceLoader
{
    public interface IAddressableAsyncResourceId
    {
        string GetAddressableKey(
            IAddressableAsyncResourceProvider provider
        );
    }
}
