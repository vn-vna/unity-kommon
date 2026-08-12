using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Editor
{
    /// <summary>
    /// Shared helpers for managing per-definition TagDefinition sub-assets.
    /// Used by both the custom ItemDefinitionEditor and the settings provider.
    /// </summary>
    internal static class ItemDefinitionTagUtils
    {
        /// <summary>Finds the default template for a tag type (Tags tab).</summary>
        internal static TagDefinition FindDefaultTemplate(ItemDatabaseConfiguration config, Type tagType)
        {
            var configured = config.TagDefinitions ?? Array.Empty<TagDefinition>();
            return configured.FirstOrDefault(t => t != null && t.GetType() == tagType);
        }

        /// <summary>True when the tag points at the default template (not a per-definition clone).</summary>
        internal static bool IsSharedTemplate(ItemDatabaseConfiguration config, TagDefinition tag)
        {
            var configured = config.TagDefinitions ?? Array.Empty<TagDefinition>();
            return configured.Contains(tag);
        }

        /// <summary>
        /// Creates a per-definition TagDefinition sub-asset.
        /// Seeded from the tag type's default template (Tags tab) and
        /// named with the definition-id postfix, e.g. "StackableTag_gold".
        /// </summary>
        internal static TagDefinition CreateTagInstance(ItemDatabaseConfiguration config, Type tagType, string definitionId)
        {
            var tag = (TagDefinition)ScriptableObject.CreateInstance(tagType);

            var template = FindDefaultTemplate(config, tagType);
            if (template != null) EditorUtility.CopySerialized(template, tag);

            tag.name = $"{tagType.Name}_{definitionId}";
            AssetDatabase.AddObjectToAsset(tag, config);
            return tag;
        }

        internal static void AddTagToDefinition(ItemDatabaseConfiguration config, ItemDefinition def, Type tagType)
        {
            var metadata = EnsureMetadata(config, def);
            var tag = CreateTagInstance(config, tagType, def.ItemId);
            var list = new List<TagDefinition>(metadata.Tags ?? Array.Empty<TagDefinition>())
            { tag };
            metadata.Tags = list.ToArray();
            MarkDirty(config);
        }

        /// <summary>Converts a definition tag that still points at the default template into its own clone.</summary>
        internal static void CloneTagIntoDefinition(ItemDatabaseConfiguration config, ItemDefinition def, TagDefinition sharedTag)
        {
            var metadata = EnsureMetadata(config, def);
            var clone = CreateTagInstance(config, sharedTag.GetType(), def.ItemId);
            EditorUtility.CopySerialized(sharedTag, clone);

            var list = new List<TagDefinition>(metadata.Tags ?? Array.Empty<TagDefinition>());
            int index = list.IndexOf(sharedTag);
            if (index >= 0)
            {
                list[index] = clone;
                metadata.Tags = list.ToArray();
                MarkDirty(config);
            }
        }

        internal static void RemoveTagFromDefinition(ItemDatabaseConfiguration config, ItemDefinition def, TagDefinition tag)
        {
            var metadata = def.Metadata;
            if (metadata == null) return;

            var list = new List<TagDefinition>(metadata.Tags ?? Array.Empty<TagDefinition>());
            if (list.Remove(tag))
            {
                metadata.Tags = list.ToArray();
                MarkDirty(config);
            }
        }

        internal static ItemMetadata EnsureMetadata(ItemDatabaseConfiguration config, ItemDefinition def)
        {
            if (def.Metadata != null) return def.Metadata;

            var metadata = ScriptableObject.CreateInstance<ItemMetadata>();
            metadata.name = $"{def.ItemId}_meta";
            AssetDatabase.AddObjectToAsset(metadata, config);
            def.Metadata = metadata;
            return metadata;
        }

        /// <summary>
        /// Destroys all per-definition tag sub-assets owned by the definition
        /// (keeps default templates alive). Call before deleting a definition.
        /// </summary>
        internal static void DestroyDefinitionTags(ItemDatabaseConfiguration config, ItemDefinition def)
        {
            var metadata = def.Metadata;
            if (metadata == null) return;

            foreach (var tag in metadata.Tags ?? Array.Empty<TagDefinition>())
            {
                if (tag != null && !IsSharedTemplate(config, tag)) DestroySubAsset(tag);
            }
        }

        internal static void DestroySubAsset(Object target)
        {
            if (target == null) return;
            AssetDatabase.RemoveObjectFromAsset(target);
            Object.DestroyImmediate(target, true);
        }

        internal static void MarkDirty(ItemDatabaseConfiguration config)
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!string.IsNullOrEmpty(assetPath)) AssetDatabase.ImportAsset(assetPath);
        }
    }
}
