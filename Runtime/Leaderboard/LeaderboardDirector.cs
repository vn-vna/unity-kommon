using System;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;
using UnityEngine.Events;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    [AddComponentMenu("Scheherazade/Leaderboard Director")]
    [DontDestroyOnLoad]
    public class LeaderboardDirector : SingletonBehavior<LeaderboardDirector>
    {
        #region Constants

        private const string ConfigPath =
            "Integration/Managers/LeaderboardConfiguration";

        #endregion

        #region Events & Delegates

        public event Action<string, long> ScoreReported;
        public event Action<string, LeaderboardEntry[]> LeaderboardFetched;
        public event Action<string, LeaderboardEntry> PlayerEntryFetched;
        public event Action<string, string> Error;

        #endregion

        #region Inspector Events

        [SerializeField]
        private UnityEvent<string, long> _onScoreReported = new UnityEvent<string, long>();

        [SerializeField]
        private UnityEvent<string> _onLeaderboardFetched = new UnityEvent<string>();

        [SerializeField]
        private UnityEvent<string> _onPlayerEntryFetched = new UnityEvent<string>();

        [SerializeField]
        private UnityEvent<string, string> _onError = new UnityEvent<string, string>();

        #endregion

        #region Static Init

        private static readonly TaskCompletionSource<bool> _readySource =
            new TaskCompletionSource<bool>();

        public static Task ReadyTask => _readySource.Task;

        #endregion

        #region Private Fields

        private LeaderboardConfiguration _config;
        private ILeaderboardProvider _activeProvider;

        #endregion

        #region Unity Callbacks

        protected override async void Awake()
        {
            base.Awake();

            try
            {
                _config = Resources.Load<LeaderboardConfiguration>(ConfigPath);

                if (_config != null)
                {
                    _activeProvider = _config.Provider;
                    if (_activeProvider != null)
                    {
                        await _activeProvider.InitializeAsync();
                    }
                }

                if (_activeProvider == null)
                {
                    _activeProvider = ScriptableObject.CreateInstance<LocalLeaderboardProvider>();
                    await _activeProvider.InitializeAsync();
                }
            }
            finally
            {
                _readySource.TrySetResult(true);
            }
        }

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("[Scheherazade Leaderboard Director]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<LeaderboardDirector>();
        }

        #endregion

        #region Public Methods

        public async Task ReportScoreAsync(
            string leaderboardId,
            long score,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();

            if (_activeProvider == null)
            {
                FireError("ReportScore", "No leaderboard provider available");
                return;
            }

            try
            {
                await _activeProvider.ReportScoreAsync(leaderboardId, score, ct);
                ScoreReported?.Invoke(leaderboardId, score);
                _onScoreReported?.Invoke(leaderboardId, score);
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "ReportScore failed for '{0}': {1}",
                    leaderboardId, ex.Message);
                Error?.Invoke("ReportScore", ex.Message);
                _onError?.Invoke("ReportScore", ex.Message);
                throw;
            }
        }

        public async Task<LeaderboardEntry[]> FetchLeaderboardAsync(
            string leaderboardId,
            int count,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();

            if (_activeProvider == null)
            {
                FireError("FetchLeaderboard", "No leaderboard provider available");
                return Array.Empty<LeaderboardEntry>();
            }

            try
            {
                var entries = await _activeProvider.FetchLeaderboardAsync(
                    leaderboardId, count, ct);
                LeaderboardFetched?.Invoke(leaderboardId, entries);
                _onLeaderboardFetched?.Invoke(leaderboardId);
                return entries;
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "FetchLeaderboard failed for '{0}': {1}",
                    leaderboardId, ex.Message);
                Error?.Invoke("FetchLeaderboard", ex.Message);
                _onError?.Invoke("FetchLeaderboard", ex.Message);
                throw;
            }
        }

        public async Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            CancellationToken ct = default)
        {
            await EnsureReadyAsync();

            if (_activeProvider == null)
            {
                FireError("FetchPlayerEntry", "No leaderboard provider available");
                return default;
            }

            try
            {
                var entry = await _activeProvider.FetchPlayerEntryAsync(
                    leaderboardId, ct);
                PlayerEntryFetched?.Invoke(leaderboardId, entry);
                _onPlayerEntryFetched?.Invoke(leaderboardId);
                return entry;
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "FetchPlayerEntry failed for '{0}': {1}",
                    leaderboardId, ex.Message);
                Error?.Invoke("FetchPlayerEntry", ex.Message);
                _onError?.Invoke("FetchPlayerEntry", ex.Message);
                throw;
            }
        }

        #endregion

        #region Private Methods

        private async Task EnsureReadyAsync()
        {
            await ReadyTask;
        }

        private void FireError(string operation, string message)
        {
            Error?.Invoke(operation, message);
            _onError?.Invoke(operation, message);
        }

        #endregion
    }
}
