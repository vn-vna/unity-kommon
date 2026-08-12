using System;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Middlewares
{
    [ItemDatabaseMiddleware(DefaultPriority = 100,
        Description = "Auto-merges stackable items into an existing batch with the same definition and expiry; non-stackable items create new slots.")]
    public class AutoStackMiddleware : IItemDatabaseBeforeAddMiddleware
    {
        public int Priority => 100;

        public void OnBeforeAdd(InventoryItem item)
        {
            var def = ItemDatabase.GetDefinition(item.itemId);
            if (def == null) return;

            bool isStackable = false;
            foreach (var tagDef in def.Tags)
                if (tagDef is StackableTag) { isStackable = true; break; }
            if (!isStackable) return;

            // Read the incoming item's expiry (if any)
            var engine = ItemDatabaseDirector.Instance.Engine;
            var incomingExpiry = engine
                .GetTagData(item.key, typeof(ExpirableTag)) as ExpirableData;

            var existing = ItemDatabase.Query()
                .WithDefinition(item.itemId).Execute();

            foreach (var exItem in existing.Items)
            {
                // For expirable items: only merge into a stack with the SAME expiry
                var exExpiry = engine
                    .GetTagData(exItem.key, typeof(ExpirableTag)) as ExpirableData;

                bool sameExpiry = (incomingExpiry == null && exExpiry == null)
                    || (incomingExpiry != null && exExpiry != null
                        && incomingExpiry.expiresAt == exExpiry.expiresAt);

                if (!sameExpiry) continue;

                // Found a matching stack — merge into it
                var stackData = engine
                    .GetTagData(exItem.key, typeof(StackableTag)) as StackableData;
                stackData ??= new StackableData { count = 1 };
                stackData.count++;

                engine.ApplySetTag(exItem.key, typeof(StackableTag), stackData);
                throw new ItemAlreadyStackedException(item.itemId, exItem.key);
            }
            // No matching stack found — let the new item be created as a new batch
        }
    }

    internal class ItemAlreadyStackedException : Exception
    {
        public string ItemId { get; }
        public string ExistingKey { get; }

        public ItemAlreadyStackedException(string itemId, string existingKey)
            : base($"Item '{itemId}' stacked onto '{existingKey}'")
        {
            ItemId = itemId;
            ExistingKey = existingKey;
        }
    }
}
