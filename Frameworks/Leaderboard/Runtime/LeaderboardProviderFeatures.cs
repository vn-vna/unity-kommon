using System;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    /// <summary>
    /// Bitmask of features a <see cref="ILeaderboardProvider"/> supports.
    /// Mirrors the <c>TrackingProviderFeatures</c> pattern in the Tracking module.
    /// Query with <c>Leaderboard.Supports(LeaderboardProviderFeatures.TimeFrameWeekly)</c>
    /// or <c>Leaderboard.SupportsTimeframe(LeaderboardTimeframe)</c>.
    /// </summary>
    [Flags]
    public enum LeaderboardProviderFeatures
    {
        None = 0,

        ReportScore = 1 << 0,

        FetchTopScores = 1 << 1,

        FetchAroundPlayer = 1 << 2,

        FetchPlayerEntry = 1 << 3,

        TimeFrameDaily = 1 << 4,

        TimeFrameWeekly = 1 << 5,

        TimeFrameMonthly = 1 << 6,

        TimeFrameAllTime = 1 << 7,

        AllTimeFrames = TimeFrameDaily
            | TimeFrameWeekly
            | TimeFrameMonthly
            | TimeFrameAllTime,

        AllFeatures = ReportScore
            | FetchTopScores
            | FetchAroundPlayer
            | FetchPlayerEntry
            | AllTimeFrames
    }
}
