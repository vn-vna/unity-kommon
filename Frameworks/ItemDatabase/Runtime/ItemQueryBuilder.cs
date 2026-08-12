using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Fluent, lock-free, synchronous query API over the engine store.
    /// </summary>
    public class ItemQueryBuilder
    {
        private readonly ItemDatabaseEngine _engine;
        private string _itemIdFilter;
        private Type _tagDefTypeFilter;
        private Func<InventoryItem, bool> _customPredicate;

        internal ItemQueryBuilder(ItemDatabaseEngine engine)
        {
            _engine = engine;
        }

        public ItemQueryBuilder WithDefinition(string itemId)
        {
            _itemIdFilter = itemId;
            return this;
        }

        /// <summary>Filter to items that have data for a specific tag type.</summary>
        public ItemQueryBuilder WithTag<T>() where T : ITagData
        {
            _tagDefTypeFilter = TagDataRegistry.GetTagDefType(typeof(T));
            return this;
        }

        public ItemQueryBuilder Where(Func<InventoryItem, bool> predicate)
        {
            _customPredicate = predicate;
            return this;
        }

        public ItemQueryResult Execute()
        {
            if (_engine == null)
                return new ItemQueryResult(null, new List<InventoryItem>());

            var results = new List<InventoryItem>();

            foreach (var kvp in _engine.Items)
            {
                var item = kvp.Value;

                if (_itemIdFilter != null && item.itemId != _itemIdFilter)
                    continue;

                if (_tagDefTypeFilter != null
                    && !_engine.HasTagData(item.key, _tagDefTypeFilter))
                    continue;

                if (_customPredicate != null && !_customPredicate(item))
                    continue;

                results.Add(item);
            }

            return new ItemQueryResult(_engine, results);
        }
    }
}
