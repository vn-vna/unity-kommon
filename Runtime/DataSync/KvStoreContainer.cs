using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [Serializable]
    [CurrentDataVersion("1.0.0")]
    public class KvStoreContainer
    {
        public List<KvEntry> entries = new List<KvEntry>();
    }
}
