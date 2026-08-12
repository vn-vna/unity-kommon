using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Middlewares
{
    [ItemDatabaseMiddleware(DefaultPriority = 0,
        Description = "Gatekeeper — validates item integrity before every operation: non-empty keys, existing definitions, allowed tags.")]
    public class ValidationMiddleware :
        IItemDatabaseBeforeAddMiddleware,
        IItemDatabaseBeforeRemoveMiddleware,
        IItemDatabaseBeforeSetTagMiddleware
    {
        public int Priority => 0;

        public void OnBeforeAdd(InventoryItem item)
        {
            if (string.IsNullOrEmpty(item.key))
                throw new ItemDatabaseException("Item key cannot be empty");
            if (ItemDatabase.GetDefinition(item.itemId) == null)
                throw new ItemDatabaseException($"Definition '{item.itemId}' not found");
        }

        public void OnBeforeRemove(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ItemDatabaseException("Remove key cannot be empty");
        }

        public void OnBeforeSetTag(string key, Type tagDefType, ITagData data)
        {
            if (!ItemDatabase.HasItem(key))
                throw new ItemDatabaseException($"Item '{key}' not found");
            var item = ItemDatabase.GetItem(key);
            if (!item.HasValue) return;
            var def = ItemDatabase.GetDefinition(item.Value.itemId);
            if (def == null) return;
            bool allowed = false;
            foreach (var tagDef in def.Tags)
                if (tagDef.GetType() == tagDefType) { allowed = true; break; }
            if (!allowed)
                throw new ItemDatabaseException($"Tag '{tagDefType.Name}' not allowed on '{item.Value.itemId}'");
        }
    }
}
