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
        private bool _hasInvalidFilter;

        internal ItemQueryBuilder(ItemDatabaseEngine engine)
        {
            _engine = engine;
        }

        public ItemQueryBuilder WithDefinition(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                _hasInvalidFilter = true;
                _itemIdFilter = null;
                return this;
            }

            _itemIdFilter = itemId;
            return this;
        }

        /// <summary>Filter to items that have data for a specific tag type.</summary>
        public ItemQueryBuilder WithTag<T>() where T : ITagData
        {
            _tagDefTypeFilter = TagDataRegistry.GetTagDefType(typeof(T));
            if (_tagDefTypeFilter == null) _hasInvalidFilter = true;
            return this;
        }

        public ItemQueryBuilder Where(Func<InventoryItem, bool> predicate)
        {
            if (predicate == null)
            {
                _hasInvalidFilter = true;
                return this;
            }

            _customPredicate = predicate;
            return this;
        }

        public ItemQueryResult Execute()
        {
            if (_engine == null || _hasInvalidFilter)
            {
                return new ItemQueryResult(Array.Empty<ItemQueryRecord>());
            }

            ItemQueryRecord[] records = _engine.CaptureQueryRecords(
                _itemIdFilter,
                _tagDefTypeFilter
            );
            if (_customPredicate == null)
            {
                return new ItemQueryResult(records);
            }

            var results = new List<ItemQueryRecord>(records.Length);
            foreach (ItemQueryRecord record in records)
            {
                if (_customPredicate(record.Item)) results.Add(record);
            }

            return new ItemQueryResult(results.ToArray());
        }
    }
}
