using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    public class ItemQueryResult
    {
        private readonly ItemDatabaseEngine _engine;
        private readonly List<InventoryItem> _items;

        internal ItemQueryResult(
            ItemDatabaseEngine engine,
            List<InventoryItem> items)
        {
            _engine = engine;
            _items = items;
        }

        public int Count => _items.Count;
        public InventoryItem[] Items => _items.ToArray();

        /// <summary>Get tag data (resolved via [TagData] attribute).</summary>
        public T GetTag<T>(int itemIndex = 0) where T : class, ITagData
        {
            if (_engine == null || itemIndex < 0 || itemIndex >= _items.Count) return null;
            var tagDefType = TagDataRegistry.GetTagDefType(typeof(T));
            return tagDefType != null
                ? _engine.GetTagData<T>(_items[itemIndex].key, tagDefType)
                : null;
        }

        /// <summary>Get all non-marker tag data for the item.</summary>
        public ITagData[] GetTags(int itemIndex = 0)
        {
            if (_engine == null || itemIndex < 0 || itemIndex >= _items.Count)
                return Array.Empty<ITagData>();
            return _engine.GetTagDatas(_items[itemIndex].key);
        }
    }
}
