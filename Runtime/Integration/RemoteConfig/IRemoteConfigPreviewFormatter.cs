namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    /// <summary>
    /// Formats a remote config value for editor preview/debug display.
    /// Implementations are referenced from <see cref="RemoteConfigMetadataAttribute.Formatter"/>.
    /// </summary>
    public interface IRemoteConfigPreviewFormatter
    {
        string Format(object value);
    }
}
