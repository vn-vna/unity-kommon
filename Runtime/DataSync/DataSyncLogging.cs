namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    /// <summary>
    /// Shared runtime flag that gates verbose (debug) logging across
    /// the DataSync module. Driven by
    /// <see cref="DataSyncConfiguration.VerboseLogging"/>.
    /// </summary>
    public static class DataSyncLogging
    {
        public static bool Verbose { get; set; }
    }
}
