namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public abstract class VersionMigrator<TOld, TNew>
        where TOld : IVersionedData
        where TNew : IVersionedData
    {
        public abstract TNew Migrate(TOld snapshot);
    }
}
