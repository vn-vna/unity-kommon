using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    public sealed class AddItemRequest
    {
        public string ItemId { get; }

        public string RequestedKey { get; }

        public int Quantity { get; }

        public string CustomName { get; }

        public DateTime CreatedAtUtc { get; }

        public IReadOnlyList<ITagData> InitialTags
        {
            get
            {
                var tags = new ITagData[_capturedTags.Length];
                for (int i = 0; i < _capturedTags.Length; i++)
                {
                    CapturedTagData captured = _capturedTags[i];
                    tags[i] = ItemTagDataSerializer.Deserialize(
                        captured.Json,
                        captured.DataType
                    );
                }

                return tags;
            }
        }

        internal IReadOnlyList<CapturedTagData> CapturedTags => _capturedTags;

        private readonly CapturedTagData[] _capturedTags;

        public AddItemRequest(
            string itemId,
            int quantity = 1,
            string requestedKey = null,
            string customName = null,
            DateTime? createdAtUtc = null,
            IEnumerable<ITagData> initialTags = null)
        {
            ItemId = itemId;
            Quantity = quantity;
            RequestedKey = string.IsNullOrWhiteSpace(requestedKey)
                ? Guid.NewGuid().ToString("N")
                : requestedKey;
            CustomName = customName;
            CreatedAtUtc = ItemDatabaseTime.NormalizeUtc(
                createdAtUtc ?? DateTime.UtcNow
            );
            _capturedTags = CaptureTags(initialTags);
        }

        internal static AddItemRequest FromInventoryItem(InventoryItem item)
        {
            DateTime createdAt = item.createdAt == default
                ? DateTime.UtcNow
                : item.createdAt;

            return new AddItemRequest(
                item.itemId,
                1,
                item.key,
                item.customName,
                createdAt
            );
        }

        internal InventoryItem CreateInventoryItem(string key)
        {
            return new InventoryItem(
                key,
                ItemId,
                CustomName,
                CreatedAtUtc
            );
        }

        private static CapturedTagData[] CaptureTags(
            IEnumerable<ITagData> initialTags)
        {
            if (initialTags == null) return Array.Empty<CapturedTagData>();

            var result = new List<CapturedTagData>();
            var tagTypes = new HashSet<Type>();
            foreach (ITagData data in initialTags)
            {
                CapturedTagData captured = ItemTagDataSerializer.Capture(data);
                if (!tagTypes.Add(captured.TagDefinitionType))
                {
                    throw new ItemDatabaseException(
                        $"Duplicate initial tag '{captured.TagDefinitionType.Name}'."
                    );
                }

                result.Add(captured);
            }

            return result.ToArray();
        }
    }
}
