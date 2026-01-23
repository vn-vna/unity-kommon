using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.MappedList;
using UnityEngine;

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

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EditorRefreshDatabaseOnLoad()
        {
            var databases = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(ItemDatabase)}")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDatabase>)
                .Where(asset => asset != null);

            foreach (var database in databases)
            {
                database._itemMapping = null;
            }
        }
#endif
    }
}