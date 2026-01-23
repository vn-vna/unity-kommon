using System;
using System.Collections.Generic;

using Com.Hapiga.FallAway.Inventory;
using Com.Hapiga.Scheherazade.Common.Chrono;
using Com.Hapiga.Scheherazade.Common.LocalSave;

using UnityEngine;

namespace Com.Hapiga.Schehrazade.IS
{
    public interface IInventory
    {
        void AppendItem(InventoryItem item);
    }

    public abstract class Inventory :
        IVersionedData,
        ISerializationCallbackReceiver,
        IInventory
    {
        #region Events & Delegates

        public event Action<InventoryItem> ItemAdded;
        public event Action<InventoryItem> ItemRemoved;

        #endregion

        #region Interfaces & Properties

        public LinkedList<InventoryItem> Items => _inventoryItems;
        public abstract ItemDatabase ItemDatabase { get; }

        #endregion

        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        internal List<InventoryItem> items;

        #endregion

        #region Private Fields

        internal LinkedList<InventoryItem> _inventoryItems;
        internal Queue<InventoryItem> _removeQueue;
        internal Dictionary<string, LinkedList<InventoryItem>> _itemLookup;
        internal Dictionary<Type, LinkedList<InventoryItem>> _itemLookupByType;
        internal Dictionary<string, InventoryItem> _itemLookupById;

        #endregion

        #region CTor

        internal Inventory()
        {
        }

        #endregion

        #region Public Methods

        public InventoryItem FindOneByType<T>()
            where T : ItemDefinition
        {
            if (_itemLookupByType.TryGetValue(typeof(T), out var items) && items.Count > 0) return items.First.Value;
            return null;
        }

        public IEnumerable<InventoryItem> FindManyByType<T>()
            where T : ItemDefinition
        {
            return FindManyByType(typeof(T));
        }

        public IEnumerable<InventoryItem> FindManyByType(Type type)
        {
            if (_itemLookupByType.TryGetValue(type, out var items))
                foreach (var item in items)
                    yield return item;
        }

        public int CountByType<T>()
            where T : ItemDefinition
        {
            return CountByType(typeof(T));
        }

        public int CountByType(Type type)
        {
            if (_itemLookupByType.TryGetValue(type, out var items)) return items.Count;
            return 0;
        }

        public int CountStackByType<T>()
            where T : ItemDefinition
        {
            return CountStackByType(typeof(T));
        }

        public int CountStackByType(Type type)
        {
            if (_itemLookupByType.TryGetValue(type, out var items))
            {
                var totalCount = 0;
                foreach (var item in items) totalCount += item.Count;
                return totalCount;
            }

            return 0;
        }

        public int CountStackByItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentNullException(nameof(itemId));
            if (_itemLookup.TryGetValue(itemId, out var items))
            {
                var totalCount = 0;
                foreach (var item in items) totalCount += item.Count;
                return totalCount;
            }

            return 0;
        }

        public InventoryItem FindOneByItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentNullException(nameof(itemId));
            if (_itemLookup.TryGetValue(itemId, out var items)) return null;
            return items.First.Value;
        }

        public IEnumerable<InventoryItem> FindManyByItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentNullException(nameof(itemId));
            if (_itemLookup.TryGetValue(itemId, out var items))
                foreach (var item in items)
                    yield return item;
        }

        public int CountByItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentNullException(nameof(itemId));
            if (_itemLookup.TryGetValue(itemId, out var items)) return items.Count;
            return 0;
        }

        public InventoryItem GetItem(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            if (_itemLookupById.TryGetValue(id, out var item))
                return item;

            return null;
        }

        public void AddItem(ItemDefinition def, int count = 1, DateTime? expired = null)
        {
            def.AddItem(this, count, expired);
        }

        public void AddItem(string itemId, int count = 1, DateTime? expired = null)
        {
            if (!ItemDatabase.ItemMapping.TryGetValue(itemId, out var data)) return;
            AddItem(data.ItemDefinition, count, expired);
        }

        public void AddItem(ItemDefinition def, int count = 1, TimeSpan? expired = null)
        {
            def.AddItem(this, count, expired);
        }

        public void AddItem(string itemId, int count = 1, TimeSpan? expired = null)
        {
            if (!ItemDatabase.ItemMapping.TryGetValue(itemId, out var data)) return;
            AddItem(data.ItemDefinition, count, expired);
        }

        public void RemoveItem(InventoryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.Inventory != this) throw new InvalidOperationException("Item does not belong to this inventory.");
            _removeQueue.Enqueue(item);
            ItemRemoved?.Invoke(item);
        }

        public abstract bool UpdateInventory();

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            _inventoryItems ??= new LinkedList<InventoryItem>();
            items = new List<InventoryItem>(_inventoryItems);
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _inventoryItems = new LinkedList<InventoryItem>(items);
            _removeQueue = new Queue<InventoryItem>();
            _itemLookup = new Dictionary<string, LinkedList<InventoryItem>>();
            _itemLookupByType = new Dictionary<Type, LinkedList<InventoryItem>>();
            _itemLookupById = new Dictionary<string, InventoryItem>();

            foreach (var item in _inventoryItems)
            {
                item.Inventory = this;
                item.ItemData = ItemDatabase.ItemMapping[item.ItemId];
            }

            foreach (var item in _inventoryItems)
            {
                if (!_itemLookup.ContainsKey(item.ItemId)) _itemLookup[item.ItemId] = new LinkedList<InventoryItem>();
                _itemLookup[item.ItemId].AddLast(item);

                if (!_itemLookupByType.ContainsKey(item.ItemData.ItemDefinition.GetType()))
                    _itemLookupByType[item.ItemData.ItemDefinition.GetType()] = new LinkedList<InventoryItem>();
                _itemLookupByType[item.ItemData.ItemDefinition.GetType()].AddLast(item);

                _itemLookupById[item.Id] = item;
            }
        }

        void IInventory.AppendItem(InventoryItem item)
        {
            _inventoryItems.AddLast(item);
            if (!_itemLookup.ContainsKey(item.ItemId)) _itemLookup[item.ItemId] = new LinkedList<InventoryItem>();
            _itemLookup[item.ItemId].AddLast(item);

            if (!_itemLookupByType.ContainsKey(item.ItemData.ItemDefinition.GetType()))
                _itemLookupByType[item.ItemData.ItemDefinition.GetType()] = new LinkedList<InventoryItem>();
            _itemLookupByType[item.ItemData.ItemDefinition.GetType()].AddLast(item);

            _itemLookupById[item.Id] = item;

            ItemAdded?.Invoke(item);
        }

        #endregion
    }

    public abstract class Inventory<SelfT> : Inventory
    {
        public override bool UpdateInventory()
        {
            // Check item expiration
            var now = ChronoDirector.Instance.TimeProvider.UtcNow;
            foreach (var item in _inventoryItems)
                if (item.Expired.HasValue && item.Expired.Value < now)
                    RemoveItem(item);

            // Resolve all items in the remove queue
            var changed = _removeQueue.Count > 0;
            while (_removeQueue.Count > 0)
            {
                var item = _removeQueue.Dequeue();
                if (item == null) continue;

                _inventoryItems.Remove(item);
                _itemLookup[item.ItemId].Remove(item);
                _itemLookupByType[item.ItemData.ItemDefinition.GetType()].Remove(item);
                _itemLookupById.Remove(item.Id);
                item.Inventory = null;
            }

            return changed;
        }
    }
}