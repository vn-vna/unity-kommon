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
        private const string CanonicalResourcesPath
            = "Integration/Managers/ItemDatabaseConfiguration";

        private static ItemDatabaseConfiguration _canonicalConfiguration;
        private static bool _canonicalLoadAttempted;

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
            var lookup = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            foreach (var def in ItemDefinitions)
            {
                if (def == null)
                {
                    throw new ItemDatabaseException(
                        "Item Database configuration contains a missing definition reference."
                    );
                }

                if (string.IsNullOrWhiteSpace(def.ItemId))
                {
                    throw new ItemDatabaseException(
                        $"Definition '{def.name}' has an empty item ID."
                    );
                }

                if (!lookup.TryAdd(def.ItemId, def))
                {
                    throw new ItemDatabaseException(
                        $"Duplicate item definition ID '{def.ItemId}'."
                    );
                }
            }

            return lookup;
        }

        internal static ItemDatabaseConfiguration LoadCanonical()
        {
            if (!_canonicalLoadAttempted)
            {
                _canonicalConfiguration
                    = Resources.Load<ItemDatabaseConfiguration>(
                        CanonicalResourcesPath
                    );
                _canonicalLoadAttempted = true;
            }

            Instance = _canonicalConfiguration;
            return _canonicalConfiguration;
        }

        internal static void SetCanonical(
            ItemDatabaseConfiguration configuration)
        {
            _canonicalConfiguration = configuration;
            _canonicalLoadAttempted = true;
            Instance = configuration;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCanonical()
        {
            _canonicalConfiguration = null;
            _canonicalLoadAttempted = false;
        }
    }
}
