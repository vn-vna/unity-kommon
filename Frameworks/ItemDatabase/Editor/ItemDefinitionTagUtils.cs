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
        internal static TagDefinition CreateTagInstance(
            ItemDatabaseConfiguration config,
            Type tagType,
            string definitionId)
        {
            EnsureEditable(config);
            if (tagType == null
                || tagType.IsAbstract
                || !typeof(TagDefinition).IsAssignableFrom(tagType))
            {
                throw new ArgumentException("A concrete TagDefinition type is required.");
            }

            EnsureUnowned(tagType, tagType.Name);

            var tag = (TagDefinition)ScriptableObject.CreateInstance(tagType);
            AssetDatabase.AddObjectToAsset(tag, config);

            var template = FindDefaultTemplate(config, tagType);
            if (template != null) EditorUtility.CopySerialized(template, tag);

            tag.name = $"{tagType.Name}_{definitionId}";
            Undo.RegisterCreatedObjectUndo(tag, $"Create {tagType.Name}");
            return tag;
        }

        internal static void AddTagToDefinition(
            ItemDatabaseConfiguration config,
            ItemDefinition def,
            Type tagType)
        {
            config = ResolveConfiguration(config, def);
            EnsureEditable(config);
            if (def == null) throw new ArgumentNullException(nameof(def));
            EnsureDefinitionUnowned(def);
            if (def.Tags.Any(tag => tag != null && tag.GetType() == tagType)) return;

            int undoGroup = BeginUndoGroup($"Add {tagType.Name} to {def.ItemId}");
            ItemMetadata metadata = EnsureMetadata(config, def);

            TagDefinition tagInstance = CreateTagInstance(
                config,
                tagType,
                def.ItemId
            );
            Undo.RegisterCompleteObjectUndo(metadata, "Add Item Tag");
            var tags = new List<TagDefinition>(metadata.Tags) { tagInstance };
            metadata.Tags = tags.ToArray();
            MarkDirty(config, def, metadata, tagInstance);
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>Converts a definition tag that still points at the default template into its own clone.</summary>
        internal static void CloneTagIntoDefinition(
            ItemDatabaseConfiguration config,
            ItemDefinition def,
            TagDefinition sharedTag)
        {
            config = ResolveConfiguration(config, def);
            EnsureEditable(config);
            if (def == null || sharedTag == null) return;
            EnsureDefinitionUnowned(def);

            ItemMetadata metadata = EnsureMetadata(config, def);
            var tags = new List<TagDefinition>(metadata.Tags);
            int index = tags.IndexOf(sharedTag);
            if (index < 0) return;

            int undoGroup = BeginUndoGroup(
                $"Clone {sharedTag.GetType().Name} for {def.ItemId}"
            );
            TagDefinition clone = CreateTagClone(
                config,
                sharedTag,
                def.ItemId
            );
            Undo.RegisterCompleteObjectUndo(metadata, "Clone Item Tag");
            tags[index] = clone;
            metadata.Tags = tags.ToArray();
            MarkDirty(config, def, metadata, clone);
            Undo.CollapseUndoOperations(undoGroup);
        }

        internal static void RemoveTagFromDefinition(
            ItemDatabaseConfiguration config,
            ItemDefinition def,
            TagDefinition tag)
        {
            config = ResolveConfiguration(config, def);
            EnsureEditable(config);
            EnsureUnowned(tag);
            EnsureDefinitionUnowned(def);

            ItemMetadata metadata = def?.Metadata;
            if (metadata == null) return;
            EnsureMetadataHasSingleOwner(config, def, metadata);
            EnsureOwnedSubAsset(config, metadata, "metadata");

            var tags = new List<TagDefinition>(metadata.Tags);
            if (!tags.Remove(tag)) return;

            int undoGroup = BeginUndoGroup(
                $"Remove {tag?.GetType().Name ?? "Tag"} from {def.ItemId}"
            );
            Undo.RegisterCompleteObjectUndo(metadata, "Remove Item Tag");
            metadata.Tags = tags.ToArray();
            MarkDirty(config, def, metadata);

            Undo.CollapseUndoOperations(undoGroup);
        }

        internal static ItemMetadata EnsureMetadata(ItemDatabaseConfiguration config, ItemDefinition def)
        {
            config = ResolveConfiguration(config, def);
            EnsureEditable(config);
            if (def == null) throw new ArgumentNullException(nameof(def));
            EnsureDefinitionUnowned(def);
            if (def.Metadata != null)
            {
                EnsureMetadataHasSingleOwner(config, def, def.Metadata);
                EnsureOwnedSubAsset(config, def.Metadata, "metadata");
                return def.Metadata;
            }

            var metadata = ScriptableObject.CreateInstance<ItemMetadata>();
            AssetDatabase.AddObjectToAsset(metadata, config);
            metadata.name = $"{def.ItemId}_meta";
            Undo.RegisterCreatedObjectUndo(metadata, "Create Item Metadata");
            Undo.RegisterCompleteObjectUndo(def, "Create Item Metadata");
            def.Metadata = metadata;
            MarkDirty(config, def, metadata);
            return metadata;
        }

        internal static ItemMetadata CloneSharedMetadataForDefinition(
            ItemDatabaseConfiguration config,
            ItemDefinition definition)
        {
            config = ResolveConfiguration(config, definition);
            EnsureEditable(config);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            EnsureDefinitionUnowned(definition);

            ItemMetadata source = definition.Metadata;
            if (source == null) return EnsureMetadata(config, definition);
            if (IsOwnedSubAsset(config, source)
                && !IsMetadataReferencedByOtherDefinition(
                    config,
                    definition,
                    source))
            {
                return source;
            }

            int undoGroup = BeginUndoGroup(
                $"Clone Metadata for {definition.ItemId}"
            );
            var clone = ScriptableObject.CreateInstance<ItemMetadata>();
            AssetDatabase.AddObjectToAsset(clone, config);
            EditorUtility.CopySerialized(source, clone);
            clone.name = $"{definition.ItemId}_meta";

            var clonedTags = new List<TagDefinition>();
            foreach (TagDefinition sourceTag in source.Tags)
            {
                if (sourceTag == null) continue;

                TagDefinition tagClone = CreateTagClone(
                    config,
                    sourceTag,
                    definition.ItemId
                );
                clonedTags.Add(tagClone);
            }

            clone.Tags = clonedTags.ToArray();
            Undo.RegisterCreatedObjectUndo(clone, "Clone Item Metadata");
            Undo.RegisterCompleteObjectUndo(definition, "Clone Item Metadata");
            definition.Metadata = clone;
            MarkDirty(
                new Object[] { config, definition, clone }
                    .Concat(clonedTags)
                    .ToArray()
            );
            Undo.CollapseUndoOperations(undoGroup);
            return clone;
        }

        private static TagDefinition CreateTagClone(
            ItemDatabaseConfiguration config,
            TagDefinition source,
            string definitionId)
        {
            Type tagType = source.GetType();
            EnsureUnowned(tagType, tagType.Name);
            var clone = (TagDefinition)ScriptableObject.CreateInstance(tagType);
            AssetDatabase.AddObjectToAsset(clone, config);
            EditorUtility.CopySerialized(source, clone);
            clone.name = $"{tagType.Name}_{definitionId}";
            Undo.RegisterCreatedObjectUndo(clone, "Clone Item Tag");
            return clone;
        }

        internal static void DeleteDefinition(
            ItemDatabaseConfiguration config,
            ItemDefinition definition)
        {
            ValidateHost(config);
            EnsureEditable(config);
            if (definition == null) return;

            string owner = ItemDatabase.GetDefinitionOwner(definition);
            if (!string.IsNullOrEmpty(owner)
                && IsOwnedSubAsset(config, definition))
            {
                throw new ModuleOwnedEntityException(
                    definition.ItemId,
                    owner
                );
            }

            var definitions = new List<ItemDefinition>(config.ItemDefinitions);
            if (!definitions.Remove(definition)) return;

            int undoGroup = BeginUndoGroup(
                $"Delete Item Definition {definition.ItemId}"
            );
            Undo.RegisterCompleteObjectUndo(config, "Delete Item Definition");
            config.ItemDefinitions = definitions.ToArray();
            MarkDirty(config);
            Undo.CollapseUndoOperations(undoGroup);
        }

        internal static TagDefinition CreateDefaultTemplate(
            ItemDatabaseConfiguration config,
            Type tagType)
        {
            ValidateHost(config);
            EnsureEditable(config);
            EnsureUnowned(tagType, tagType?.Name);
            TagDefinition existing = FindDefaultTemplate(config, tagType);
            if (existing != null) return existing;

            int undoGroup = BeginUndoGroup($"Create {tagType.Name} Defaults");
            var tag = (TagDefinition)ScriptableObject.CreateInstance(tagType);
            AssetDatabase.AddObjectToAsset(tag, config);
            tag.name = $"{tagType.Name}_defaults";
            Undo.RegisterCreatedObjectUndo(tag, "Create Tag Defaults");
            Undo.RegisterCompleteObjectUndo(config, "Create Tag Defaults");

            var defaults = new List<TagDefinition>(config.TagDefinitions) { tag };
            config.TagDefinitions = defaults.ToArray();
            MarkDirty(config, tag);
            Undo.CollapseUndoOperations(undoGroup);
            return tag;
        }

        internal static void ClearDefaultTemplate(
            ItemDatabaseConfiguration config,
            TagDefinition template,
            bool materializeReferences)
        {
            ValidateHost(config);
            EnsureEditable(config);
            if (template == null || !IsSharedTemplate(config, template)) return;
            if (IsOwnedSubAsset(config, template)) EnsureUnowned(template);

            ItemDefinition[] references = GetDefinitionReferences(config, template);
            if (references.Length > 0 && !materializeReferences)
            {
                throw new InvalidOperationException(
                    $"Default '{template.GetType().Name}' is referenced by "
                    + $"{references.Length} definitions. Clone references before clearing it."
                );
            }

            if (references.Length > 0)
            {
                EnsureUnowned(template);
                foreach (ItemDefinition definition in references)
                {
                    EnsureDefinitionUnowned(definition);
                    if (RequiresMetadataClone(config, definition))
                    {
                        throw new InvalidOperationException(
                            $"Definition '{definition.ItemId}' has shared or "
                            + "external metadata. Clone its metadata before "
                            + "clearing the default."
                        );
                    }
                }
            }

            int undoGroup = BeginUndoGroup(
                $"Clear {template.GetType().Name} Defaults"
            );
            foreach (ItemDefinition definition in references)
            {
                CloneTagIntoDefinition(config, definition, template);
            }

            Undo.RegisterCompleteObjectUndo(config, "Clear Tag Defaults");
            var defaults = new List<TagDefinition>(config.TagDefinitions);
            defaults.Remove(template);
            config.TagDefinitions = defaults.ToArray();
            MarkDirty(config);

            Undo.CollapseUndoOperations(undoGroup);
        }

        internal static ItemDefinition[] GetDefinitionReferences(
            ItemDatabaseConfiguration config,
            TagDefinition tag)
        {
            if (config == null || tag == null) return Array.Empty<ItemDefinition>();
            return config.ItemDefinitions
                .Where(definition => definition != null
                    && definition.Tags.Contains(tag))
                .ToArray();
        }

        internal static bool RequiresMetadataClone(
            ItemDatabaseConfiguration config,
            ItemDefinition definition)
        {
            ItemMetadata metadata = definition?.Metadata;
            return metadata != null
                && (!IsOwnedSubAsset(config, metadata)
                    || IsMetadataReferencedByOtherDefinition(
                        config,
                        definition,
                        metadata
                    ));
        }

        internal static bool IsOwnedSubAsset(
            ItemDatabaseConfiguration config,
            Object target)
        {
            if (config == null
                || target == null
                || !AssetDatabase.IsSubAsset(target))
            {
                return false;
            }

            return string.Equals(
                AssetDatabase.GetAssetPath(config),
                AssetDatabase.GetAssetPath(target),
                StringComparison.OrdinalIgnoreCase
            );
        }

        internal static ItemDatabaseConfiguration ResolveConfiguration(
            ItemDatabaseConfiguration config,
            ItemDefinition definition)
        {
            string definitionPath = definition != null
                ? AssetDatabase.GetAssetPath(definition)
                : null;
            if (!string.IsNullOrEmpty(definitionPath))
            {
                ItemDatabaseConfiguration host = AssetDatabase
                    .LoadAssetAtPath<ItemDatabaseConfiguration>(definitionPath);
                if (host == null)
                {
                    throw new InvalidOperationException(
                        $"Definition '{definition.name}' is not hosted by an "
                        + "Item Database configuration."
                    );
                }

                if (config != null && config != host)
                {
                    throw new InvalidOperationException(
                        $"Definition '{definition.name}' belongs to a different "
                        + "Item Database configuration."
                    );
                }

                return host;
            }

            return config;
        }

        internal static void MarkDirty(params Object[] targets)
        {
            if (targets == null) return;
            foreach (Object target in targets)
            {
                if (target != null) EditorUtility.SetDirty(target);
            }
        }

        private static int BeginUndoGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return group;
        }

        private static void ValidateHost(ItemDatabaseConfiguration config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            string assetPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new InvalidOperationException(
                    "Item Database configuration must be a saved asset."
                );
            }
        }

        private static void EnsureEditable(ItemDatabaseConfiguration config)
        {
            ValidateHost(config);
            string assetPath = AssetDatabase.GetAssetPath(config);
            if (!AssetDatabase.IsOpenForEdit(assetPath, StatusQueryOptions.UseCachedIfPossible))
            {
                throw new InvalidOperationException(
                    $"Item Database configuration is not open for edit: '{assetPath}'."
                );
            }
        }

        private static void EnsureUnowned(TagDefinition tag)
        {
            EnsureUnowned(tag?.GetType(), tag?.name);
        }

        private static void EnsureUnowned(Type tagType, string displayName)
        {
            string owner = tagType != null
                ? ItemDatabase.GetTagOwner(tagType)
                : null;
            if (!string.IsNullOrEmpty(owner))
            {
                throw new ModuleOwnedEntityException(
                    displayName ?? tagType?.Name ?? "Tag",
                    owner
                );
            }
        }

        private static void EnsureDefinitionUnowned(
            ItemDefinition definition)
        {
            string owner = definition != null
                ? ItemDatabase.GetDefinitionOwner(definition)
                : null;
            if (string.IsNullOrEmpty(owner)) return;

            throw new ModuleOwnedEntityException(
                definition.ItemId,
                owner
            );
        }

        private static void EnsureOwnedSubAsset(
            ItemDatabaseConfiguration config,
            Object target,
            string role)
        {
            if (IsOwnedSubAsset(config, target)) return;

            throw new InvalidOperationException(
                $"The {role} '{target?.name ?? "missing"}' is external to "
                + "this Item Database configuration. Clone it before editing."
            );
        }

        private static void EnsureMetadataHasSingleOwner(
            ItemDatabaseConfiguration config,
            ItemDefinition definition,
            ItemMetadata metadata)
        {
            if (!IsMetadataReferencedByOtherDefinition(
                    config,
                    definition,
                    metadata))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Metadata '{metadata.name}' is shared by multiple definitions. "
                + "Clone the metadata before editing tags."
            );
        }

        private static bool IsMetadataReferencedByOtherDefinition(
            ItemDatabaseConfiguration config,
            ItemDefinition definition,
            ItemMetadata metadata)
        {
            return config != null
                && metadata != null
                && config.ItemDefinitions.Any(candidate => candidate != null
                    && candidate != definition
                    && candidate.Metadata == metadata);
        }

    }
}
