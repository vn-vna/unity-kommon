using System;

using Com.Hapiga.FallAway.Inventory;

using UnityEngine;

namespace Com.Hapiga.Schehrazade.IS
{
    [Serializable]
    public class ItemData
    {
        #region Interfaces

        public string ItemId => itemId;
        public string ItemName => itemDefinition.ItemName;
        public string ItemDescription => itemDefinition.ItemDescription;
        public ItemDefinition ItemDefinition => itemDefinition;
        public bool AutoDispose => autoDispose;

        #endregion

        #region Serialized Fields

        [SerializeField]
        private string itemId;

        [SerializeField]
        private ItemDefinition itemDefinition;

        [SerializeField]
        private bool autoDispose = true;

        #endregion
    }

    public class ItemData<T> : ItemData
        where T : ItemDefinition<T>
    {
        public new T ItemDefinition => base.ItemDefinition as T;
    }
}