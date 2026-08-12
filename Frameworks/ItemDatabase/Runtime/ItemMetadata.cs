using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Visual and semantic metadata for an item type.
    /// Stores the list of TagDefinitions that describe what
    /// runtime attributes this item type carries.
    /// </summary>
    public class ItemMetadata : ScriptableObject
    {
#if UNITY_EDITOR
        [Tooltip("Inventory icon")]
#endif
        [SerializeField]
        private Sprite _icon;

#if UNITY_EDITOR
        [Tooltip("Tag definitions that describe this item's attributes")]
#endif
        [SerializeField]
        private TagDefinition[] _tags;

#if UNITY_EDITOR
        [Tooltip("Arbitrary key-value extras (JSON string)")]
#endif
        [SerializeField]
        private string _extras;

        public Sprite Icon
        {
            get => _icon;
            set => _icon = value;
        }

        public TagDefinition[] Tags
        {
            get => _tags ?? Array.Empty<TagDefinition>();
            set => _tags = value ?? Array.Empty<TagDefinition>();
        }

        public string Extras
        {
            get => _extras;
            set => _extras = value;
        }
    }
}
