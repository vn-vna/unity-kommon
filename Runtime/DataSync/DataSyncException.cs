using System;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public class DataSyncException : Exception
    {
        public DataSyncException(string message) : base(message) { }
        public DataSyncException(string message, Exception inner) : base(message, inner) { }
    }

    public class SaveAdapterException : DataSyncException
    {
        public string AdapterId { get; }
        public SaveAdapterException(string adapterId, string message, Exception inner = null)
            : base(message, inner) { AdapterId = adapterId; }
    }

    public class TranslationException : DataSyncException
    {
        public TranslationException(string message, Exception inner = null)
            : base(message, inner) { }
    }

    public class MigrationException : DataSyncException
    {
        public Type SnapshotType { get; }
        public MigrationException(Type snapshotType, string message, Exception inner = null)
            : base(message, inner) { SnapshotType = snapshotType; }
    }

    public class SaveNotFoundException : DataSyncException
    {
        public string Key { get; }
        public SaveNotFoundException(string key) : base($"No save found for key: {key}") { Key = key; }
    }
}
