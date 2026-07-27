using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public static class Leaderboard
    {
        #region Initialization Guard

        private static async Task EnsureReadyAsync()
        {
            await LeaderboardDirector.ReadyTask;
        }

        #endregion

        #region Async API

        public static async Task ReportScoreAsync(
            string leaderboardId,
            long score,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            await LeaderboardDirector.Instance.ReportScoreAsync(
                leaderboardId, score, ct);
        }

        public static async Task<LeaderboardEntry[]> FetchLeaderboardAsync(
            string leaderboardId,
            int count,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            return await LeaderboardDirector.Instance.FetchLeaderboardAsync(
                leaderboardId, count, ct);
        }

        public static async Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();
            return await LeaderboardDirector.Instance.FetchPlayerEntryAsync(
                leaderboardId, ct);
        }

        #endregion

        #region Fire-and-Forget

        public static async void ReportScore(
            string leaderboardId,
            long score)
        {
            try
            {
                await EnsureReadyAsync();
                await LeaderboardDirector.Instance.ReportScoreAsync(
                    leaderboardId, score);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[Leaderboard] ReportScore failed for '{leaderboardId}': {ex}");
            }
        }

        public static async void FetchLeaderboard(
            string leaderboardId,
            int count,
            Action<LeaderboardEntry[]> onFetched)
        {
            try
            {
                await EnsureReadyAsync();
                var entries =
                    await LeaderboardDirector.Instance.FetchLeaderboardAsync(
                        leaderboardId, count);
                onFetched?.Invoke(entries);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[Leaderboard] FetchLeaderboard failed for '{leaderboardId}': {ex}");
            }
        }

        #endregion
    }
}
