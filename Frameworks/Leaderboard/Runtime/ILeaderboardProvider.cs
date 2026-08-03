using System.Threading;
using System.Threading.Tasks;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public interface ILeaderboardProvider
    {
        string ProviderId { get; }
        bool IsAvailable { get; }
        bool IsInitialized { get; }

        /// <summary>
        /// Bitmask of features this provider supports. Query with
        /// <c>Leaderboard.Supports(LeaderboardProviderFeatures.TimeFrameWeekly)</c>
        /// or <c>Leaderboard.SupportsTimeframe(LeaderboardTimeframe)</c>.
        /// </summary>
        LeaderboardProviderFeatures Features { get; }

        Task<bool> InitializeAsync();

        Task ReportScoreAsync(
            string leaderboardId,
            long score,
            string metadata,
            LeaderboardType type,
            LeaderboardScoreSubmissionMode mode,
            CancellationToken ct = default
        );

        Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId,
            int index,
            int size,
            LeaderboardType type,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        );

        Task<LeaderboardResult> FetchLeaderboardAroundPlayerAsync(
            string leaderboardId,
            int radius,
            LeaderboardType type,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        );

        Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            LeaderboardType type,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        );
    }
}
