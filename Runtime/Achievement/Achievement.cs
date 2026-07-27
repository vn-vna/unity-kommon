using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Achievement
{
    public static class Achievement
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
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            await AchievementDirector.Instance.UnlockAsync(
                achievementId, ct);
        }

        public static async Task ReportProgressAsync(
            string achievementId,
            double progress,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            await AchievementDirector.Instance.ReportProgressAsync(
                achievementId, progress, ct);
        }

        public static async Task<AchievementState> GetStateAsync(
            string achievementId,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            return await AchievementDirector.Instance.GetStateAsync(
                achievementId, ct);
        }

        public static async Task<AchievementState[]> GetAllStatesAsync(
            CancellationToken ct = default)
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
                Debug.LogError(
                    $"[Achievement] Unlock failed for '{achievementId}': {ex}");
            }
        }

        public static async void ReportProgress(
            string achievementId,
            double progress)
        {
            try
            {
                await EnsureReadyAsync();
                await AchievementDirector.Instance.ReportProgressAsync(
                    achievementId, progress);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[Achievement] ReportProgress failed for '{achievementId}': {ex}");
            }
        }

        #endregion
    }
}
