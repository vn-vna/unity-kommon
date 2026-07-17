namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public abstract class VersionMigrator<TOld, TNew>
    {
        public abstract TNew Migrate(TOld snapshot);
    }
}
