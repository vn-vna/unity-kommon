namespace Com.Scheherazade.Common.VIC
{
    public interface IVersionNamePlaceholderProvider
    {
        bool TryResolve(string key, string format, out string value);
    }
}
