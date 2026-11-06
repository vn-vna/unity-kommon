namespace Com.Scheherazade.Common.VIC
{
    public interface IVersionInfoConsumer
    {
        bool IsActive { get; }
        void Consume(string versionInfo);
    }
}
