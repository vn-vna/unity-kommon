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
            if (string.IsNullOrWhiteSpace(item.key))
            {
                throw new ItemDatabaseException(
                    ItemDatabaseErrorCode.InvalidItemKey,
                    "Item key cannot be empty."
                );
            }

            if (string.IsNullOrWhiteSpace(item.itemId))
            {
                throw new ItemDatabaseException(
                    ItemDatabaseErrorCode.InvalidItemId,
                    "Item ID cannot be empty."
                );
            }
        }

        public void OnBeforeRemove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ItemDatabaseException(
                    ItemDatabaseErrorCode.InvalidItemKey,
                    "Remove key cannot be empty."
                );
            }
        }

        public void OnBeforeSetTag(string key, Type tagDefType, ITagData data)
        {
            if (tagDefType == null || data == null)
            {
                throw new ItemDatabaseException(
                    ItemDatabaseErrorCode.InvalidTagData,
                    "Tag type and data are required."
                );
            }
        }
    }
}
