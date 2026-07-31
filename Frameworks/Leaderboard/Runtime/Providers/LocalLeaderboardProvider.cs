using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public class LocalLeaderboardProvider :
        ScriptableObject, ILeaderboardProvider
    {
        #region Constants

        private const int NotFoundRank = -1;
        private const int DefaultRank = 0;
        private const string DefaultPlayerName = "Player";

        #endregion

        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        private string _providerId = "local";

        [SerializeField]
        private string _storagePath = "LeaderboardData";

        #endregion

        #region ILeaderboardProvider

        public string ProviderId => _providerId;
        public bool IsAvailable => true;
        public bool IsInitialized => true;

        public Task<bool> InitializeAsync()
        {
            EnsureDirectory();
            return Task.FromResult(true);
        }

        public Task ReportScoreAsync(
            string leaderboardId,
            long score,
            string metadata,
            LeaderboardType type,
            LeaderboardScoreSubmissionMode mode,
            CancellationToken ct = default)
        {
            var entries = LoadEntries(leaderboardId);
            string playerId = SystemInfo.deviceUniqueIdentifier;

            LeaderboardEntry newEntry = new LeaderboardEntry(
                DefaultRank,
                playerId,
                DefaultPlayerName,
                score,
                DateTime.UtcNow,
                metadata
            );

            entries.Add(newEntry);
            SaveEntries(leaderboardId, entries);
            return Task.CompletedTask;
        }

        public Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId,
            int index,
            int size,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            var entries = LoadEntries(leaderboardId);
            string playerId = SystemInfo.deviceUniqueIdentifier;

            List<LeaderboardEntry> aggregated =
                AggregateBest(entries, type);

            bool isAscending = type == LeaderboardType.Duration;
            var sorted = isAscending
                ? aggregated.OrderBy(e => e.Score).ToList()
                : aggregated.OrderByDescending(e => e.Score).ToList();

            int totalPlayers = sorted.Count;

            for (int i = 0; i < sorted.Count; i++)
            {
                LeaderboardEntry entry = sorted[i];
                sorted[i] = new LeaderboardEntry(
                    i + 1,
                    entry.PlayerId,
                    entry.PlayerName,
                    entry.Score,
                    entry.Timestamp,
                    entry.Metadata
                );
            }

            int clampedIndex = Math.Max(0, Math.Min(index, sorted.Count));
            int clampedSize = Math.Max(1, size);
            int endIndex = Math.Min(clampedIndex + clampedSize, sorted.Count);

            var sliced = new LeaderboardEntry[endIndex - clampedIndex];
            int playerEntryIndex = NotFoundRank;

            for (int i = clampedIndex; i < endIndex; i++)
            {
                int slicePos = i - clampedIndex;
                sliced[slicePos] = sorted[i];

                if (playerEntryIndex < 0
                    && sliced[slicePos].PlayerId == playerId)
                {
                    playerEntryIndex = slicePos;
                }
            }

            int? playerRank = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].PlayerId == playerId)
                {
                    playerRank = i + 1;
                    break;
                }
            }

            return Task.FromResult(new LeaderboardResult(
                sliced, totalPlayers, playerEntryIndex, playerRank));
        }

        public Task<LeaderboardResult> FetchLeaderboardAroundPlayerAsync(
            string leaderboardId,
            int radius,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            var entries = LoadEntries(leaderboardId);
            string playerId = SystemInfo.deviceUniqueIdentifier;

            List<LeaderboardEntry> aggregated =
                AggregateBest(entries, type);

            bool isAscending = type == LeaderboardType.Duration;
            var sorted = isAscending
                ? aggregated.OrderBy(e => e.Score).ToList()
                : aggregated.OrderByDescending(e => e.Score).ToList();

            // Assign ranks
            for (int i = 0; i < sorted.Count; i++)
            {
                LeaderboardEntry entry = sorted[i];
                sorted[i] = new LeaderboardEntry(
                    i + 1,
                    entry.PlayerId,
                    entry.PlayerName,
                    entry.Score,
                    entry.Timestamp,
                    entry.Metadata
                );
            }

            // Find player rank
            int playerRank = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].PlayerId == playerId)
                {
                    playerRank = i + 1;
                    break;
                }
            }

            int fetchSize = (radius * 2) + 1;
            int totalPlayers = sorted.Count;

            if (playerRank <= 0)
            {
                // Player has no rank — fall back to top entries
                int clampedIndex = 0;
                int endIndex = Math.Min(fetchSize, totalPlayers);
                var sliced = new LeaderboardEntry[endIndex - clampedIndex];

                for (int i = clampedIndex; i < endIndex; i++)
                {
                    sliced[i - clampedIndex] = sorted[i];
                }

                return Task.FromResult(new LeaderboardResult(
                    sliced, totalPlayers, -1, null));
            }

            int startIndex = Math.Max(0, playerRank - radius - 1);
            int endIdx = Math.Min(startIndex + fetchSize, totalPlayers);

            var window = new LeaderboardEntry[endIdx - startIndex];
            int playerLocalIndex = -1;

            for (int i = startIndex; i < endIdx; i++)
            {
                int pos = i - startIndex;
                window[pos] = sorted[i];

                if (window[pos].PlayerId == playerId)
                {
                    playerLocalIndex = pos;
                }
            }

            return Task.FromResult(new LeaderboardResult(
                window, totalPlayers, playerLocalIndex, playerRank));
        }

        public Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            var entries = LoadEntries(leaderboardId);
            string playerId = SystemInfo.deviceUniqueIdentifier;

            var playerEntries = entries
                .Where(e => e.PlayerId == playerId)
                .ToArray();

            if (playerEntries.Length == 0)
            {
                return Task.FromResult(new LeaderboardEntry(
                    NotFoundRank, playerId, DefaultPlayerName,
                    0, DateTime.MinValue));
            }

            bool isAscending = type == LeaderboardType.Duration;

            LeaderboardEntry best = isAscending
                ? playerEntries.OrderBy(e => e.Score).First()
                : playerEntries.OrderByDescending(e => e.Score).First();

            List<LeaderboardEntry> aggregated =
                AggregateBest(entries, type);

            var sorted = isAscending
                ? aggregated.OrderBy(e => e.Score).ToList()
                : aggregated.OrderByDescending(e => e.Score).ToList();

            int rank = NotFoundRank;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].PlayerId == playerId)
                {
                    rank = i + 1;
                    break;
                }
            }

            return Task.FromResult(new LeaderboardEntry(
                rank,
                best.PlayerId,
                best.PlayerName,
                best.Score,
                best.Timestamp,
                best.Metadata
            ));
        }

        #endregion

        #region Private Methods — Aggregation

        private static List<LeaderboardEntry> AggregateBest(
            List<LeaderboardEntry> entries,
            LeaderboardType type)
        {
            bool isAscending = type == LeaderboardType.Duration;

            return entries
                .GroupBy(e => e.PlayerId)
                .Select(g => isAscending
                    ? g.OrderBy(e => e.Score).First()
                    : g.OrderByDescending(e => e.Score).First())
                .ToList();
        }

        #endregion

        #region Private Methods — Storage

        private string GetStorageDirectory()
        {
            return Path.Combine(
                Application.persistentDataPath,
                _storagePath
            );
        }

        private string GetFilePath(string leaderboardId)
        {
            return Path.Combine(
                GetStorageDirectory(),
                $"{leaderboardId}.json"
            );
        }

        private void EnsureDirectory()
        {
            string dir = GetStorageDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private List<LeaderboardEntry> LoadEntries(string leaderboardId)
        {
            string path = GetFilePath(leaderboardId);
            if (!File.Exists(path))
            {
                return new List<LeaderboardEntry>();
            }

            try
            {
                string json = File.ReadAllText(path);
                var wrapper =
                    JsonUtility.FromJson<LeaderboardSaveData>(json);
                return wrapper?.Entries ?? new List<LeaderboardEntry>();
            }
            catch
            {
                return new List<LeaderboardEntry>();
            }
        }

        private void SaveEntries(
            string leaderboardId,
            List<LeaderboardEntry> entries)
        {
            EnsureDirectory();
            string path = GetFilePath(leaderboardId);

            var wrapper = new LeaderboardSaveData { Entries = entries };
            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(path, json);
        }

        #endregion

        #region Nested Types

        [Serializable]
        private class LeaderboardSaveData
        {
            public List<LeaderboardEntry> Entries;
        }

        #endregion
    }
}
