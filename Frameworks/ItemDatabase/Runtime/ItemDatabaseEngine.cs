using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Operation Layer: the in-memory item database.
    /// Readers access the ConcurrentDictionary store lock-free;
    /// writers are expected to go through the CommitQueue's single
    /// processor thread (middleware may apply direct mutations, which
    /// remain thread-safe on the ConcurrentDictionary).
    /// </summary>
    public class ItemDatabaseEngine
    {
        // ─── In-Memory Store ───
        // _items:  key (GUID) → InventoryItem
        // _tags:   key (GUID) → { TagDefinitionType → ITagData }
        internal readonly ConcurrentDictionary<string, InventoryItem> _items
            = new ConcurrentDictionary<string, InventoryItem>();

        internal readonly ConcurrentDictionary<string, ConcurrentDictionary<Type, ITagData>> _tags
            = new ConcurrentDictionary<string, ConcurrentDictionary<Type, ITagData>>();

        internal int _dirty;

        // ─── Query (lock-free reads) ───

        public InventoryItem? GetItem(string key)
        {
            if (_items.TryGetValue(key, out var item)) return item;
            return null;
        }

        public bool HasItem(string key) => _items.ContainsKey(key);

        public int Count => _items.Count;

        public IEnumerable<KeyValuePair<string, InventoryItem>> Items => _items;

        /// <summary>
        /// Get tag data for a specific TagDefinition type.
        /// Internally resolves via TagDataRegistry.
        /// </summary>
        public T GetTagData<T>(string key, Type tagDefType) where T : class, ITagData
        {
            if (_tags.TryGetValue(key, out var tagDict)
                && tagDict.TryGetValue(tagDefType, out var data))
                return data as T;
            return null;
        }

        /// <summary>Get tag data object by TagDefinition type (non-generic).</summary>
        public ITagData GetTagData(string key, Type tagDefType)
        {
            if (_tags.TryGetValue(key, out var tagDict)
                && tagDict.TryGetValue(tagDefType, out var data))
                return data as ITagData;
            return null;
        }

        /// <summary>Check if item has tag data for a specific TagDefinition.</summary>
        public bool HasTagData(string key, Type tagDefType)
        {
            return _tags.TryGetValue(key, out var tagDict)
                && tagDict.ContainsKey(tagDefType);
        }

        /// <summary>Get all non-marker tag data for an item.</summary>
        public ITagData[] GetTagDatas(string key)
        {
            if (_tags.TryGetValue(key, out var tagDict))
            {
                var arr = new ITagData[tagDict.Count];
                tagDict.Values.CopyTo(arr, 0);
                return arr;
            }

            return Array.Empty<ITagData>();
        }

        // ─── Write (called by CommitQueue processor only) ───

        internal void ApplyAdd(InventoryItem item)
        {
            _items[item.key] = item;
            if (!_tags.ContainsKey(item.key))
                _tags[item.key] = new ConcurrentDictionary<Type, ITagData>();
            InterlockedExchangeDirty();
        }

        internal void ApplyRemove(string key)
        {
            _items.TryRemove(key, out _);
            _tags.TryRemove(key, out _);
            InterlockedExchangeDirty();
        }

        /// <summary>
        /// Set tag data, keyed by TagDefinition type.
        /// Caller must provide both the data and its resolved tagDefType.
        /// </summary>
        internal void ApplySetTag(string key, Type tagDefType, ITagData data)
        {
            var tagDict = _tags.GetOrAdd(key,
                _ => new ConcurrentDictionary<Type, ITagData>());
            tagDict[tagDefType] = data;
            InterlockedExchangeDirty();
        }

        internal void ApplyRemoveTag(string key, Type tagDefType)
        {
            if (_tags.TryGetValue(key, out var tagDict))
            {
                tagDict.TryRemove(tagDefType, out _);
                InterlockedExchangeDirty();
            }
        }

        // ─── Hydrate / Snapshot ───

        internal void Hydrate(ItemDatabaseState state)
        {
            _items.Clear();
            _tags.Clear();
            TagDataRegistry.EnsureInitialized();

            foreach (var item in state.items)
                _items[item.key] = item;

            foreach (var entry in state.tags)
            {
                // entry.tagDefTypeName stores the TagDefinition type
                var tagDefType = Type.GetType(entry.tagDefTypeName);
                if (tagDefType == null) continue;

                // Resolve which ITagData class to deserialize
                var dataType = TagDataRegistry.GetDataType(tagDefType);
                if (dataType == null) continue; // marker tags have no data

                var tagDict = _tags.GetOrAdd(entry.key,
                    _ => new ConcurrentDictionary<Type, ITagData>());

                try
                {
                    var data = (ITagData)JsonUtility.FromJson(entry.jsonData, dataType);
                    tagDict[tagDefType] = data;
                }
                catch
                {
                    QuickLog.Warning<ItemDatabaseEngine>(
                        "Failed to deserialize tag data for {0} on item {1}",
                        entry.tagDefTypeName, entry.key);
                }
            }
        }

        internal ItemDatabaseState Snapshot()
        {
            var state = new ItemDatabaseState();
            state.savedAtUtc = DateTime.UtcNow.ToString("o");
            foreach (var kvp in _items)
                state.items.Add(kvp.Value);

            foreach (var kvp in _tags)
            {
                foreach (var tagKvp in kvp.Value)
                {
                    state.tags.Add(new InventoryTagEntry
                    {
                        key = kvp.Key,
                        tagDefTypeName = tagKvp.Key.AssemblyQualifiedName,
                        jsonData = JsonUtility.ToJson(tagKvp.Value)
                    });
                }
            }

            return state;
        }

        internal bool IsDirty =>
            Volatile.Read(ref _dirty) != 0;

        internal void ClearDirty()
        {
            Interlocked.Exchange(ref _dirty, 0);
        }

        private void InterlockedExchangeDirty()
        {
            Interlocked.Exchange(ref _dirty, 1);
        }
    }
}
