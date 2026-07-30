using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{
    public class Achievement
    {
        #region Initialization Guard

        private static async Task EnsureReadyAsync()
        {
            await AchievementDirector.ReadyTask;
        }

        #endregion

        #region Async API

        public static async Task UnlockAsync(
            string achievementId,
            CancellationToken ct = default
        )
        {
            await EnsureReadyAsync();
            await AchievementDirector.Instance.UnlockAsync(achievementId, ct);
        }

        public static async Task IncrementAchievementAsync(
            string achievementId,
            int steps,
            CancellationToken ct = default
        )
        {
            await EnsureReadyAsync();
            await AchievementDirector.Instance.IncrementAchievementAsync(achievementId, steps, ct);
        }

        public static async Task<AchievementState> GetStateAsync(
            string achievementId,
            CancellationToken ct = default
        )
        {
            await EnsureReadyAsync();
            return await AchievementDirector.Instance.GetStateAsync(achievementId, ct);
        }

        public static async Task<AchievementState[]> GetAllStatesAsync(CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            return await AchievementDirector.Instance.GetAllStatesAsync(ct);
        }

        #endregion

        #region Fire-and-Forget

        public static async void Unlock(string achievementId)
        {
            try
            {
                await EnsureReadyAsync();
                await AchievementDirector.Instance.UnlockAsync(achievementId);
            }
            catch (Exception ex)
            {
                QuickLog.Error<Achievement>("Unlock failed for '{0}': {1}", achievementId, ex);
            }
        }

        public static async void IncrementAchievement(
            string achievementId,
            int steps
        )
        {
            try
            {
                await EnsureReadyAsync();
                await AchievementDirector.Instance.IncrementAchievementAsync(achievementId, steps);
            }
            catch (Exception ex)
            {
                QuickLog.Error<Achievement>("IncrementAchievement failed for '{0}': {1}", achievementId, ex);
            }
        }

        #endregion
    }
}
