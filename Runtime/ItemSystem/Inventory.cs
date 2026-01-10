using System;
using System.Collections.Generic;

using Com.Hapiga.FallAway.Inventory;
using Com.Hapiga.Scheherazade.Common.Chrono;
using Com.Hapiga.Scheherazade.Common.LocalSave;
using Com.Hapiga.Scheherazade.Common.Logging;
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
            if (_itemLookupByType.TryGetValue(typeof(T), out LinkedList<InventoryItem> items) && items.Count > 0) return items.First.Value;
            return null;
        }

        public IEnumerable<InventoryItem> FindManyByType<T>()
            where T : ItemDefinition
        {
            return FindManyByType(typeof(T));
        }

        public IEnumerable<InventoryItem> FindManyByType(Type type)
        {
            if (_itemLookupByType.TryGetValue(type, out LinkedList<InventoryItem> items))
                foreach (InventoryItem item in items)
                    yield return item;
        }

        public int CountByType<T>()
            where T : ItemDefinition
        {
            return CountByType(typeof(T));
        }

        public int CountByType(Type type)
        {
            if (_itemLookupByType.TryGetValue(type, out LinkedList<InventoryItem> items)) return items.Count;
            return 0;
        }

        public int CountStackByType<T>()
            where T : ItemDefinition
        {
            return CountStackByType(typeof(T));
        }

        public int CountStackByType(Type type)
        {
            if (_itemLookupByType.TryGetValue(type, out LinkedList<InventoryItem> items))
            {
                var totalCount = 0;
                foreach (InventoryItem item in items) totalCount += item.Count;
                return totalCount;
            }

            return 0;
        }

        public int CountStackByItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentNullException(nameof(itemId));
            if (_itemLookup.TryGetValue(itemId, out LinkedList<InventoryItem> items))
            {
                var totalCount = 0;
                foreach (InventoryItem item in items) totalCount += item.Count;
                return totalCount;
            }

            return 0;
        }

        public InventoryItem FindOneByItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentNullException(nameof(itemId));
            if (!_itemLookup.TryGetValue(itemId, out LinkedList<InventoryItem> items)) return null;
            return items.First.Value;
        }

        public IEnumerable<InventoryItem> FindManyByItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentNullException(nameof(itemId));
            if (_itemLookup.TryGetValue(itemId, out LinkedList<InventoryItem> items))
                foreach (InventoryItem item in items)
                    yield return item;
        }

        public int CountByItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentNullException(nameof(itemId));
            if (_itemLookup.TryGetValue(itemId, out LinkedList<InventoryItem> items)) return items.Count;
            return 0;
        }

        public InventoryItem GetItem(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            foreach (InventoryItem item in _inventoryItems)
                if (item.Id == id)
                    return item;

            return null;
        }

        public void AddItem(ItemDefinition def, int count = 1, DateTime? expired = null)
        {
            def.AddItem(this, count, expired);
        }

        public void AddItem(string itemId, int count = 1, DateTime? expired = null)
        {
            if (!ItemDatabase.ItemMapping.TryGetValue(itemId, out ItemData data)) return;
            AddItem(data.ItemDefinition, count, expired);
        }

        public void AddItem(ItemDefinition def, int count = 1, TimeSpan? expired = null)
        {
            def.AddItem(this, count, expired);
        }

        public void AddItem(string itemId, int count = 1, TimeSpan? expired = null)
        {
            if (!ItemDatabase.ItemMapping.TryGetValue(itemId, out ItemData data)) return;
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

            foreach (InventoryItem item in _inventoryItems)
            {
                item.Inventory = this;
                item.ItemData = ItemDatabase.ItemMapping[item.ItemId];
            }

            foreach (InventoryItem item in _inventoryItems)
            {
                if (!_itemLookup.ContainsKey(item.ItemId)) _itemLookup[item.ItemId] = new LinkedList<InventoryItem>();
                _itemLookup[item.ItemId].AddLast(item);

                if (!_itemLookupByType.ContainsKey(item.ItemData.ItemDefinition.GetType()))
                    _itemLookupByType[item.ItemData.ItemDefinition.GetType()] = new LinkedList<InventoryItem>();
                _itemLookupByType[item.ItemData.ItemDefinition.GetType()].AddLast(item);
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

            ItemAdded?.Invoke(item);
        }

        #endregion
    }

    public abstract class Inventory<SelfT> : Inventory
    {
        public override bool UpdateInventory()
        {
            // Check item expiration
            DateTime now = ChronoDirector.Instance.TimeProvider.UtcNow;

            foreach (InventoryItem item in _inventoryItems)
            {
                try
                {
                    if (!item.Expired.HasValue || item.Expired.Value >= now) continue;
                    RemoveItem(item);
                }
                catch (Exception ex)
                {
                    QuickLog.Critical<Inventory<SelfT>>(
                        "Failed to check expiration for item {0}: {1}",
                        item.Id, ex
                    );
                }
            }

            // Resolve all items in the remove queue
            var changed = _removeQueue.Count > 0;
            while (_removeQueue.Count > 0)
            {
                try
                {
                    InventoryItem item = _removeQueue.Dequeue();
                    if (item == null) continue;
                    RemoveItemFromTypeLookupCache(item);
                    RemoveItemFromLookupCache(item);
                    item.Inventory = null;
                }
                catch (Exception ex)
                {
                    QuickLog.Critical<Inventory<SelfT>>(
                        "Failed to remove item from inventory: {0}",
                        ex
                    );
                }
            }

            return changed;
        }

        private void RemoveItemFromLookupCache(InventoryItem item)
        {
            if (_itemLookupByType.TryGetValue(
                item.ItemData.ItemDefinition.GetType(),
                out LinkedList<InventoryItem> typeList)
            )
            {
                typeList.Remove(item);
                if (typeList.Count == 0)
                {
                    _itemLookupByType.Remove(item.ItemData.ItemDefinition.GetType());
                }
            }
        }

        private void RemoveItemFromTypeLookupCache(InventoryItem item)
        {
            _inventoryItems.Remove(item);
            if (_itemLookup.TryGetValue(item.ItemId, out LinkedList<InventoryItem> itemList))
            {
                itemList.Remove(item);
                if (itemList.Count == 0)
                {
                    _itemLookup.Remove(item.ItemId);
                }
            }
        }
    }
}