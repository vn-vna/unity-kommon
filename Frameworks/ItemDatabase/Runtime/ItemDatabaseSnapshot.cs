namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    internal readonly struct ItemDatabaseSnapshot
    {
        internal ItemDatabaseState State { get; }

        internal long Version { get; }

        internal ItemDatabaseSnapshot(ItemDatabaseState state, long version)
        {
            State = state;
            Version = version;
        }
    }
}
