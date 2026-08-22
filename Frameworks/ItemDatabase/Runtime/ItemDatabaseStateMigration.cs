using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.DataSync;
using UnityEngine.Scripting;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    [Serializable]
    [Preserve]
    public class ItemDatabaseStateV1
    {
        public List<InventoryItemStateV1> items = new List<InventoryItemStateV1>();

        public List<InventoryTagEntry> tags = new List<InventoryTagEntry>();

        public string savedAtUtc;
    }

    [Serializable]
    [Preserve]
    public struct InventoryItemStateV1
    {
        public string key;

        public string itemId;

        public string customName;

        public DateTime createdAt;
    }

    [MigratorVersion("1.0.0", "2.0.0")]
    [Preserve]
    public sealed class ItemDatabaseStateV1ToV2Migrator
        : VersionMigrator<ItemDatabaseStateV1, ItemDatabaseState>
    {
        public override ItemDatabaseState Migrate(ItemDatabaseStateV1 snapshot)
        {
            if (snapshot == null)
            {
                throw new ItemDatabaseException("Cannot migrate a null Item Database state.");
            }

            TagDataRegistry.EnsureInitialized();
            DateTime fallbackUtc = ItemDatabaseTime.ParseOrEpoch(snapshot.savedAtUtc);
            var result = new ItemDatabaseState
            {
                savedAtUtc = fallbackUtc.ToString("o"),
                revision = 0,
                requiresLegacyBackup = true
            };

            var legacyItems = snapshot.items ?? new List<InventoryItemStateV1>();
            var legacyTags = snapshot.tags ?? new List<InventoryTagEntry>();
            var tagsByKey = BuildTagLookup(legacyItems, legacyTags);
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (InventoryItemStateV1 legacyItem in legacyItems)
            {
                if (string.IsNullOrWhiteSpace(legacyItem.key)
                    || !seenKeys.Add(legacyItem.key))
                {
                    throw new ItemDatabaseException(
                        $"Legacy Item Database contains an invalid or duplicate key '{legacyItem.key}'."
                    );
                }

                var item = new InventoryItem(
                    legacyItem.key,
                    legacyItem.itemId,
                    legacyItem.customName,
                    legacyItem.createdAt != default
                        ? ItemDatabaseTime.NormalizeUtc(legacyItem.createdAt)
                        : fallbackUtc
                );
                List<InventoryTagEntry> itemTags = tagsByKey.TryGetValue(
                    legacyItem.key,
                    out List<InventoryTagEntry> foundTags)
                    ? foundTags
                    : new List<InventoryTagEntry>();

                if (TryMigrateTags(itemTags, out List<InventoryTagEntry> migratedTags,
                        out string quarantineReason))
                {
                    result.items.Add(item);
                    result.tags.AddRange(migratedTags);
                    continue;
                }

                result.quarantine.Add(new QuarantinedInventoryItem
                {
                    item = item,
                    tags = new List<InventoryTagEntry>(itemTags),
                    reason = quarantineReason
                });
            }

            return result;
        }

        private static Dictionary<string, List<InventoryTagEntry>> BuildTagLookup(
            List<InventoryItemStateV1> items,
            List<InventoryTagEntry> tags)
        {
            var itemKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventoryItemStateV1 item in items)
            {
                if (!string.IsNullOrEmpty(item.key)) itemKeys.Add(item.key);
            }

            var result = new Dictionary<string, List<InventoryTagEntry>>(
                StringComparer.Ordinal
            );
            foreach (InventoryTagEntry entry in tags)
            {
                if (string.IsNullOrEmpty(entry.key) || !itemKeys.Contains(entry.key))
                {
                    throw new ItemDatabaseException(
                        $"Legacy Item Database contains an orphan tag for key '{entry.key}'."
                    );
                }

                if (!result.TryGetValue(entry.key, out List<InventoryTagEntry> entries))
                {
                    entries = new List<InventoryTagEntry>();
                    result.Add(entry.key, entries);
                }

                entries.Add(entry);
            }

            return result;
        }

        private static bool TryMigrateTags(
            List<InventoryTagEntry> legacyTags,
            out List<InventoryTagEntry> migratedTags,
            out string quarantineReason)
        {
            migratedTags = new List<InventoryTagEntry>(legacyTags.Count);
            quarantineReason = null;
            var seenTypes = new HashSet<Type>();

            foreach (InventoryTagEntry legacyTag in legacyTags)
            {
                Type tagDefinitionType = TagDataRegistry.ResolveTagDefinitionType(
                    legacyTag.tagId,
                    legacyTag.tagDefTypeName
                );
                if (tagDefinitionType == null)
                {
                    quarantineReason = $"Unknown legacy tag '{legacyTag.tagDefTypeName}'.";
                    return false;
                }

                if (!seenTypes.Add(tagDefinitionType))
                {
                    quarantineReason = $"Duplicate legacy tag '{tagDefinitionType.Name}'.";
                    return false;
                }

                if (tagDefinitionType == typeof(ExpirableTag))
                {
                    quarantineReason = "Legacy expiry timestamps were not serialized and cannot be recovered safely.";
                    return false;
                }

                Type dataType = TagDataRegistry.GetDataType(tagDefinitionType);
                if (dataType == null)
                {
                    quarantineReason = $"Legacy payload targets marker tag '{tagDefinitionType.Name}'.";
                    return false;
                }

                try
                {
                    _ = ItemTagDataSerializer.Deserialize(legacyTag.jsonData, dataType);
                }
                catch (Exception exception)
                {
                    quarantineReason = exception.Message;
                    return false;
                }

                migratedTags.Add(new InventoryTagEntry
                {
                    key = legacyTag.key,
                    tagId = TagDataRegistry.GetPersistentId(tagDefinitionType),
                    tagDefTypeName = tagDefinitionType.AssemblyQualifiedName,
                    jsonData = legacyTag.jsonData
                });
            }

            return true;
        }
    }
}
