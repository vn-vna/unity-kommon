using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Coherent in-memory item store. Every compound item/tag mutation and
    /// persistence snapshot crosses the same synchronization boundary.
    /// Mutable tag objects never escape this class; immutable JSON is stored
    /// internally and deserialized into defensive copies for callers.
    /// </summary>
    public class ItemDatabaseEngine
    {
        internal event Action<long> MutationCommitted;

        private readonly object _stateLock = new object();
        private readonly Dictionary<string, ItemDefinition> _definitions;
        private readonly Dictionary<string, InventoryItem> _items
            = new Dictionary<string, InventoryItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<Type, string>> _tags
            = new Dictionary<string, Dictionary<Type, string>>(StringComparer.Ordinal);
        private readonly List<QuarantinedInventoryItem> _quarantine
            = new List<QuarantinedInventoryItem>();

        private long _mutationVersion;
        private long _acknowledgedVersion;
        private bool _requiresLegacyBackup;

        public ItemDatabaseEngine()
            : this(new Dictionary<string, ItemDefinition>(StringComparer.Ordinal))
        {
        }

        internal ItemDatabaseEngine(
            IReadOnlyDictionary<string, ItemDefinition> definitions)
        {
            _definitions = new Dictionary<string, ItemDefinition>(
                StringComparer.Ordinal
            );

            if (definitions == null) return;
            foreach (KeyValuePair<string, ItemDefinition> entry in definitions)
            {
                _definitions.Add(entry.Key, entry.Value);
            }

            ValidateDefinitionSchema();
        }

        public InventoryItem? GetItem(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            lock (_stateLock)
            {
                if (!_items.TryGetValue(key, out InventoryItem item)
                    || IsExpiredLocked(key, DateTime.UtcNow))
                {
                    return null;
                }

                return item;
            }
        }

        public bool HasItem(string key)
        {
            return GetItem(key).HasValue;
        }

        public int Count
        {
            get
            {
                lock (_stateLock)
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    int count = 0;
                    foreach (string key in _items.Keys)
                    {
                        if (!IsExpiredLocked(key, nowUtc)) count++;
                    }

                    return count;
                }
            }
        }

        public IEnumerable<KeyValuePair<string, InventoryItem>> Items
        {
            get
            {
                InventoryItem[] items = GetItemsSnapshot();
                var result = new KeyValuePair<string, InventoryItem>[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    result[i] = new KeyValuePair<string, InventoryItem>(
                        items[i].key,
                        items[i]
                    );
                }

                return result;
            }
        }

        internal long MutationVersion
        {
            get
            {
                lock (_stateLock) return _mutationVersion;
            }
        }

        internal long AcknowledgedVersion
        {
            get
            {
                lock (_stateLock) return _acknowledgedVersion;
            }
        }

        internal bool IsDirty
        {
            get
            {
                lock (_stateLock)
                {
                    return _mutationVersion > _acknowledgedVersion;
                }
            }
        }

        /// <summary>
        /// Get tag data for a specific TagDefinition type.
        /// Internally resolves via TagDataRegistry.
        /// </summary>
        public T GetTagData<T>(string key, Type tagDefType) where T : class, ITagData
        {
            return GetTagData(key, tagDefType) as T;
        }

        /// <summary>Get tag data object by TagDefinition type (non-generic).</summary>
        public ITagData GetTagData(string key, Type tagDefType)
        {
            if (string.IsNullOrEmpty(key) || tagDefType == null) return null;

            string json;
            Type dataType;
            lock (_stateLock)
            {
                if (!_items.ContainsKey(key)
                    || IsExpiredLocked(key, DateTime.UtcNow)
                    || !_tags.TryGetValue(key, out Dictionary<Type, string> tagDictionary)
                    || !tagDictionary.TryGetValue(tagDefType, out json))
                {
                    return null;
                }

                dataType = TagDataRegistry.GetDataType(tagDefType);
            }

            return dataType != null
                ? ItemTagDataSerializer.Deserialize(json, dataType)
                : null;
        }

        /// <summary>Check if item has tag data for a specific TagDefinition.</summary>
        public bool HasTagData(string key, Type tagDefType)
        {
            if (string.IsNullOrEmpty(key) || tagDefType == null) return false;

            lock (_stateLock)
            {
                return _items.ContainsKey(key)
                    && !IsExpiredLocked(key, DateTime.UtcNow)
                    && _tags.TryGetValue(key, out Dictionary<Type, string> tagDictionary)
                    && tagDictionary.ContainsKey(tagDefType);
            }
        }

        /// <summary>Get all non-marker tag data for an item.</summary>
        public ITagData[] GetTagDatas(string key)
        {
            KeyValuePair<Type, string>[] entries;
            lock (_stateLock)
            {
                if (string.IsNullOrEmpty(key)
                    || !_items.ContainsKey(key)
                    || IsExpiredLocked(key, DateTime.UtcNow)
                    || !_tags.TryGetValue(key, out Dictionary<Type, string> tagDictionary))
                {
                    return Array.Empty<ITagData>();
                }

                entries = new KeyValuePair<Type, string>[tagDictionary.Count];
                int index = 0;
                foreach (KeyValuePair<Type, string> entry in tagDictionary)
                {
                    entries[index++] = entry;
                }
            }

            var result = new ITagData[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                Type dataType = TagDataRegistry.GetDataType(entries[i].Key);
                result[i] = ItemTagDataSerializer.Deserialize(
                    entries[i].Value,
                    dataType
                );
            }

            return result;
        }

        internal InventoryItem[] GetItemsSnapshot()
        {
            lock (_stateLock)
            {
                DateTime nowUtc = DateTime.UtcNow;
                var result = new List<InventoryItem>(_items.Count);
                foreach (KeyValuePair<string, InventoryItem> entry in _items)
                {
                    if (!IsExpiredLocked(entry.Key, nowUtc)) result.Add(entry.Value);
                }

                return result.ToArray();
            }
        }

        internal ItemQueryRecord[] CaptureQueryRecords(
            string itemIdFilter,
            Type tagDefinitionTypeFilter)
        {
            lock (_stateLock)
            {
                DateTime nowUtc = DateTime.UtcNow;
                var records = new List<ItemQueryRecord>();
                foreach (KeyValuePair<string, InventoryItem> entry in _items)
                {
                    InventoryItem item = entry.Value;
                    if (IsExpiredLocked(entry.Key, nowUtc)) continue;
                    if (itemIdFilter != null && item.itemId != itemIdFilter) continue;

                    _tags.TryGetValue(
                        entry.Key,
                        out Dictionary<Type, string> tagDictionary
                    );
                    if (tagDefinitionTypeFilter != null
                        && (tagDictionary == null
                            || !tagDictionary.ContainsKey(tagDefinitionTypeFilter)))
                    {
                        continue;
                    }

                    records.Add(new ItemQueryRecord(
                        item,
                        tagDictionary != null
                            ? new Dictionary<Type, string>(tagDictionary)
                            : new Dictionary<Type, string>()
                    ));
                }

                return records.ToArray();
            }
        }

        internal ItemOperationResult ApplyAdd(AddItemRequest request)
        {
            if (request == null)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidItemId,
                    "Add request cannot be null."
                );
            }

            ItemOperationResult result;
            long committedVersion = 0;
            lock (_stateLock)
            {
                result = ApplyAddLocked(request, out committedVersion);
            }

            NotifyMutation(committedVersion);
            return result;
        }

        internal ItemOperationResult ApplyRemove(string key)
        {
            ItemOperationResult result;
            long committedVersion = 0;
            lock (_stateLock)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.InvalidItemKey,
                        "Remove key cannot be empty.",
                        _mutationVersion
                    );
                }

                if (!_items.Remove(key))
                {
                    return ItemOperationResult.NoChange(
                        _mutationVersion,
                        $"Item '{key}' was not found."
                    );
                }

                int previousQuantity = GetStackQuantityLocked(key);
                _tags.Remove(key);
                committedVersion = IncrementMutationVersionLocked();
                result = ItemOperationResult.Success(
                    committedVersion,
                    new[]
                    {
                        new ItemStackDelta(key, previousQuantity, 0, false)
                    }
                );
            }

            NotifyMutation(committedVersion);
            return result;
        }

        internal ItemOperationResult ApplySetTag(
            string key,
            Type tagDefinitionType,
            CapturedTagData capturedData)
        {
            long committedVersion = 0;
            ItemOperationResult result;
            lock (_stateLock)
            {
                result = ValidateTagMutationLocked(
                    key,
                    tagDefinitionType,
                    capturedData
                );
                if (result != null) return result;

                InventoryItem item = _items[key];
                ItemDefinition definition = GetDefinitionLocked(item.itemId);
                Dictionary<Type, string> tagDictionary = _tags[key];
                bool tagAlreadyExists = tagDictionary.ContainsKey(tagDefinitionType);
                if (IsImmutableTagMutationLocked(
                        definition,
                        tagDefinitionType,
                        tagAlreadyExists))
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.ImmutableStackIdentity,
                        $"Tag '{tagDefinitionType.Name}' is part of stack identity. "
                        + "Supply it in AddItemRequest instead.",
                        _mutationVersion
                    );
                }

                if (tagDictionary.TryGetValue(tagDefinitionType, out string existingJson)
                    && string.Equals(existingJson, capturedData.Json, StringComparison.Ordinal))
                {
                    return ItemOperationResult.NoChange(
                        _mutationVersion,
                        $"Tag '{tagDefinitionType.Name}' is unchanged."
                    );
                }

                tagDictionary[tagDefinitionType] = capturedData.Json;
                committedVersion = IncrementMutationVersionLocked();
                result = ItemOperationResult.Success(committedVersion);
            }

            NotifyMutation(committedVersion);
            return result;
        }

        internal ItemOperationResult ApplyRemoveTag(
            string key,
            Type tagDefinitionType)
        {
            long committedVersion = 0;
            ItemOperationResult result;
            lock (_stateLock)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.InvalidItemKey,
                        "Item key cannot be empty.",
                        _mutationVersion
                    );
                }

                if (tagDefinitionType == null)
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.InvalidTagData,
                        "Tag type cannot be null.",
                        _mutationVersion
                    );
                }

                if (!_items.TryGetValue(key, out InventoryItem item))
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.ItemNotFound,
                        $"Item '{key}' was not found.",
                        _mutationVersion
                    );
                }

                if (IsExpiredLocked(key, DateTime.UtcNow))
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.ItemNotFound,
                        $"Item '{key}' is expired.",
                        _mutationVersion
                    );
                }

                ItemDefinition definition = GetDefinitionLocked(item.itemId);
                if (!_tags.TryGetValue(
                        key,
                        out Dictionary<Type, string> tagDictionary)
                    || !tagDictionary.ContainsKey(tagDefinitionType))
                {
                    return ItemOperationResult.NoChange(
                        _mutationVersion,
                        $"Tag '{tagDefinitionType.Name}' was not present."
                    );
                }

                if (IsImmutableTagMutationLocked(
                        definition,
                        tagDefinitionType,
                        tagAlreadyExists: true))
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.ImmutableStackIdentity,
                        $"Tag '{tagDefinitionType.Name}' is part of stack identity.",
                        _mutationVersion
                    );
                }

                tagDictionary.Remove(tagDefinitionType);

                committedVersion = IncrementMutationVersionLocked();
                result = ItemOperationResult.Success(committedVersion);
            }

            NotifyMutation(committedVersion);
            return result;
        }

        internal ItemOperationResult ApplyRemoveQuantity(string key, int quantity)
        {
            long committedVersion = 0;
            ItemOperationResult result;
            lock (_stateLock)
            {
                if (quantity <= 0)
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.InvalidQuantity,
                        "Quantity must be greater than zero.",
                        _mutationVersion
                    );
                }

                if (!_items.TryGetValue(key, out InventoryItem item))
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.ItemNotFound,
                        $"Item '{key}' was not found.",
                        _mutationVersion
                    );
                }

                if (IsExpiredLocked(key, DateTime.UtcNow))
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.ItemNotFound,
                        $"Item '{key}' is expired.",
                        _mutationVersion
                    );
                }

                ItemDefinition definition = GetDefinitionLocked(item.itemId);
                StackableTag stackable = FindTagDefinition<StackableTag>(definition);
                int previousQuantity = stackable != null
                    ? GetStackQuantityLocked(key)
                    : 1;
                if (quantity > previousQuantity)
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.InsufficientQuantity,
                        $"Item '{key}' contains {previousQuantity}, requested {quantity}.",
                        _mutationVersion
                    );
                }

                int currentQuantity = previousQuantity - quantity;
                if (currentQuantity == 0)
                {
                    _items.Remove(key);
                    _tags.Remove(key);
                }
                else
                {
                    SetStackQuantityLocked(key, currentQuantity);
                }

                committedVersion = IncrementMutationVersionLocked();
                result = ItemOperationResult.Success(
                    committedVersion,
                    new[]
                    {
                        new ItemStackDelta(
                            key,
                            previousQuantity,
                            currentQuantity,
                            false
                        )
                    }
                );
            }

            NotifyMutation(committedVersion);
            return result;
        }

        internal ItemOperationResult ApplyExpire(DateTime nowUtc)
        {
            nowUtc = ItemDatabaseTime.NormalizeUtc(nowUtc);
            long committedVersion = 0;
            ItemOperationResult result;
            lock (_stateLock)
            {
                var expiredKeys = new List<string>();
                foreach (string key in _items.Keys)
                {
                    if (IsExpiredLocked(key, nowUtc)) expiredKeys.Add(key);
                }

                if (expiredKeys.Count == 0)
                {
                    return ItemOperationResult.NoChange(
                        _mutationVersion,
                        "No expired items were found."
                    );
                }

                var deltas = new ItemStackDelta[expiredKeys.Count];
                for (int i = 0; i < expiredKeys.Count; i++)
                {
                    string key = expiredKeys[i];
                    int previousQuantity = GetStackQuantityLocked(key);
                    _items.Remove(key);
                    _tags.Remove(key);
                    deltas[i] = new ItemStackDelta(
                        key,
                        previousQuantity,
                        0,
                        false
                    );
                }

                committedVersion = IncrementMutationVersionLocked();
                result = ItemOperationResult.Success(
                    committedVersion,
                    deltas,
                    $"Expired {expiredKeys.Count} item stacks."
                );
            }

            NotifyMutation(committedVersion);
            return result;
        }

        internal DateTime? GetNextExpiryUtc()
        {
            lock (_stateLock)
            {
                DateTime? next = null;
                foreach (string key in _items.Keys)
                {
                    DateTime? expiry = GetExpiryLocked(key);
                    if (!expiry.HasValue) continue;
                    if (!next.HasValue || expiry.Value < next.Value) next = expiry;
                }

                return next;
            }
        }

internal void Hydrate(ItemDatabaseState state)
        {
            state ??= new ItemDatabaseState();
            TagDataRegistry.EnsureInitialized();

            DateTime fallbackUtc = ItemDatabaseTime.ParseOrEpoch(state.savedAtUtc);
            List<InventoryItem> persistedItems = state.items
                ?? new List<InventoryItem>();
            List<InventoryTagEntry> persistedTags = state.tags
                ?? new List<InventoryTagEntry>();
            Dictionary<string, List<InventoryTagEntry>> tagsByKey
                = BuildPersistedTagLookup(persistedTags);
            var hydratedItems = new Dictionary<string, InventoryItem>(
                StringComparer.Ordinal
            );
            var hydratedTags = new Dictionary<string, Dictionary<Type, string>>(
                StringComparer.Ordinal
            );
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var newlyQuarantined = new List<QuarantinedInventoryItem>();
            bool repaired = false;

            foreach (InventoryItem serializedItem in persistedItems)
            {
                InventoryItem item = serializedItem;
                if (!seenKeys.Add(item.key))
                {
                    throw new ItemDatabaseException(
                        $"Persisted inventory contains duplicate key '{item.key}'."
                    );
                }

                List<InventoryTagEntry> itemTags = !string.IsNullOrEmpty(item.key)
                    && tagsByKey.TryGetValue(
                        item.key,
                        out List<InventoryTagEntry> foundTags)
                    ? foundTags
                    : new List<InventoryTagEntry>();
                string quarantineReason = null;
                if (string.IsNullOrWhiteSpace(item.key)
                    || string.IsNullOrWhiteSpace(item.itemId))
                {
                    quarantineReason = "Item has an empty key or item ID.";
                }
                else if (_definitions.Count > 0
                    && !_definitions.ContainsKey(item.itemId))
                {
                    quarantineReason =
                        $"Unknown definition '{item.itemId}'.";
                }

                Dictionary<Type, string> itemTagData = null;
                if (quarantineReason == null)
                {
                    try
                    {
                        item.RestoreAfterDeserialization(fallbackUtc);
                        if (!TryHydrateTags(
                                item,
                                itemTags,
                                out itemTagData,
                                out quarantineReason))
                        {
                            repaired = true;
                        }
                    }
                    catch (Exception exception)
                    {
                        quarantineReason = exception.Message;
                    }
                }

                if (quarantineReason != null)
                {
                    newlyQuarantined.Add(CreateQuarantineEntry(
                        item,
                        itemTags,
                        quarantineReason
                    ));
                    repaired = true;
                    continue;
                }

                hydratedItems.Add(item.key, item);
                hydratedTags.Add(item.key, itemTagData);
            }

            foreach (string tagKey in tagsByKey.Keys)
            {
                if (!seenKeys.Contains(tagKey))
                {
                    throw new ItemDatabaseException(
                        $"Persisted inventory contains an orphan tag for '{tagKey}'."
                    );
                }
            }

            repaired |= RepairStackState(hydratedItems, hydratedTags);
            repaired |= PurgeExpiredState(
                hydratedItems,
                hydratedTags,
                DateTime.UtcNow
            );

            lock (_stateLock)
            {
                _items.Clear();
                _tags.Clear();
                _quarantine.Clear();

                foreach (KeyValuePair<string, InventoryItem> entry in hydratedItems)
                {
                    _items.Add(entry.Key, entry.Value);
                    _tags.Add(entry.Key, hydratedTags[entry.Key]);
                }

                if (state.quarantine != null)
                {
                    foreach (QuarantinedInventoryItem entry in state.quarantine)
                    {
                        if (entry != null)
                        {
                            _quarantine.Add(CloneQuarantineEntry(entry));
                        }
                    }
                }

                foreach (QuarantinedInventoryItem entry in newlyQuarantined)
                {
                    _quarantine.Add(entry);
                }

                _acknowledgedVersion = Math.Max(0, state.revision);
                _mutationVersion = _acknowledgedVersion;
                _requiresLegacyBackup = state.requiresLegacyBackup;
                if (repaired || _requiresLegacyBackup) _mutationVersion++;
            }
        }

        private bool TryHydrateTags(
            InventoryItem item,
            List<InventoryTagEntry> persistedTags,
            out Dictionary<Type, string> hydratedTags,
            out string quarantineReason)
        {
            hydratedTags = new Dictionary<Type, string>();
            quarantineReason = null;

            foreach (InventoryTagEntry entry in persistedTags)
            {
                try
                {
                    Type tagDefinitionType = TagDataRegistry
                        .ResolveTagDefinitionType(
                            entry.tagId,
                            entry.tagDefTypeName
                        );
                    if (tagDefinitionType == null)
                    {
                        quarantineReason =
                            $"Unknown tag '{entry.tagId ?? entry.tagDefTypeName}'.";
                        return false;
                    }

                    ItemDefinition definition = GetDefinitionLocked(item.itemId);
                    if (_definitions.Count > 0
                        && !IsTagAllowed(definition, tagDefinitionType))
                    {
                        quarantineReason =
                            $"Tag '{tagDefinitionType.Name}' is not allowed "
                            + $"on '{item.itemId}'.";
                        return false;
                    }

                    Type dataType = TagDataRegistry.GetDataType(tagDefinitionType);
                    if (dataType == null)
                    {
                        quarantineReason =
                            $"Persisted payload targets marker tag "
                            + $"'{tagDefinitionType.Name}'.";
                        return false;
                    }

                    ITagData data = ItemTagDataSerializer.Deserialize(
                        entry.jsonData,
                        dataType
                    );
                    if (data is StackableData stackable && stackable.count <= 0)
                    {
                        quarantineReason =
                            $"Stack '{item.key}' has invalid quantity {stackable.count}.";
                        return false;
                    }

                    string canonicalJson = ItemTagDataSerializer.Serialize(
                        tagDefinitionType,
                        data
                    );
                    if (!hydratedTags.TryAdd(
                            tagDefinitionType,
                            canonicalJson))
                    {
                        quarantineReason =
                            $"Duplicate tag '{tagDefinitionType.Name}'.";
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    quarantineReason = exception.Message;
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, List<InventoryTagEntry>>
            BuildPersistedTagLookup(List<InventoryTagEntry> persistedTags)
        {
            var result = new Dictionary<string, List<InventoryTagEntry>>(
                StringComparer.Ordinal
            );
            foreach (InventoryTagEntry entry in persistedTags)
            {
                if (string.IsNullOrWhiteSpace(entry.key))
                {
                    throw new ItemDatabaseException(
                        "Persisted inventory contains a tag with an empty item key."
                    );
                }

                if (!result.TryGetValue(
                        entry.key,
                        out List<InventoryTagEntry> entries))
                {
                    entries = new List<InventoryTagEntry>();
                    result.Add(entry.key, entries);
                }

                entries.Add(entry);
            }

            return result;
        }

        private static QuarantinedInventoryItem CreateQuarantineEntry(
            InventoryItem item,
            List<InventoryTagEntry> tags,
            string reason)
        {
            return new QuarantinedInventoryItem
            {
                item = item,
                tags = tags != null
                    ? new List<InventoryTagEntry>(tags)
                    : new List<InventoryTagEntry>(),
                reason = reason
            };
        }


        internal ItemDatabaseSnapshot CaptureSnapshot()
        {
            lock (_stateLock)
            {
                var state = new ItemDatabaseState
                {
                    savedAtUtc = DateTime.UtcNow.ToString("o"),
                    revision = _mutationVersion,
                    requiresLegacyBackup = _requiresLegacyBackup
                };

                foreach (KeyValuePair<string, InventoryItem> entry in _items)
                {
                    InventoryItem item = entry.Value;
                    item.PrepareForSerialization();
                    state.items.Add(item);
                }

                foreach (KeyValuePair<string, Dictionary<Type, string>> itemTags in _tags)
                {
                    foreach (KeyValuePair<Type, string> tag in itemTags.Value)
                    {
                        state.tags.Add(new InventoryTagEntry
                        {
                            key = itemTags.Key,
                            tagId = TagDataRegistry.GetPersistentId(tag.Key),
                            tagDefTypeName = tag.Key.AssemblyQualifiedName,
                            jsonData = tag.Value
                        });
                    }
                }

                foreach (QuarantinedInventoryItem entry in _quarantine)
                {
                    state.quarantine.Add(CloneQuarantineEntry(entry));
                }

                return new ItemDatabaseSnapshot(state, _mutationVersion);
            }
        }

        internal void AcknowledgePersisted(
            long version,
            bool legacyBackupCompleted)
        {
            lock (_stateLock)
            {
                if (version > _acknowledgedVersion)
                {
                    _acknowledgedVersion = Math.Min(version, _mutationVersion);
                }

                if (legacyBackupCompleted) _requiresLegacyBackup = false;
            }
        }

        private ItemOperationResult ApplyAddLocked(
            AddItemRequest request,
            out long committedVersion)
        {
            committedVersion = 0;
            if (string.IsNullOrWhiteSpace(request.ItemId))
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidItemId,
                    "Item ID cannot be empty.",
                    _mutationVersion
                );
            }

            ItemDefinition definition = GetDefinitionLocked(request.ItemId);
            if (definition == null)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.DefinitionNotFound,
                    $"Definition '{request.ItemId}' was not found.",
                    _mutationVersion
                );
            }

            if (request.Quantity <= 0)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidQuantity,
                    "Quantity must be greater than zero.",
                    _mutationVersion
                );
            }

            string requestedKey = string.IsNullOrWhiteSpace(request.RequestedKey)
                ? CreateUniqueKeyLocked()
                : request.RequestedKey;
            if (_items.ContainsKey(requestedKey))
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.DuplicateItemKey,
                    $"Item key '{requestedKey}' already exists.",
                    _mutationVersion
                );
            }

            if (!TryBuildInitialTags(
                    definition,
                    request.CapturedTags,
                    out Dictionary<Type, string> incomingTags,
                    out ItemOperationResult validationFailure))
            {
                return validationFailure;
            }

            StackableTag stackable = FindTagDefinition<StackableTag>(definition);
            if (stackable == null && request.Quantity != 1)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidQuantity,
                    $"Non-stackable definition '{request.ItemId}' only accepts quantity 1.",
                    _mutationVersion
                );
            }

            int maxStack = stackable?.MaxStack ?? 1;
            if (maxStack <= 0)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidStackCapacity,
                    $"Definition '{request.ItemId}' has invalid MaxStack {maxStack}.",
                    _mutationVersion
                );
            }

            int remaining = request.Quantity;
            var deltas = new List<ItemStackDelta>();
            if (stackable != null)
            {
                List<InventoryItem> candidates = FindStackCandidatesLocked(
                    request,
                    incomingTags
                );
                foreach (InventoryItem candidate in candidates)
                {
                    int previousQuantity = GetStackQuantityLocked(candidate.key);
                    int available = maxStack - previousQuantity;
                    if (available <= 0) continue;

                    int added = Math.Min(available, remaining);
                    SetStackQuantityLocked(candidate.key, checked(previousQuantity + added));
                    deltas.Add(new ItemStackDelta(
                        candidate.key,
                        previousQuantity,
                        previousQuantity + added,
                        false
                    ));
                    remaining -= added;
                    if (remaining == 0) break;
                }
            }

            bool usedRequestedKey = false;
            while (remaining > 0)
            {
                int stackQuantity = Math.Min(maxStack, remaining);
                string key = !usedRequestedKey
                    ? requestedKey
                    : CreateUniqueKeyLocked();
                usedRequestedKey = true;

                InventoryItem item = request.CreateInventoryItem(key);
                _items.Add(key, item);
                var tagDictionary = new Dictionary<Type, string>(incomingTags);
                if (stackable != null)
                {
                    tagDictionary[typeof(StackableTag)] = ItemTagDataSerializer.Serialize(
                        typeof(StackableTag),
                        new StackableData { count = stackQuantity }
                    );
                }

                _tags.Add(key, tagDictionary);
                deltas.Add(new ItemStackDelta(key, 0, stackQuantity, true));
                remaining -= stackQuantity;
            }

            committedVersion = IncrementMutationVersionLocked();
            return ItemOperationResult.Success(
                committedVersion,
                deltas.ToArray()
            );
        }

        private ItemOperationResult ValidateTagMutationLocked(
            string key,
            Type tagDefinitionType,
            CapturedTagData capturedData)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidItemKey,
                    "Item key cannot be empty.",
                    _mutationVersion
                );
            }

            if (!_items.TryGetValue(key, out InventoryItem item))
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.ItemNotFound,
                    $"Item '{key}' was not found.",
                    _mutationVersion
                );
            }

            if (IsExpiredLocked(key, DateTime.UtcNow))
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.ItemNotFound,
                    $"Item '{key}' is expired.",
                    _mutationVersion
                );
            }

            if (tagDefinitionType == null
                || capturedData.TagDefinitionType != tagDefinitionType)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidTagData,
                    "Tag mapping does not match the captured payload.",
                    _mutationVersion
                );
            }

            ItemDefinition definition = GetDefinitionLocked(item.itemId);
            if (!IsTagAllowed(definition, tagDefinitionType))
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.TagNotAllowed,
                    $"Tag '{tagDefinitionType.Name}' is not allowed on '{item.itemId}'.",
                    _mutationVersion
                );
            }

            if (tagDefinitionType == typeof(StackableTag))
            {
                var stack = ItemTagDataSerializer.Deserialize(
                    capturedData.Json,
                    capturedData.DataType
                ) as StackableData;
                StackableTag stackable = FindTagDefinition<StackableTag>(definition);
                if (stack == null || stack.count < 1 || stack.count > stackable.MaxStack)
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.InvalidQuantity,
                        $"Stack count must be between 1 and {stackable.MaxStack}.",
                        _mutationVersion
                    );
                }
            }

            if (tagDefinitionType == typeof(ExpirableTag))
            {
                var expirable = ItemTagDataSerializer.Deserialize(
                    capturedData.Json,
                    capturedData.DataType
                ) as ExpirableData;
                if (expirable == null || expirable.IsExpired)
                {
                    return ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.InvalidTagData,
                        "Expiry must be a future UTC timestamp.",
                        _mutationVersion
                    );
                }
            }

            return null;
        }

        private static bool IsImmutableTagMutationLocked(
            ItemDefinition definition,
            Type tagDefinitionType,
            bool tagAlreadyExists)
        {
            if (tagDefinitionType == typeof(StackableTag))
            {
                return true;
            }

            bool stackable = FindTagDefinition<StackableTag>(definition) != null;
            if (tagDefinitionType == typeof(ExpirableTag))
            {
                return stackable || tagAlreadyExists;
            }

            return stackable
                && TagDataRegistry.GetDataType(tagDefinitionType) != null;
        }

        private bool TryBuildInitialTags(
            ItemDefinition definition,
            IReadOnlyList<CapturedTagData> capturedTags,
            out Dictionary<Type, string> tags,
            out ItemOperationResult failure)
        {
            tags = new Dictionary<Type, string>();
            failure = null;
            foreach (CapturedTagData captured in capturedTags)
            {
                Type tagDefinitionType = captured.TagDefinitionType;
                if (tagDefinitionType == typeof(StackableTag))
                {
                    failure = ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.InvalidTagData,
                        "StackableData is derived from AddItemRequest.Quantity.",
                        _mutationVersion
                    );
                    return false;
                }

                if (!IsTagAllowed(definition, tagDefinitionType))
                {
                    failure = ItemOperationResult.Rejected(
                        ItemDatabaseErrorCode.TagNotAllowed,
                        $"Tag '{tagDefinitionType.Name}' is not allowed on "
                        + $"'{definition.ItemId}'.",
                        _mutationVersion
                    );
                    return false;
                }

                if (tagDefinitionType == typeof(ExpirableTag))
                {
                    var expiry = ItemTagDataSerializer.Deserialize(
                        captured.Json,
                        captured.DataType
                    ) as ExpirableData;
                    if (expiry == null || expiry.IsExpired)
                    {
                        failure = ItemOperationResult.Rejected(
                            ItemDatabaseErrorCode.InvalidTagData,
                            "Initial expiry must be a future UTC timestamp.",
                            _mutationVersion
                        );
                        return false;
                    }
                }

                tags.Add(tagDefinitionType, captured.Json);
            }

            return true;
        }

        private List<InventoryItem> FindStackCandidatesLocked(
            AddItemRequest request,
            Dictionary<Type, string> incomingTags)
        {
            var result = new List<InventoryItem>();
            DateTime nowUtc = DateTime.UtcNow;
            foreach (KeyValuePair<string, InventoryItem> entry in _items)
            {
                InventoryItem item = entry.Value;
                if (item.itemId != request.ItemId
                    || item.customName != request.CustomName
                    || IsExpiredLocked(entry.Key, nowUtc))
                {
                    continue;
                }

                if (!_tags.TryGetValue(entry.Key, out Dictionary<Type, string> existingTags)
                    || !HaveSameStackIdentity(existingTags, incomingTags))
                {
                    continue;
                }

                result.Add(item);
            }

            result.Sort((left, right) =>
            {
                int createdComparison = left.createdAt.CompareTo(right.createdAt);
                return createdComparison != 0
                    ? createdComparison
                    : string.CompareOrdinal(left.key, right.key);
            });
            return result;
        }

        private static bool HaveSameStackIdentity(
            Dictionary<Type, string> existingTags,
            Dictionary<Type, string> incomingTags)
        {
            int existingCount = existingTags.ContainsKey(typeof(StackableTag))
                ? existingTags.Count - 1
                : existingTags.Count;
            if (existingCount != incomingTags.Count) return false;

            foreach (KeyValuePair<Type, string> incoming in incomingTags)
            {
                if (!existingTags.TryGetValue(incoming.Key, out string existingJson)
                    || !string.Equals(
                        existingJson,
                        incoming.Value,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private bool RepairStackState(
            Dictionary<string, InventoryItem> items,
            Dictionary<string, Dictionary<Type, string>> tags)
        {
            bool repaired = false;
            var originalItems = new List<InventoryItem>(items.Values);
            foreach (InventoryItem item in originalItems)
            {
                ItemDefinition definition = GetDefinitionLocked(item.itemId);
                StackableTag stackable = FindTagDefinition<StackableTag>(definition);
                if (stackable == null) continue;
                if (stackable.MaxStack <= 0)
                {
                    throw new ItemDatabaseException(
                        $"Definition '{item.itemId}' has invalid MaxStack {stackable.MaxStack}."
                    );
                }

                Dictionary<Type, string> tagDictionary = tags[item.key];
                int quantity;
                if (!tagDictionary.TryGetValue(typeof(StackableTag), out string stackJson))
                {
                    quantity = 1;
                    tagDictionary[typeof(StackableTag)] = ItemTagDataSerializer.Serialize(
                        typeof(StackableTag),
                        new StackableData { count = 1 }
                    );
                    repaired = true;
                }
                else
                {
                    var stack = ItemTagDataSerializer.Deserialize(
                        stackJson,
                        typeof(StackableData)
                    ) as StackableData;
                    quantity = stack?.count ?? 0;
                }

                if (quantity <= 0)
                {
                    throw new ItemDatabaseException(
                        $"Persisted stack '{item.key}' has invalid quantity {quantity}."
                    );
                }

                if (quantity <= stackable.MaxStack) continue;
                tagDictionary[typeof(StackableTag)] = ItemTagDataSerializer.Serialize(
                    typeof(StackableTag),
                    new StackableData { count = stackable.MaxStack }
                );

                int remaining = quantity - stackable.MaxStack;
                while (remaining > 0)
                {
                    int stackQuantity = Math.Min(stackable.MaxStack, remaining);
                    string key = CreateUniqueKey(items);
                    var splitItem = new InventoryItem(
                        key,
                        item.itemId,
                        item.customName,
                        item.createdAt
                    );
                    var splitTags = new Dictionary<Type, string>(tagDictionary)
                    {
                        [typeof(StackableTag)] = ItemTagDataSerializer.Serialize(
                            typeof(StackableTag),
                            new StackableData { count = stackQuantity }
                        )
                    };
                    items.Add(key, splitItem);
                    tags.Add(key, splitTags);
                    remaining -= stackQuantity;
                }

                repaired = true;
            }

            return repaired;
        }

        private static bool PurgeExpiredState(
            Dictionary<string, InventoryItem> items,
            Dictionary<string, Dictionary<Type, string>> tags,
            DateTime nowUtc)
        {
            var expiredKeys = new List<string>();
            foreach (KeyValuePair<string, Dictionary<Type, string>> entry in tags)
            {
                if (!entry.Value.TryGetValue(typeof(ExpirableTag), out string json))
                {
                    continue;
                }

                var expiry = ItemTagDataSerializer.Deserialize(
                    json,
                    typeof(ExpirableData)
                ) as ExpirableData;
                if (expiry != null && expiry.expiresAt <= nowUtc)
                {
                    expiredKeys.Add(entry.Key);
                }
            }

            foreach (string key in expiredKeys)
            {
                items.Remove(key);
                tags.Remove(key);
            }

            return expiredKeys.Count > 0;
        }

        private ItemOperationResult ValidateDefinitionSchema()
        {
            foreach (KeyValuePair<string, ItemDefinition> entry in _definitions)
            {
                ItemDefinition definition = entry.Value;
                if (definition == null || definition.ItemId != entry.Key)
                {
                    throw new ItemDatabaseException(
                        $"Definition lookup entry '{entry.Key}' is invalid."
                    );
                }

                var tagTypes = new HashSet<Type>();
                foreach (TagDefinition tag in definition.Tags)
                {
                    if (tag == null)
                    {
                        throw new ItemDatabaseException(
                            $"Definition '{definition.ItemId}' contains a missing tag reference."
                        );
                    }

                    if (!tagTypes.Add(tag.GetType()))
                    {
                        throw new ItemDatabaseException(
                            $"Definition '{definition.ItemId}' contains duplicate tag "
                            + $"'{tag.GetType().Name}'."
                        );
                    }

                    if (tag is StackableTag stackable && stackable.MaxStack <= 0)
                    {
                        throw new ItemDatabaseException(
                            $"Definition '{definition.ItemId}' has invalid MaxStack "
                            + $"{stackable.MaxStack}."
                        );
                    }

                    if (tag is WeaponTag weapon
                        && weapon.AtkRange.x > weapon.AtkRange.y)
                    {
                        throw new ItemDatabaseException(
                            $"Definition '{definition.ItemId}' has an inverted "
                            + "weapon attack range."
                        );
                    }

                    if (tag is ArmorTag armor
                        && (armor.HpRange.x > armor.HpRange.y
                            || armor.DefRange.x > armor.DefRange.y))
                    {
                        throw new ItemDatabaseException(
                            $"Definition '{definition.ItemId}' has an inverted "
                            + "armor stat range."
                        );
                    }
                }
            }

            return null;
        }

        private ItemDefinition GetDefinitionLocked(string itemId)
        {
            _definitions.TryGetValue(itemId, out ItemDefinition definition);
            return definition;
        }

        private static T FindTagDefinition<T>(ItemDefinition definition)
            where T : TagDefinition
        {
            if (definition == null) return null;
            foreach (TagDefinition tag in definition.Tags)
            {
                if (tag is T typedTag) return typedTag;
            }

            return null;
        }

        private static bool IsTagAllowed(
            ItemDefinition definition,
            Type tagDefinitionType)
        {
            if (definition == null || tagDefinitionType == null) return false;
            foreach (TagDefinition tag in definition.Tags)
            {
                if (tag != null && tag.GetType() == tagDefinitionType) return true;
            }

            return false;
        }

        private int GetStackQuantityLocked(string key)
        {
            if (!_tags.TryGetValue(key, out Dictionary<Type, string> tagDictionary)
                || !tagDictionary.TryGetValue(typeof(StackableTag), out string json))
            {
                return 1;
            }

            var stack = ItemTagDataSerializer.Deserialize(
                json,
                typeof(StackableData)
            ) as StackableData;
            return stack?.count ?? 1;
        }

        private void SetStackQuantityLocked(string key, int quantity)
        {
            _tags[key][typeof(StackableTag)] = ItemTagDataSerializer.Serialize(
                typeof(StackableTag),
                new StackableData { count = quantity }
            );
        }

        private bool IsExpiredLocked(string key, DateTime nowUtc)
        {
            DateTime? expiry = GetExpiryLocked(key);
            return expiry.HasValue && expiry.Value <= nowUtc;
        }

        private DateTime? GetExpiryLocked(string key)
        {
            if (!_tags.TryGetValue(key, out Dictionary<Type, string> tagDictionary)
                || !tagDictionary.TryGetValue(typeof(ExpirableTag), out string json))
            {
                return null;
            }

            var expiry = ItemTagDataSerializer.Deserialize(
                json,
                typeof(ExpirableData)
            ) as ExpirableData;
            return expiry?.expiresAt;
        }

        private long IncrementMutationVersionLocked()
        {
            return ++_mutationVersion;
        }

        private void NotifyMutation(long version)
        {
            if (version <= 0) return;

            try
            {
                MutationCommitted?.Invoke(version);
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseEngine>(
                    "Mutation observer failed at version {0}: {1}",
                    version,
                    exception
                );
            }
        }

        private string CreateUniqueKeyLocked()
        {
            return CreateUniqueKey(_items);
        }

        private static string CreateUniqueKey<T>(IDictionary<string, T> items)
        {
            string key;
            do
            {
                key = Guid.NewGuid().ToString("N");
            }
            while (items.ContainsKey(key));

            return key;
        }

        private static QuarantinedInventoryItem CloneQuarantineEntry(
            QuarantinedInventoryItem source)
        {
            return new QuarantinedInventoryItem
            {
                item = source.item,
                reason = source.reason,
                tags = source.tags != null
                    ? new List<InventoryTagEntry>(source.tags)
                    : new List<InventoryTagEntry>()
            };
        }
    }

    internal sealed class ItemQueryRecord
    {
        internal InventoryItem Item { get; }

        internal IReadOnlyDictionary<Type, string> Tags { get; }

        internal ItemQueryRecord(
            InventoryItem item,
            IReadOnlyDictionary<Type, string> tags)
        {
            Item = item;
            Tags = tags;
        }
    }
}
