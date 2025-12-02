namespace Com.Hapiga.Scheherazade.Common.LocalSave
{
    public abstract class VersionMigrator<T>
    {
        public abstract void Migrate(string serializedData, out string targetVersionData);
    }


}