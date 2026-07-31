using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public class NullLeaderboardProvider : 
        ScriptableObject, 
        ILeaderboardProvider
    {
        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        private string _providerId = "null";

        #endregion

        #region ILeaderboardProvider

        public string ProviderId => _providerId;
        public bool IsAvailable => true;
        public bool IsInitialized => true;

        public Task<bool> InitializeAsync()
        {
            QuickLog.Info<NullLeaderboardProvider>(
                "Null provider initialized. All operations will be no-ops.");
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
            QuickLog.Debug<NullLeaderboardProvider>(
                "Null ReportScore: id='{0}', score={1}, mode={2}, type={3}",
                leaderboardId, score, mode, type);
            return Task.CompletedTask;
        }

        public Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId,
            int index,
            int size,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            QuickLog.Debug<NullLeaderboardProvider>(
                "Null FetchLeaderboard: id='{0}', index={1}, size={2}, type={3}",
                leaderboardId, index, size, type);
            return Task.FromResult(LeaderboardResult.Empty);
        }

        public Task<LeaderboardResult> FetchLeaderboardAroundPlayerAsync(
            string leaderboardId,
            int radius,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            QuickLog.Debug<NullLeaderboardProvider>(
                "Null FetchLeaderboardAroundPlayer: id='{0}', radius={1}, type={2}",
                leaderboardId, radius, type);
            return Task.FromResult(LeaderboardResult.Empty);
        }

        public Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            QuickLog.Debug<NullLeaderboardProvider>(
                "Null FetchPlayerEntry: id='{0}', type={1}",
                leaderboardId, type);
            return Task.FromResult(default(LeaderboardEntry));
        }

        #endregion
    }
}
