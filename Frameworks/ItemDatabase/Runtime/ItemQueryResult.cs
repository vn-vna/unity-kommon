using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    public class ItemQueryResult
    {
        private readonly ItemQueryRecord[] _records;
        private readonly InventoryItem[] _items;

        internal ItemQueryResult(ItemQueryRecord[] records)
        {
            _records = records ?? Array.Empty<ItemQueryRecord>();
            _items = new InventoryItem[_records.Length];
            for (int i = 0; i < _records.Length; i++)
            {
                _items[i] = _records[i].Item;
            }
        }

        public int Count => _items.Length;

        public InventoryItem[] Items => (InventoryItem[])_items.Clone();

        /// <summary>Get tag data (resolved via [TagData] attribute).</summary>
        public T GetTag<T>(int itemIndex = 0) where T : class, ITagData
        {
            if (itemIndex < 0 || itemIndex >= _records.Length) return null;

            Type tagDefinitionType = TagDataRegistry.GetTagDefType(typeof(T));
            if (tagDefinitionType == null
                || !_records[itemIndex].Tags.TryGetValue(
                    tagDefinitionType,
                    out string json))
            {
                return null;
            }

            return ItemTagDataSerializer.Deserialize(json, typeof(T)) as T;
        }

        /// <summary>Get all non-marker tag data for the item.</summary>
        public ITagData[] GetTags(int itemIndex = 0)
        {
            if (itemIndex < 0 || itemIndex >= _records.Length)
                return Array.Empty<ITagData>();

            var result = new ITagData[_records[itemIndex].Tags.Count];
            int resultIndex = 0;
            foreach (var entry in _records[itemIndex].Tags)
            {
                Type dataType = TagDataRegistry.GetDataType(entry.Key);
                result[resultIndex++] = ItemTagDataSerializer.Deserialize(
                    entry.Value,
                    dataType
                );
            }

            return result;
        }
    }
}
