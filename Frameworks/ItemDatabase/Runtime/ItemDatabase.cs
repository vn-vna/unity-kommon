using System;
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
        /// <summary>True if the ItemDatabase module is active (config found).</summary>
        public static bool IsEnabled
            => ItemDatabaseConfiguration.LoadCanonical() != null;

        public static ItemDatabaseLifecycleState State
            => ItemDatabaseDirector.State;

        public static Exception InitializationException
            => ItemDatabaseDirector.InitializationException;

        public static Task<ItemDatabaseInitializationResult> InitializationTask
            => ItemDatabaseDirector.InitializationTask;

        private static ItemDatabaseDirector TryGetReadyDirector()
        {
            ItemDatabaseDirector director = ItemDatabaseDirector.Instance;
            return ItemDatabaseDirector.State == ItemDatabaseLifecycleState.Ready
                && director != null
                && director.IsReady
                ? director
                : null;
        }

        private static async Task<ItemDatabaseDirector> GetReadyDirectorAsync(
            CancellationToken cancellationToken = default)
        {
            if (!IsEnabled) return null;

            bool ready = await ItemDatabaseTaskUtility.WaitAsync(
                ItemDatabaseDirector.ReadyTask,
                cancellationToken
            );
            return ready ? TryGetReadyDirector() : null;
        }

        private static ItemOperationResult CreateUnavailableResult()
        {
            return !IsEnabled
                ? ItemOperationResult.Disabled()
                : ItemOperationResult.Unavailable(
                    $"Item Database is {State} and cannot accept commands."
                );
        }

        // ═══════════════════════════════════════════════════════
        // QUERY API (synchronous, lock-free)
        // ═══════════════════════════════════════════════════════

        public static InventoryItem? GetItem(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            ItemDatabaseDirector director = TryGetReadyDirector();
            return director != null ? director.Engine.GetItem(key) : null;
        }

        /// <summary>Get typed tag data for an item via the [TagData] attribute.</summary>
        public static T GetTag<T>(string key) where T : class, ITagData
        {
            if (string.IsNullOrEmpty(key)) return null;
            ItemDatabaseDirector director = TryGetReadyDirector();
            if (director == null) return null;
            var tagDefType = TagDataRegistry.GetTagDefType(typeof(T));
            return tagDefType != null
                ? director.Engine.GetTagData<T>(key, tagDefType)
                : null;
        }

        /// <summary>Get all non-marker tag data attached to an item.</summary>
        public static ITagData[] GetTags(string key)
        {
            if (string.IsNullOrEmpty(key)) return Array.Empty<ITagData>();
            ItemDatabaseDirector director = TryGetReadyDirector();
            return director != null ? director.Engine.GetTagDatas(key) : Array.Empty<ITagData>();
        }

        /// <summary>Check if item has a specific TagDefinition (marker or data).</summary>
        public static bool HasTag<TTagDef>(string key) where TTagDef : TagDefinition
        {
            if (string.IsNullOrEmpty(key)) return false;
            ItemDatabaseDirector director = TryGetReadyDirector();
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
                {
                    if (tagDef != null && tagDef.GetType() == typeof(TTagDef))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static bool HasItem(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            ItemDatabaseDirector director = TryGetReadyDirector();
            return director != null && director.Engine.HasItem(key);
        }

        public static int Count
        {
            get
            {
                ItemDatabaseDirector director = TryGetReadyDirector();
                return director != null ? director.Engine.Count : 0;
            }
        }

        /// <summary>Snapshot of every inventory item (used by sweep middleware).</summary>
        public static InventoryItem[] AllItems
        {
            get
            {
                ItemDatabaseDirector director = TryGetReadyDirector();
                return director != null
                    ? director.Engine.GetItemsSnapshot()
                    : Array.Empty<InventoryItem>();
            }
        }

        public static ItemQueryBuilder Query()
        {
            ItemDatabaseDirector director = TryGetReadyDirector();
            return new ItemQueryBuilder(director != null ? director.Engine : null);
        }

        // ═══════════════════════════════════════════════════════
        // WRITE API (enqueued, async — no-ops when disabled)
        // ═══════════════════════════════════════════════════════

        public static async Task AddItemAsync(
            InventoryItem item,
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return;
            await director.AddItemAsync(item, ct);
        }

        public static async Task<ItemOperationResult> AddItemAsync(
            AddItemRequest request,
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return CreateUnavailableResult();
            return await director.AddItemAsync(request, ct);
        }

        public static async Task RemoveItemAsync(
            string key,
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return;
            await director.RemoveItemAsync(key, ct);
        }

        public static async Task<ItemOperationResult> TryRemoveItemAsync(
            string key,
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return CreateUnavailableResult();
            return await director.TryRemoveItemAsync(key, ct);
        }

        public static async Task<ItemOperationResult> RemoveQuantityAsync(
            string key,
            int quantity,
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return CreateUnavailableResult();
            return await director.RemoveQuantityAsync(
                key,
                quantity,
                ct
            );
        }

        public static async Task SetTagAsync<T>(
            string key, T data,
            CancellationToken ct = default)
            where T : ITagData
        {
            if (data == null)
            {
                throw new ItemDatabaseException("Tag data cannot be null.");
            }

            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return;
            Type dataType = data.GetType();
            Type tagDefType = TagDataRegistry.GetTagDefType(dataType);
            if (tagDefType == null)
                throw new ItemDatabaseException(
                    $"Type {dataType.Name} has no [TagData] attribute");
            await director.SetTagAsync(key, tagDefType, data, ct);
        }

        public static async Task<ItemOperationResult> TrySetTagAsync<T>(
            string key,
            T data,
            CancellationToken ct = default)
            where T : ITagData
        {
            if (data == null)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidTagData,
                    "Tag data cannot be null."
                );
            }

            Type dataType = data.GetType();
            Type tagDefinitionType = TagDataRegistry.GetTagDefType(dataType);
            if (tagDefinitionType == null)
            {
                return ItemOperationResult.Rejected(
                    ItemDatabaseErrorCode.InvalidTagData,
                    $"Type '{dataType.Name}' has no [TagData] mapping."
                );
            }

            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return CreateUnavailableResult();
            return await director.TrySetTagAsync(
                key,
                tagDefinitionType,
                data,
                ct
            );
        }

        public static async Task RemoveTagAsync(
            string key, Type tagDefType,
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return;
            await director.RemoveTagAsync(key, tagDefType, ct);
        }

        public static async Task<ItemOperationResult> TryRemoveTagAsync(
            string key,
            Type tagDefinitionType,
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return CreateUnavailableResult();
            return await director.TryRemoveTagAsync(
                key,
                tagDefinitionType,
                ct
            );
        }

        public static async Task<ItemOperationResult> ExpireNowAsync(
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return CreateUnavailableResult();
            return await director.ExpireNowAsync(ct);
        }

        // ═══════════════════════════════════════════════════════
        // FIRE-AND-FORGET (no-ops when disabled)
        // ═══════════════════════════════════════════════════════

        [Obsolete("Use AddItemAsync and await the returned Task.")]
        public static async void AddItem(InventoryItem item)
        {
            try
            {
                await AddItemAsync(item);
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseDirector>(
                    "AddItem failed: {0}",
                    exception
                );
            }
        }

        [Obsolete("Use RemoveItemAsync and await the returned Task.")]
        public static async void RemoveItem(string key)
        {
            try
            {
                await RemoveItemAsync(key);
            }
            catch (Exception exception)
            {
                QuickLog.Error<ItemDatabaseDirector>(
                    "RemoveItem failed: {0}",
                    exception
                );
            }
        }

        // ═══════════════════════════════════════════════════════
        // SYNC CONTROL
        // ═══════════════════════════════════════════════════════

        public static async Task ForceSyncAsync(
            CancellationToken ct = default)
        {
            ItemDatabaseDirector director = await GetReadyDirectorAsync(ct);
            if (director == null) return;
            await director.ForceSyncAsync(ct);
        }

        // ═══════════════════════════════════════════════════════
        // DEFINITION LOOKUP
        // ═══════════════════════════════════════════════════════

        public static ItemDefinition GetDefinition(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            ItemDatabaseDirector director = TryGetReadyDirector();
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
            return string.IsNullOrWhiteSpace(attr?.ModuleName)
                ? null
                : attr.ModuleName;
        }

        /// <summary>Returns the owner module name, or null if unowned.</summary>
        public static string GetDefinitionOwner(string itemId)
        {
            var def = GetDefinition(itemId);
            return GetDefinitionOwner(def);
        }

        public static string GetDefinitionOwner(ItemDefinition definition)
        {
            if (definition == null) return null;

            foreach (var tagDef in definition.Tags)
            {
                if (tagDef == null) continue;
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
