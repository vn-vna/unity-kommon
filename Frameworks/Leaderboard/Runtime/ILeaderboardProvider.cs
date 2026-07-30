using System.Threading;
using System.Threading.Tasks;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public interface ILeaderboardProvider
    {
        string ProviderId { get; }
        bool IsAvailable { get; }
        bool IsInitialized { get; }

        Task<bool> InitializeAsync();

        Task ReportScoreAsync(
            string leaderboardId,
            long score,
            string metadata,
            LeaderboardType type,
            ScoreSubmissionMode mode,
            CancellationToken ct = default
        );

        Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId,
            int index,
            int size,
            LeaderboardType type,
            CancellationToken ct = default
        );

        Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            LeaderboardType type,
            CancellationToken ct = default
        );
    }
}
