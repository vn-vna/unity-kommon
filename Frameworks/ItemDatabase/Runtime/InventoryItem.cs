using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    [Serializable]
    public struct InventoryItem
    {
        public string key;           // Unique inventory key (GUID)
        public string itemId;        // References ItemDefinition.itemId
        public string customName;    // Optional display name override
        public DateTime createdAt;   // UTC timestamp

        public InventoryItem(string key, string itemId, string customName = null)
        {
            this.key = key;
            this.itemId = itemId;
            this.customName = customName;
            this.createdAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Serialization entry for a tag attached to an inventory item.
    /// Only tags WITH data (non-marker) have entries here.
    /// The tagDefTypeName stores the TagDefinition type, and TagDataRegistry
    /// resolves which ITagData to deserialize.
    /// </summary>
    [Serializable]
    public struct InventoryTagEntry
    {
        public string key;            // links to InventoryItem.key
        public string tagDefTypeName; // AssemblyQualifiedName of TagDefinition type
        public string jsonData;       // JsonUtility serialized ITagData (e.g., StackableData)
    }
}
