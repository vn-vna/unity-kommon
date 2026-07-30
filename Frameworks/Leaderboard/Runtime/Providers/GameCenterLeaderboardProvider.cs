#if UNITY_IOS || UNITY_TVOS
#define SCHEHERAZADE_GAMECENTER_ENABLED
#endif

using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
#if SCHEHERAZADE_GAMECENTER_ENABLED
    public class GameCenterLeaderboardProvider :
        ScriptableObject, ILeaderboardProvider
    {
        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        private string _providerId = "gamecenter";

        #endregion

        #region Private Fields

        private bool _isInitialized;

        #endregion

        #region ILeaderboardProvider

        public string ProviderId => _providerId;
        public bool IsAvailable { get; private set; }
        public bool IsInitialized => _isInitialized;

        public Task<bool> InitializeAsync()
        {
            _isInitialized = false;

            QuickLog.Info<GameCenterLeaderboardProvider>(
                "Game Center provider is enabled on this platform. "
                + "Full implementation pending.");

            IsAvailable = true;
            _isInitialized = true;
            return Task.FromResult(true);
        }

        public Task ReportScoreAsync(
            string leaderboardId,
            long score,
            string metadata,
            LeaderboardType type,
            ScoreSubmissionMode mode,
            CancellationToken ct = default)
        {
            QuickLog.Warning<GameCenterLeaderboardProvider>(
                "Game Center ReportScore is a stub. "
                + "id='{0}', score={1}",
                leaderboardId, score);
            throw new NotImplementedException(
                "Game Center leaderboard integration is not yet implemented.");
        }

        public Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId,
            int index,
            int size,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            QuickLog.Warning<GameCenterLeaderboardProvider>(
                "Game Center FetchLeaderboard is a stub. "
                + "id='{0}', index={1}, size={2}",
                leaderboardId, index, size);
            throw new NotImplementedException(
                "Game Center leaderboard integration is not yet implemented.");
        }

        public Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            QuickLog.Warning<GameCenterLeaderboardProvider>(
                "Game Center FetchPlayerEntry is a stub. id='{0}'",
                leaderboardId);
            throw new NotImplementedException(
                "Game Center leaderboard integration is not yet implemented.");
        }

        #endregion
    }
#else
    internal class GameCenterLeaderboardProvider_Disabled
    {
        // Game Center is not available on this platform.
        // The SCHEHERAZADE_GAMECENTER_ENABLED define is not set.
    }
#endif
}
