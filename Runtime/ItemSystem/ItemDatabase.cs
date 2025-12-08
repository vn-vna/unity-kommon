using System;
using System.Collections.Generic;

using Com.Hapiga.Scheherazade.Common.MappedList;

using UnityEngine;

using System.Linq;

namespace Com.Hapiga.Schehrazade.IS
{
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        public MappedList<string, ItemData> ItemMapping
        {
            get
            {
                _itemMapping ??= new Lazy<MappedList<string, ItemData>>(BuildItemMapping);
                return _itemMapping.Value;
            }
        }

        [SerializeField]
        private List<ItemData> allItems;

        private Lazy<MappedList<string, ItemData>> _itemMapping;

        private MappedList<string, ItemData> BuildItemMapping()
        {
            return new MappedList<string, ItemData>(allItems, (item) => item.ItemId);
        }
    }
}