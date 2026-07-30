using System;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    [Serializable]
    public struct LeaderboardEntry
    {
        public int Rank;
        public string PlayerId;
        public string PlayerName;
        public long Score;
        public DateTime Timestamp;
        public string Metadata;

        public LeaderboardEntry(
            int rank,
            string playerId,
            string playerName,
            long score,
            DateTime timestamp,
            string metadata = null
        )
        {
            Rank = rank;
            PlayerId = playerId;
            PlayerName = playerName;
            Score = score;
            Timestamp = timestamp;
            Metadata = metadata ?? string.Empty;
        }
    }
}
