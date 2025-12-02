namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    public interface IRemoteConfigParserModule
    {
        int Priority { get; }
        bool TryParse(string input, out object output);
    }
}