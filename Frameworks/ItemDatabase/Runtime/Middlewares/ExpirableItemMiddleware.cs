using System;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Middlewares
{
    /// <summary>
    /// Guards expirable items. Runs as a real middleware hook: it rejects
    /// attempts to set an already-expired <see cref="ExpirableData"/>, so
    /// expired batches can never enter the store. Also exposes
    /// <see cref="ExpireNow"/> as a manual/timer-driven sweep that removes
    /// every expired item from the database.
    /// </summary>
    [ItemDatabaseMiddleware(DefaultPriority = 200,
        Description = "Rejects expired expirable batches and sweeps expired items. Use ExpireNow() on a timer to purge.")]
    public class ExpirableItemMiddleware : IItemDatabaseBeforeSetTagMiddleware
    {
        public int Priority => 200;

        public void OnBeforeSetTag(string key, Type tagDefType, ITagData data)
        {
            if (tagDefType != typeof(ExpirableTag)) return;
            if (data is not ExpirableData expirable) return;
            if (!expirable.IsExpired) return;

            throw new ItemDatabaseException(
                $"Cannot set expired ExpirableData on item '{key}' — " +
                $"expiresAt {expirable.expiresAt:o} is in the past.");
        }

        /// <summary>
        /// Removes every item whose ExpirableData has already passed.
        /// Returns the number of removed items. Safe to call on a timer.
        /// </summary>
        public int ExpireNow()
        {
            int removed = 0;
            var now = DateTime.UtcNow;
            foreach (var item in ItemDatabase.AllItems)
            {
                var expData = ItemDatabaseDirector.Instance.Engine
                    .GetTagData(item.key, typeof(ExpirableTag)) as ExpirableData;
                if (expData != null && expData.expiresAt <= now)
                {
                    ItemDatabase.RemoveItem(item.key);
                    removed++;
                }
            }

            if (removed > 0) QuickLog.Info<ExpirableItemMiddleware>("Expired {0} items", removed);
            return removed;
        }
    }
}
