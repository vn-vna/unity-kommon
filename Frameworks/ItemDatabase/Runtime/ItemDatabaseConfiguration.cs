using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Central configuration for the Item Database framework.
    /// Stored at Assets/Resources/Integration/Managers/ItemDatabaseConfiguration.asset.
    /// If this asset does not exist, the module is considered disabled.
    /// </summary>
    [SingletonScriptableConfig(
        ScriptableLoadSource.Resources,
        "Integration/Managers/ItemDatabaseConfiguration")]
    public class ItemDatabaseConfiguration
        : SingletonScriptableObject<ItemDatabaseConfiguration>
    {
        [SerializeField]
        private ItemDefinition[] _itemDefinitions = Array.Empty<ItemDefinition>();

        [SerializeField]
        private TagDefinition[] _tagDefinitions = Array.Empty<TagDefinition>();

        [SerializeField]
        private string[] _middlewareTypeNames = Array.Empty<string>();

        public ItemDefinition[] ItemDefinitions
        {
            get => _itemDefinitions ?? Array.Empty<ItemDefinition>();
            set => _itemDefinitions = value ?? Array.Empty<ItemDefinition>();
        }

        public TagDefinition[] TagDefinitions
        {
            get => _tagDefinitions ?? Array.Empty<TagDefinition>();
            set => _tagDefinitions = value ?? Array.Empty<TagDefinition>();
        }

        /// <summary>
        /// Assembly-qualified type names for middleware classes.
        /// Each type must have [ItemDatabaseMiddleware] attribute.
        /// </summary>
        public string[] MiddlewareTypeNames
        {
            get => _middlewareTypeNames ?? Array.Empty<string>();
            set => _middlewareTypeNames = value ?? Array.Empty<string>();
        }

        /// <summary>Build a lookup dictionary: itemId → ItemDefinition.</summary>
        public Dictionary<string, ItemDefinition> BuildDefinitionLookup()
        {
            var lookup = new Dictionary<string, ItemDefinition>();
            foreach (var def in ItemDefinitions)
            {
                if (def != null && !string.IsNullOrEmpty(def.ItemId)
                    && !lookup.ContainsKey(def.ItemId))
                {
                    lookup[def.ItemId] = def;
                }
            }

            return lookup;
        }
    }
}
