using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    [CreateAssetMenu(
        fileName = "LocalLeaderboardProvider",
        menuName = "Scheherazade/Leaderboard/Local Leaderboard Provider")]
    public class LocalLeaderboardProvider : ScriptableObject, ILeaderboardProvider
    {
        #region Serialized Fields

        [SerializeField]
        private string _providerId = "local";

        [SerializeField]
        private string _storagePath = "LeaderboardData";

        #endregion

        #region ILeaderboardProvider

        public string ProviderId => _providerId;
        public bool IsAvailable => true;

        public Task<bool> InitializeAsync()
        {
            EnsureDirectory();
            return Task.FromResult(true);
        }

        public Task ReportScoreAsync(
            string leaderboardId,
            long score,
            CancellationToken ct = default)
        {
            var entries = LoadEntries(leaderboardId);

            LeaderboardEntry newEntry = new LeaderboardEntry(
                default,
                SystemInfo.deviceUniqueIdentifier,
                "Player",
                score,
                DateTime.UtcNow
            );

            entries.Add(newEntry);
            SaveEntries(leaderboardId, entries);
            return Task.CompletedTask;
        }

        public Task<LeaderboardEntry[]> FetchLeaderboardAsync(
            string leaderboardId,
            int count,
            CancellationToken ct = default)
        {
            var entries = LoadEntries(leaderboardId);
            var sorted = entries
                .OrderByDescending(e => e.Score)
                .Take(count)
                .Select((entry, index) =>
                    new LeaderboardEntry(
                        index + 1,
                        entry.PlayerId,
                        entry.PlayerName,
                        entry.Score,
                        entry.Timestamp
                    ))
                .ToArray();

            return Task.FromResult(sorted);
        }

        public Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            CancellationToken ct = default)
        {
            const int notFoundRank = -1;
            string playerId = SystemInfo.deviceUniqueIdentifier;
            var entries = LoadEntries(leaderboardId);

            var playerEntries = entries
                .Where(e => e.PlayerId == playerId)
                .OrderByDescending(e => e.Score)
                .ToArray();

            if (playerEntries.Length == 0)
            {
                return Task.FromResult(new LeaderboardEntry(
                    notFoundRank, playerId, "Player", 0, DateTime.MinValue));
            }

            var sorted = entries
                .OrderByDescending(e => e.Score)
                .ToList();

            int rank = sorted.FindIndex(
                e => e.PlayerId == playerId && e.Score == playerEntries[0].Score) + 1;

            LeaderboardEntry best = playerEntries[0];
            return Task.FromResult(new LeaderboardEntry(
                rank,
                best.PlayerId,
                best.PlayerName,
                best.Score,
                best.Timestamp
            ));
        }

        #endregion

        #region Private Methods

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
                var wrapper = JsonUtility.FromJson<LeaderboardSaveData>(json);
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
