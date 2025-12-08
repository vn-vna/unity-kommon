using System;
using System.Collections.Generic;

using Com.Hapiga.Scheherazade.Common.Chrono;

using UnityEngine;

namespace Com.Hapiga.Schehrazade.IS
{
    public interface IItemTraits
    {
    }

    public abstract class ItemDefinition :
        ScriptableObject
    {
        public ItemData ItemData { get; set; }
        public string ItemName => itemName;
        public string ItemDescription => itemDescription;
        public int MaxStack => maxStack;

        [SerializeField]
        private string itemName;

        [SerializeField]
        [Multiline]
        private string itemDescription;

        [SerializeField]
        private int maxStack;

        public abstract Type ItemType { get; }
        public abstract Type TraitsType { get; }

        public virtual void AddItem(Inventory ingameInventory, int amount, DateTime? expiration)
        {
            var existingItems = ingameInventory.FindManyByItemId(ItemData.ItemId);
            if (amount <= 0) return;

            foreach (var existingItem in existingItems)
            {
                if (existingItem.Expired != expiration) continue;

                var remainingStack = existingItem.ItemData.ItemDefinition.MaxStack - existingItem.Count;
                var distributeCount = Math.Min(remainingStack, amount);
                existingItem.Count += distributeCount;
                amount -= distributeCount;
            }

            if (amount <= 0) return;
            if (!ingameInventory.ItemDatabase.ItemMapping.TryGetValue(ItemData.ItemId, out var data)) return;

            var newItem = InventoryItem.Create(ingameInventory, data, amount, expiration);
            ((IInventory)ingameInventory).AppendItem(newItem);
        }

        public virtual void AddItem(Inventory ingameInventory, int amount, TimeSpan? expiration)
        {
            DateTime? expiryDate = null;
            if (expiration.HasValue) expiryDate = ChronoDirector.Instance.TimeProvider.UtcNow.Add(expiration.Value);
            AddItem(ingameInventory, amount, expiryDate);
        }
    }

    public abstract class ItemDefinition<T>
        : ItemDefinition
    {
        public override Type ItemType => typeof(T);
        public override Type TraitsType => null;
    }

    public abstract class ItemDefinition<T, TTraits>
        : ItemDefinition<T>
        where T : ItemData
        where TTraits : IItemTraits
    {
        public override Type TraitsType => typeof(TTraits);
    }
}