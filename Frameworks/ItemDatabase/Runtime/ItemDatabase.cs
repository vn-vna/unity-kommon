using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Public API for the Item Database framework.
    /// All methods are safe to call even when the module is disabled
    /// (no configuration asset found). Queries return default/empty values;
    /// writes are silent no-ops.
    /// </summary>
    public static class ItemDatabase
    {
        private static bool _checkedEnabled;
        private static bool _enabled;

        /// <summary>True if the ItemDatabase module is active (config found).</summary>
        public static bool IsEnabled
        {
            get
            {
                if (!_checkedEnabled)
                {
                    _enabled = ItemDatabaseConfiguration.Instance != null;
                    _checkedEnabled = true;
                }
                return _enabled;
            }
        }

        private static ItemDatabaseDirector EnsureDirector()
            => IsEnabled ? ItemDatabaseDirector.Instance : null;

        private static async Task<bool> EnsureReadyAsync()
        {
            if (!IsEnabled) return false;
            await ItemDatabaseDirector.ReadyTask;
            return true;
        }

        // ═══════════════════════════════════════════════════════
        // QUERY API (synchronous, lock-free)
        // ═══════════════════════════════════════════════════════

        public static InventoryItem? GetItem(string key)
        {
            var director = EnsureDirector();
            return director != null ? director.Engine.GetItem(key) : null;
        }

        /// <summary>Get typed tag data for an item via the [TagData] attribute.</summary>
        public static T GetTag<T>(string key) where T : class, ITagData
        {
            var director = EnsureDirector();
            if (director == null) return null;
            var tagDefType = TagDataRegistry.GetTagDefType(typeof(T));
            return tagDefType != null
                ? director.Engine.GetTagData<T>(key, tagDefType)
                : null;
        }

        /// <summary>Get all non-marker tag data attached to an item.</summary>
        public static ITagData[] GetTags(string key)
        {
            var director = EnsureDirector();
            return director != null ? director.Engine.GetTagDatas(key) : Array.Empty<ITagData>();
        }

        /// <summary>Check if item has a specific TagDefinition (marker or data).</summary>
        public static bool HasTag<TTagDef>(string key) where TTagDef : TagDefinition
        {
            var director = EnsureDirector();
            if (director == null) return false;

            var dataType = TagDataRegistry.GetDataType(typeof(TTagDef));
            if (dataType != null)
                return director.Engine.GetTagData(key, typeof(TTagDef)) != null;
            else
            {
                // Marker tag: check the item's definition
                var item = GetItem(key);
                if (!item.HasValue) return false;
                var def = GetDefinition(item.Value.itemId);
                if (def == null) return false;
                foreach (var tagDef in def.Tags)
                    if (tagDef.GetType() == typeof(TTagDef)) return true;
                return false;
            }
        }

        public static bool HasItem(string key)
        {
            var director = EnsureDirector();
            return director != null && director.Engine.HasItem(key);
        }

        public static int Count
        {
            get
            {
                var director = EnsureDirector();
                return director != null ? director.Engine.Count : 0;
            }
        }

        /// <summary>Snapshot of every inventory item (used by sweep middleware).</summary>
        public static InventoryItem[] AllItems
        {
            get
            {
                var director = EnsureDirector();
                if (director == null) return Array.Empty<InventoryItem>();
                var items = new List<InventoryItem>();
                foreach (var kvp in director.Engine.Items) items.Add(kvp.Value);
                return items.ToArray();
            }
        }

        public static ItemQueryBuilder Query()
        {
            var director = EnsureDirector();
            return new ItemQueryBuilder(director != null ? director.Engine : null);
        }

        // ═══════════════════════════════════════════════════════
        // WRITE API (enqueued, async — no-ops when disabled)
        // ═══════════════════════════════════════════════════════

        public static async Task AddItemAsync(
            InventoryItem item,
            CancellationToken ct = default)
        {
            if (!await EnsureReadyAsync()) return;
            await ItemDatabaseDirector.Instance.AddItemAsync(item, ct);
        }

        public static async Task RemoveItemAsync(
            string key,
            CancellationToken ct = default)
        {
            if (!await EnsureReadyAsync()) return;
            await ItemDatabaseDirector.Instance.RemoveItemAsync(key, ct);
        }

        public static async Task SetTagAsync<T>(
            string key, T data,
            CancellationToken ct = default)
            where T : ITagData
        {
            if (!await EnsureReadyAsync()) return;
            var tagDefType = TagDataRegistry.GetTagDefType(typeof(T));
            if (tagDefType == null)
                throw new ItemDatabaseException(
                    $"Type {typeof(T).Name} has no [TagData] attribute");
            await ItemDatabaseDirector.Instance.SetTagAsync(key, tagDefType, data, ct);
        }

        public static async Task RemoveTagAsync(
            string key, Type tagDefType,
            CancellationToken ct = default)
        {
            if (!await EnsureReadyAsync()) return;
            await ItemDatabaseDirector.Instance.RemoveTagAsync(key, tagDefType, ct);
        }

        // ═══════════════════════════════════════════════════════
        // FIRE-AND-FORGET (no-ops when disabled)
        // ═══════════════════════════════════════════════════════

        public static async void AddItem(InventoryItem item)
        {
            if (!IsEnabled) return;
            try { await ItemDatabaseDirector.Instance.AddItemAsync(item); }
            catch (Exception ex) { QuickLog.Error<ItemDatabaseDirector>("AddItem failed: {0}", ex); }
        }

        public static async void RemoveItem(string key)
        {
            if (!IsEnabled) return;
            try { await ItemDatabaseDirector.Instance.RemoveItemAsync(key); }
            catch (Exception ex) { QuickLog.Error<ItemDatabaseDirector>("RemoveItem failed: {0}", ex); }
        }

        // ═══════════════════════════════════════════════════════
        // SYNC CONTROL
        // ═══════════════════════════════════════════════════════

        public static async Task ForceSyncAsync(
            CancellationToken ct = default)
        {
            if (!await EnsureReadyAsync()) return;
            await ItemDatabaseDirector.Instance.Syncer.ForceSyncAsync(ct);
        }

        // ═══════════════════════════════════════════════════════
        // DEFINITION LOOKUP
        // ═══════════════════════════════════════════════════════

        public static ItemDefinition GetDefinition(string itemId)
        {
            var director = EnsureDirector();
            return director != null ? director.GetDefinition(itemId) : null;
        }

        // ═══════════════════════════════════════════════════════
        // OWNERSHIP QUERIES ([OwnedByModule])
        // ═══════════════════════════════════════════════════════

        /// <summary>Returns the owner module name, or null if unowned.</summary>
        public static string GetTagOwner(Type tagDefType)
        {
            if (tagDefType == null) return null;
            var attr = tagDefType.GetCustomAttribute<OwnedByModuleAttribute>();
            return attr?.ModuleName;
        }

        /// <summary>Returns the owner module name, or null if unowned.</summary>
        public static string GetDefinitionOwner(string itemId)
        {
            var def = GetDefinition(itemId);
            if (def == null) return null;

            foreach (var tagDef in def.Tags)
            {
                var owner = GetTagOwner(tagDef.GetType());
                if (owner != null) return owner;
            }

            return null;
        }

        /// <summary>True if the tag is owned by the given module.</summary>
        public static bool IsTagOwnedBy(Type tagDefType, string moduleName)
        {
            var owner = GetTagOwner(tagDefType);
            return owner != null && owner == moduleName;
        }

        /// <summary>True if the definition is owned by the given module.</summary>
        public static bool IsDefinitionOwnedBy(string itemId, string moduleName)
        {
            var owner = GetDefinitionOwner(itemId);
            return owner != null && owner == moduleName;
        }
    }
}
