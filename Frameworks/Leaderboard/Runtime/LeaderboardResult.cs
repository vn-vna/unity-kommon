using System;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    [Serializable]
    public readonly struct LeaderboardResult
    {
        public static readonly LeaderboardResult Empty = new LeaderboardResult();

        public LeaderboardEntry[] Entries { get; }
        public int TotalPlayers { get; }
        public int PlayerEntryIndex { get; }
        public int? PlayerRank { get; }

        public LeaderboardResult(
            LeaderboardEntry[] entries,
            int totalPlayers,
            int playerEntryIndex,
            int? playerRank)
        {
            Entries = entries ?? Array.Empty<LeaderboardEntry>();
            TotalPlayers = totalPlayers;
            PlayerEntryIndex = playerEntryIndex;
            PlayerRank = playerRank;
        }

    }
}
