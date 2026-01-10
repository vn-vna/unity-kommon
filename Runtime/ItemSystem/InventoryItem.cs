using System;

using UnityEngine;

namespace Com.Hapiga.Schehrazade.IS
{
    [Serializable]
    public class InventoryItem
    {
        #region Events & Delegates

        public event Action<InventoryItem> CountChanged;

        #endregion

        #region Interfaces & Properties

        public Inventory Inventory { get; set; }
        public ItemData ItemData { get; set; }
        public string ItemId => itemId;
        public string Id => id;

        public int Count
        {
            get => count;
            set
            {
                if (value <= 0 && ItemData.AutoDispose)
                {
                    count = 0;
                    Remove();
                    return;
                }

                if (value > ItemData.ItemDefinition.MaxStack) value = ItemData.ItemDefinition.MaxStack;

                count = value;
                CountChanged?.Invoke(this);
            }
        }

        public DateTime? Expired
        {
            get => string.IsNullOrEmpty(expired)
                ? null
                : DateTime.TryParse(expired, out var dateTime)
                    ? dateTime
                    : null;
            set => expired = value?.ToString("o") ?? null;
        }

        public IItemTraits ItemTraits
        {
            get => itemTraits;
            set => itemTraits = value ?? throw new ArgumentNullException(nameof(value));
        }

        #endregion

        #region Serialized Fields

        [SerializeField]
        protected string id;

        [SerializeField]
        protected string itemId;

        [SerializeField]
        protected int count;

        [SerializeField]
        protected string expired;

        [SerializeField]
        [SerializeReference]
        protected IItemTraits itemTraits;

        #endregion

        #region Public Methods

        public void Remove()
        {
            if (Inventory == null)
            {
                Debug.LogWarning("Cannot remove item: Inventory is null.");
                return;
            }

            Inventory.RemoveItem(this);
            Inventory = null;
        }

        public static InventoryItem Create(
            Inventory inventory,
            ItemData itemData,
            int count = 1,
            DateTime? expired = null)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (itemData == null) throw new ArgumentNullException(nameof(itemData));

            return new InventoryItem
            {
                Inventory = inventory,
                ItemData = itemData,
                Count = count,
                Expired = expired,
                itemId = itemData.ItemId,
                id = Guid.NewGuid().ToString()
            };
        }

        #endregion
    }
}