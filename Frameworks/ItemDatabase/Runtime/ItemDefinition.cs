using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Editor-defined schema describing what an item type is.
    /// Stored as a sub-asset of ItemDatabaseConfiguration.
    /// </summary>
    public class ItemDefinition : ScriptableObject
    {
#if UNITY_EDITOR
        [Tooltip("Unique identifier for this item type (e.g., 'gold', 'sword_01')")]
#endif
        [SerializeField]
        private string _itemId;

#if UNITY_EDITOR
        [Tooltip("Human-readable description")]
#endif
        [SerializeField]
        private string _description;

#if UNITY_EDITOR
        [Tooltip("Metadata with icons, extras, and attached tag definitions")]
#endif
        [SerializeField]
        private ItemMetadata _metadata;

        public string ItemId
        {
            get => _itemId;
            set => _itemId = value;
        }

        public string Description
        {
            get => _description;
            set => _description = value;
        }

        public ItemMetadata Metadata
        {
            get => _metadata;
            set => _metadata = value;
        }

        public TagDefinition[] Tags => _metadata != null
            ? _metadata.Tags
            : Array.Empty<TagDefinition>();
    }
}
