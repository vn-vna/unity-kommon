using System;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [Serializable]
    public struct KvEntry
    {
        public string key;
        public string serializedValue;
    }
}
