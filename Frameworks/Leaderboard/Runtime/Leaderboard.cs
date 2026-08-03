using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public class Leaderboard
    {
        #region Initialization Guard

        private static void EnsureReady()
        {
            LeaderboardDirector director = LeaderboardDirector.Instance
                ?? throw new LeaderboardNotInitializedException(
                    "Leaderboard director is not available. Ensure a LeaderboardConfiguration exists with a provider."
                );

            if (director.Status == LeaderboardDirector.LeaderboardManagerStatus.Ready) return;
            throw new LeaderboardNotInitializedException();
        }

        private static async Task EnsureReadyAsync()
        {
            await Task.CompletedTask;
            EnsureReady();
        }

        #endregion

        #region Async API

        public static async Task ReportScoreAsync(
            string leaderboardId,
            long score,
            string metadata = null,
            LeaderboardScoreSubmissionMode mode = LeaderboardScoreSubmissionMode.Best,
            CancellationToken ct = default
        )
        {
            await EnsureReadyAsync();
            await LeaderboardDirector
                .Instance
                .ReportScoreAsync(leaderboardId, score, metadata, mode, ct);
        }

        public static async Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId, int index, int size,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            await EnsureReadyAsync();
            return await LeaderboardDirector
                .Instance
                .FetchLeaderboardAsync(leaderboardId, index, size, ct, timeframe);
        }

        public static async Task<LeaderboardResult> FetchLeaderboardAroundPlayerAsync(
            string leaderboardId, int radius,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            await EnsureReadyAsync();
            return await LeaderboardDirector
                .Instance
                .FetchLeaderboardAroundPlayerAsync(leaderboardId, radius, ct, timeframe);
        }

        public static async Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime)
        {
            await EnsureReadyAsync();
            return await LeaderboardDirector
                .Instance
                .FetchPlayerEntryAsync(leaderboardId, ct, timeframe);
        }

        #endregion

        #region Fire-and-Forget

        public static async void ReportScore(
            string leaderboardId,
            long score,
            string metadata = null,
            LeaderboardScoreSubmissionMode mode = LeaderboardScoreSubmissionMode.Best
        )
        {
            try
            {
                await EnsureReadyAsync();
                await LeaderboardDirector
                    .Instance
                    .ReportScoreAsync(leaderboardId, score, metadata, mode);
            }
            catch (Exception ex)
            {
                QuickLog.Critical<Leaderboard>(
                    "Report score failed for {0} due to exception: {1}",
                    leaderboardId, ex
                );
            }
        }

        public static async void FetchLeaderboard(
            string leaderboardId, int index, int size,
            Action<LeaderboardResult> onFetched,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            try
            {
                await EnsureReadyAsync();
                LeaderboardResult result = await LeaderboardDirector
                    .Instance
                    .FetchLeaderboardAsync(leaderboardId, index, size,
                        timeframe: timeframe);
                onFetched?.Invoke(result);
            }
            catch (Exception ex)
            {
                QuickLog.Critical<Leaderboard>(
                    "Fetch leaderboard failed for {0} due to exception: {1}",
                    leaderboardId, ex
                );
            }
        }

        public static async void FetchLeaderboardAroundPlayer(
            string leaderboardId, int radius,
            Action<LeaderboardResult> onFetched,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            try
            {
                await EnsureReadyAsync();
                LeaderboardResult result = await LeaderboardDirector
                    .Instance
                    .FetchLeaderboardAroundPlayerAsync(leaderboardId, radius,
                        timeframe: timeframe);
                onFetched?.Invoke(result);
            }
            catch (Exception ex)
            {
                QuickLog.Critical<Leaderboard>(
                    "Fetch leaderboard around player failed for {0} due to exception: {1}",
                    leaderboardId, ex
                );
            }
        }

        public static async void FetchPlayerEntry(
            string leaderboardId,
            Action<LeaderboardEntry> onFetched,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            try
            {
                await EnsureReadyAsync();
                LeaderboardEntry entry = await LeaderboardDirector
                    .Instance
                    .FetchPlayerEntryAsync(leaderboardId,
                        timeframe: timeframe);

                onFetched?.Invoke(entry);
            }
            catch (Exception ex)
            {
                QuickLog.Critical<Leaderboard>(
                    "Fetch player leaderboard information failed for {0} due to exception: {1}",
                    leaderboardId, ex
                );
            }
        }

        #endregion

        #region Feature Query

        /// <summary>
        /// Returns <c>true</c> if the active provider supports the given feature.
        /// Safe to call before initialization.
        /// </summary>
        public static bool Supports(LeaderboardProviderFeatures feature)
        {
            LeaderboardDirector director = LeaderboardDirector.Instance;
            return director != null && director.SupportsFeature(feature);
        }

        /// <summary>
        /// Returns <c>true</c> if the active provider natively supports
        /// the given timeframe (without fallback). For example,
        /// <c>Leaderboard.SupportsTimeframe(LeaderboardTimeframe.Monthly)</c>
        /// returns <c>false</c> on GPGS and Game Center.
        /// </summary>
        public static bool SupportsTimeframe(LeaderboardTimeframe timeframe)
        {
            LeaderboardDirector director = LeaderboardDirector.Instance;
            return director != null && director.SupportsTimeframe(timeframe);
        }

        #endregion
    }

}
