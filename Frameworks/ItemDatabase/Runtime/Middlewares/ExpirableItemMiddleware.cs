using System;
using System.Threading;
using System.Threading.Tasks;
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
        Description = "Rejects expired payloads. The director schedules atomic expiry purges automatically.")]
    public class ExpirableItemMiddleware : IItemDatabaseBeforeSetTagMiddleware
    {
        public int Priority => 200;

        public void OnBeforeSetTag(string key, Type tagDefType, ITagData data)
        {
            if (tagDefType != typeof(ExpirableTag)) return;
            if (data is not ExpirableData expirable) return;
            if (!expirable.IsExpired) return;

            throw new ItemDatabaseException(
                ItemDatabaseErrorCode.InvalidTagData,
                $"Cannot set expired ExpirableData on item '{key}' — "
                + $"expiresAt {expirable.expiresAt:o} is in the past."
            );
        }

        /// <summary>
        /// Removes every item whose ExpirableData has already passed.
        /// Returns the number of removed items. Safe to call on a timer.
        /// </summary>
        [Obsolete("Use ExpireNowAsync and await completion.", true)]
        public int ExpireNow()
        {
            _ = ExpireNowAsync();
            return 0;
        }

        public async Task<int> ExpireNowAsync(
            CancellationToken cancellationToken = default)
        {
            ItemOperationResult result = await ItemDatabase.ExpireNowAsync(
                cancellationToken
            );
            result.ThrowIfRejected();

            int removed = 0;
            foreach (ItemStackDelta delta in result.Deltas)
            {
                if (delta.WasRemoved) removed++;
            }

            if (removed > 0)
            {
                QuickLog.Info<ExpirableItemMiddleware>(
                    "Expired {0} item stacks",
                    removed
                );
            }

            return removed;
        }
    }
}
