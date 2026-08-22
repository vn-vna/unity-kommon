using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    [Serializable]
    public struct InventoryItem
    {
        public string key;           // Unique inventory key (GUID)
        public string itemId;        // References ItemDefinition.itemId
        public string customName;    // Optional display name override

        [NonSerialized]
        public DateTime createdAt;   // UTC timestamp

        [SerializeField]
        private long _createdAtUnixMilliseconds;

        [SerializeField]
        private bool _hasPersistedCreatedAt;

        public long CreatedAtUnixMilliseconds
        {
            get => _createdAtUnixMilliseconds;
            set => _createdAtUnixMilliseconds = value;
        }

        public bool HasPersistedCreatedAt
        {
            get => _hasPersistedCreatedAt;
            set => _hasPersistedCreatedAt = value;
        }

        public InventoryItem(string key, string itemId, string customName = null)
            : this(key, itemId, customName, DateTime.UtcNow)
        {
        }

        internal InventoryItem(
            string key,
            string itemId,
            string customName,
            DateTime createdAtUtc)
        {
            this.key = key;
            this.itemId = itemId;
            this.customName = customName;
            createdAt = ItemDatabaseTime.NormalizeUtc(createdAtUtc);
            _createdAtUnixMilliseconds = ItemDatabaseTime.ToUnixMilliseconds(createdAt);
            _hasPersistedCreatedAt = true;
        }

        internal void PrepareForSerialization()
        {
            if (createdAt == default)
            {
                throw new ItemDatabaseException(
                    $"Item '{key}' has no creation timestamp."
                );
            }

            createdAt = ItemDatabaseTime.NormalizeUtc(createdAt);
            _createdAtUnixMilliseconds = ItemDatabaseTime.ToUnixMilliseconds(createdAt);
            _hasPersistedCreatedAt = true;
        }

        internal void RestoreAfterDeserialization(DateTime fallbackUtc)
        {
            if (_hasPersistedCreatedAt)
            {
                createdAt = ItemDatabaseTime.FromUnixMilliseconds(
                    _createdAtUnixMilliseconds
                );
                return;
            }

            createdAt = ItemDatabaseTime.NormalizeUtc(fallbackUtc);
            PrepareForSerialization();
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
        public string tagId;          // stable TagDefinition identifier
        public string tagDefTypeName; // AssemblyQualifiedName of TagDefinition type
        public string jsonData;       // JsonUtility serialized ITagData (e.g., StackableData)
    }

    internal static class ItemDatabaseTime
    {
        internal static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default)
            {
                throw new ItemDatabaseException("UTC timestamp cannot be empty.");
            }

            switch (value.Kind)
            {
                case DateTimeKind.Utc:
                    return value;
                case DateTimeKind.Local:
                    return value.ToUniversalTime();
                default:
                    return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }
        }

        internal static long ToUnixMilliseconds(DateTime value)
        {
            return new DateTimeOffset(NormalizeUtc(value)).ToUnixTimeMilliseconds();
        }

        internal static DateTime FromUnixMilliseconds(long value)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
        }

        internal static DateTime ParseOrEpoch(string value)
        {
            if (DateTime.TryParse(
                    value,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
            {
                return NormalizeUtc(parsed);
            }

            return DateTime.UnixEpoch;
        }
    }
}
