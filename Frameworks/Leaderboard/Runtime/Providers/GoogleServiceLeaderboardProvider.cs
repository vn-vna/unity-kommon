#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif
using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public class GoogleServiceLeaderboardProvider :
        ScriptableObject, ILeaderboardProvider
    {

        #region Interfaces & Properties

        public string ProviderId => providerId;
        public bool IsAvailable { get; private set; }
        public bool IsInitialized => _isInitialized;
        #endregion

        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        private string providerId = "google_service";

        [SerializeField]
        private List<GooglePlayServiceLeaderBoardIdMapping> idMapping;

        #endregion

        #region Private Fields

        private bool _isInitialized;
        private Dictionary<string, string> _mappedIds;

        #endregion

        #region Public Methods

        public Task<bool> InitializeAsync()
        {
            _isInitialized = false;
            IsAvailable = false;

            BuildMappedDictionary();

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            try
            {
                return Task.FromResult(InitializeInternal());
            }
            catch (Exception ex)
            {
                QuickLog.Error<GoogleServiceLeaderboardProvider>(
                    "[{0}] Initialization failed: {1}",
                    providerId, ex.Message
                );
                return Task.FromResult(false);
            }
#else
            QuickLog.Info<GoogleServiceLeaderboardProvider>(
                "[{0}] Not available on this platform.",
                _providerId
            );
            return Task.FromResult(false);
#endif
        }

        public Task ReportScoreAsync(
            string leaderboardId,
            long score,
            string metadata,
            LeaderboardType type,
            ScoreSubmissionMode mode,
            CancellationToken ct = default)
        {
            QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                "[{0}] ReportScore is a stub. id='{1}', score={2}",
                providerId, leaderboardId, score
            );

            throw new NotImplementedException(
                "Google Service leaderboard integration is not yet implemented.");
        }

        public Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId,
            int index,
            int size,
            LeaderboardType type,
            CancellationToken ct = default)
        {
            QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                "[{0}] FetchLeaderboard is a stub. id='{1}', index={2}, size={3}",
                providerId, leaderboardId, index, size
            );

            throw new NotImplementedException(
                "Google Service leaderboard integration is not yet implemented.");
        }

        public Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            LeaderboardType type,
            CancellationToken ct = default
        )
        {
            QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                "[{0}] FetchPlayerEntry is a stub. id='{1}'",
                providerId, leaderboardId
            );

            throw new NotImplementedException(
                "Google Service leaderboard integration is not yet implemented.");
        }

        #endregion

        #region Private Methods
        private void BuildMappedDictionary()
        {
            _mappedIds = new Dictionary<string, string>();
            foreach (GooglePlayServiceLeaderBoardIdMapping mapping in idMapping)
            {
                _mappedIds[mapping.leaderboardId] = mapping.playServiceLeaderboardId;
            }
        }

        private string ResolveMappedId(string id)
        {
            return _mappedIds?.TryGetValue(id, out string value) == true 
                ? value 
                : id;
        }

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
        private bool InitializeInternal()
        {
            if (PlayGamesPlatform.Instance == null)
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] PlayGamesPlatform not found. Not available.",
                    providerId
                );
                return false;
            }

            if (!PlayGamesPlatform.Instance.IsAuthenticated())
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] Not authenticated. Provider unavailable until login.",
                    providerId
                );
                return false;
            }

            IsAvailable = true;
            _isInitialized = true;

            QuickLog.Info<GoogleServiceLeaderboardProvider>(
                "[{0}] Initialized successfully.",
                providerId
            );

            return true;
        }
#endif

        #endregion
    }

    [Serializable]
    internal class GooglePlayServiceLeaderBoardIdMapping
    {
        public string leaderboardId;
        public string playServiceLeaderboardId;
    }
}
