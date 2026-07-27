using System.Threading;
using System.Threading.Tasks;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public interface ILeaderboardProvider
    {
        string ProviderId { get; }
        bool IsAvailable { get; }
        Task<bool> InitializeAsync();

        Task ReportScoreAsync(
            string leaderboardId,
            long score,
            CancellationToken ct = default);

        Task<LeaderboardEntry[]> FetchLeaderboardAsync(
            string leaderboardId,
            int count,
            CancellationToken ct = default);

        Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            CancellationToken ct = default);
    }
}
