using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.DataSync;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Full persistent state of the item database.
    /// Saved via DataSyncDirector.SaveAsync("item_db", state).
    /// </summary>
    [Serializable]
    [CurrentDataVersion("1.0.0")]
    public class ItemDatabaseState
    {
        public List<InventoryItem> items = new List<InventoryItem>();

        public List<InventoryTagEntry> tags = new List<InventoryTagEntry>();

        public string savedAtUtc;
    }
}
