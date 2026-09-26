using System;

namespace Com.Scheherazade.Common.DataSync
{
    [Serializable]
    public struct KvEntry
    {
        public string key;
        public string serializedValue;
    }
}
