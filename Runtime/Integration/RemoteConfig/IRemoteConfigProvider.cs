namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    public interface IRemoteConfigProvider
    {
        int Priority { get; }
        bool IsInitialized { get; }
        bool IsReady { get; }
        IRemoteConfigManager Manager { get; set; }

        void Initialize();
        void Refresh();
        bool TryGetConfig<T>(string key, out T result);
    }
}