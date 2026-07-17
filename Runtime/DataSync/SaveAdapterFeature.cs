using System;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [Flags]
    public enum SaveAdapterFeature
    {
        None    = 0,
        Read    = 1 << 0,
        Write   = 1 << 1,
        Delete  = 1 << 2,
        Exists  = 1 << 3,
        KvStore = 1 << 4,
        Cloud   = 1 << 5
    }
}
