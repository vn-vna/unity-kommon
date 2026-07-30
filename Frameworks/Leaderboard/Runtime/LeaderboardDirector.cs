using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;
using UnityEngine.Events;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    [AddComponentMenu("Scheherazade/Leaderboard Director")]
    [DontDestroyOnLoad]
    public class LeaderboardDirector : SingletonBehavior<LeaderboardDirector>
    {
        #region Events & Delegates

        public event Action<string, long, string, ScoreSubmissionMode> ScoreReported;
        public event Action<string, LeaderboardResult> LeaderboardFetched;
        public event Action<string, LeaderboardEntry> PlayerEntryFetched;
        public event Action<string, string> ErrorOccurred;

        #endregion

        #region Properties

        public LeaderboardManagerStatus Status { get; private set; } = LeaderboardManagerStatus.Uninitialized;

        #endregion

        #region Private Fields

        private LeaderboardConfiguration _config;
        private ILeaderboardProvider _activeProvider;

        #endregion

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            LeaderboardConfiguration config = LeaderboardConfiguration.Instance;

            if (config == null)
            {
                QuickLog.Info<LeaderboardDirector>(
                    "Leaderboard disabled — no configuration found."
                );
                return;
            }

            if (!config.HasAnyProvider)
            {
                QuickLog.Info<LeaderboardDirector>(
                    "Leaderboard disabled — no provider configured."
                );
                return;
            }

            var go = new GameObject("[Scheherazade Leaderboard Director]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<LeaderboardDirector>();
        }

        #endregion

        #region Unity Callbacks

        protected override void Awake()
        {
            base.Awake();

            try
            {
                _config = LeaderboardConfiguration.Instance;

                if (_config != null)
                {
                    _activeProvider = _config.Provider;

                    if (_activeProvider == null)
                    {
                        QuickLog.Info<LeaderboardDirector>(
                            "Leaderboard director created but no provider is configured. "
                            + "Call Initialize() after assigning a provider."
                        );
                    }
                }

                Status = LeaderboardManagerStatus.Uninitialized;

                QuickLog.Info<LeaderboardDirector>(
                    "Leaderboard director ready. Call Initialize() to start."
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "Awake failed: {0}", ex.Message
                );
                Status = LeaderboardManagerStatus.Uninitialized;
            }
        }

        #endregion

        #region Initialization

        public void Initialize(float timeOut = float.MaxValue)
        {
            Dispatcher.DispatchCoroutine(InitializeCoroutine(timeOut));
        }

        public IEnumerator InitializeCoroutine(float timeOut = float.MaxValue)
        {
            if (_activeProvider == null)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "No provider available. Cannot initialize."
                );
                Status = LeaderboardManagerStatus.Uninitialized;
                yield break;
            }

            Status = LeaderboardManagerStatus.Initializing;

            QuickLog.Info<LeaderboardDirector>(
                "Initializing leaderboard with provider '{0}'.",
                _activeProvider.ProviderId
            );

            Task<bool> initTask = _activeProvider.InitializeAsync();
            float timer = 0.0f;

            while (!initTask.IsCompleted && timer < timeOut)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            if (_activeProvider.IsInitialized)
            {
                Status = LeaderboardManagerStatus.Ready;
                QuickLog.Info<LeaderboardDirector>(
                    "Leaderboard initialized successfully with provider '{0}'.",
                    _activeProvider.ProviderId
                );
            }
            else if (timer >= timeOut)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "Leaderboard initialization timed out after {0:F1}s.",
                    timeOut
                );
                Status = LeaderboardManagerStatus.Uninitialized;
            }
            else
            {
                QuickLog.Error<LeaderboardDirector>(
                    "Leaderboard initialization failed for provider '{0}'.",
                    _activeProvider.ProviderId
                );
                Status = LeaderboardManagerStatus.Uninitialized;
            }
        }

        public async Task<bool> InitializeAsync(float timeOut = float.MaxValue)
        {
            if (_activeProvider == null)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "No provider available. Cannot initialize."
                );
                Status = LeaderboardManagerStatus.Uninitialized;
                return false;
            }

            Status = LeaderboardManagerStatus.Initializing;

            QuickLog.Info<LeaderboardDirector>(
                "Initializing leaderboard with provider '{0}'.",
                _activeProvider.ProviderId
            );

            try
            {
                Task<bool> initTask = _activeProvider.InitializeAsync();

                using (var cts = new CancellationTokenSource())
                {
                    Task timeoutTask = Task.Delay(
                        TimeSpan.FromSeconds(timeOut), cts.Token);

                    Task completed = await Task.WhenAny(initTask, timeoutTask);

                    if (completed == initTask)
                    {
                        cts.Cancel();

                        if (_activeProvider.IsInitialized)
                        {
                            Status = LeaderboardManagerStatus.Ready;
                            QuickLog.Info<LeaderboardDirector>(
                                "Leaderboard initialized successfully with provider '{0}'.",
                                _activeProvider.ProviderId
                            );
                            return true;
                        }

                        QuickLog.Error<LeaderboardDirector>(
                            "Leaderboard initialization failed for provider '{0}'.",
                            _activeProvider.ProviderId
                        );
                        Status = LeaderboardManagerStatus.Uninitialized;
                        return false;
                    }

                    QuickLog.Error<LeaderboardDirector>(
                        "Leaderboard initialization timed out after {0:F1}s.",
                        timeOut
                    );
                    Status = LeaderboardManagerStatus.Uninitialized;
                    return false;
                }
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "Leaderboard initialization threw an exception: {0}",
                    ex.Message
                );
                Status = LeaderboardManagerStatus.Uninitialized;
                return false;
            }
        }

        public void Shutdown()
        {
            QuickLog.Info<LeaderboardDirector>("Shutting down leaderboard.");

            Status = LeaderboardManagerStatus.Uninitialized;
            _activeProvider = null;
        }

        #endregion

        #region Public Methods

        public async Task ReportScoreAsync(
            string leaderboardId,
            long score,
            string metadata = null,
            ScoreSubmissionMode mode = ScoreSubmissionMode.Best,
            CancellationToken ct = default)
        {
            EnsureReady();

            if (_activeProvider == null)
            {
                FireError("ReportScore",
                    "No leaderboard provider available");
                return;
            }

            LeaderboardType type = ResolveLeaderboardType(leaderboardId);

            try
            {
                await _activeProvider.ReportScoreAsync(
                    leaderboardId, score, metadata, type, mode, ct);

                QuickLog.Info<LeaderboardDirector>(
                    "Score reported: id='{0}', score={1}, mode={2}, type={3}",
                    leaderboardId, score, mode, type);

                ScoreReported?.Invoke(leaderboardId, score, metadata, mode);
            }
            catch (LeaderboardException)
            {
                throw;
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "ReportScore failed: id='{0}', score={1}, error={2}",
                    leaderboardId, score, ex.Message);

                var wrapped = new ScoreSubmissionException(
                    leaderboardId, score,
                    $"Score submission failed via '{_activeProvider.ProviderId}'",
                    ex);

                ErrorOccurred?.Invoke("ReportScore", ex.Message);
                throw wrapped;
            }
        }

        public async Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId,
            int index,
            int size,
            CancellationToken ct = default)
        {
            EnsureReady();

            if (_activeProvider == null)
            {
                FireError("FetchLeaderboard",
                    "No leaderboard provider available");
                return LeaderboardResult.Empty;
            }

            LeaderboardType type = ResolveLeaderboardType(leaderboardId);

            try
            {
                LeaderboardResult result =
                    await _activeProvider.FetchLeaderboardAsync(
                        leaderboardId, index, size, type, ct);

                QuickLog.Info<LeaderboardDirector>(
                    "Fetched leaderboard: id='{0}', index={1}, size={2}, "
                    + "entries={3}, total={4}",
                    leaderboardId, index, size,
                    result.Entries.Length, result.TotalPlayers);

                LeaderboardFetched?.Invoke(leaderboardId, result);
                return result;
            }
            catch (LeaderboardException)
            {
                throw;
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "FetchLeaderboard failed: id='{0}', index={1}, size={2}, "
                    + "error={3}",
                    leaderboardId, index, size, ex.Message);

                ErrorOccurred?.Invoke("FetchLeaderboard", ex.Message);
                throw;
            }
        }

        public async Task<LeaderboardResult> FetchLeaderboardAroundPlayerAsync(
            string leaderboardId,
            int radius,
            CancellationToken ct = default)
        {
            EnsureReady();

            if (_activeProvider == null)
            {
                FireError("FetchLeaderboardAroundPlayer",
                    "No leaderboard provider available");
                return LeaderboardResult.Empty;
            }

            LeaderboardType type = ResolveLeaderboardType(leaderboardId);
            int fetchSize = (radius * 2) + 1;

            try
            {
                LeaderboardEntry playerEntry =
                    await _activeProvider.FetchPlayerEntryAsync(
                        leaderboardId, type, ct);

                int playerRank = playerEntry.Rank;

                if (playerRank <= 0)
                {
                    QuickLog.Debug<LeaderboardDirector>(
                        "AroundPlayer: player has no rank for id='{0}'. "
                        + "Falling back to top {1}.",
                        leaderboardId, fetchSize
                    );

                    LeaderboardResult topResult = await _activeProvider
                        .FetchLeaderboardAsync(leaderboardId, 0, fetchSize, type, ct);

                    LeaderboardFetched?.Invoke(leaderboardId, topResult);
                    return topResult;
                }

                int index = Math.Max(0, playerRank - radius - 1);

                QuickLog.Debug<LeaderboardDirector>(
                    "AroundPlayer: playerRank={0}, fetching index={1}, "
                    + "size={2} for id='{3}'",
                    playerRank, index, fetchSize, leaderboardId);

                LeaderboardResult rangeResult =
                    await _activeProvider.FetchLeaderboardAsync(
                        leaderboardId, index, fetchSize, type, ct);

                int localIndex = playerRank - index - 1;
                LeaderboardResult patched = new LeaderboardResult(
                    rangeResult.Entries,
                    rangeResult.TotalPlayers,
                    localIndex,
                    playerRank);

                QuickLog.Info<LeaderboardDirector>(
                    "Fetched around player: id='{0}', radius={1}, "
                    + "entries={2}, playerIndex={3}",
                    leaderboardId, radius,
                    patched.Entries.Length, patched.PlayerEntryIndex);

                LeaderboardFetched?.Invoke(leaderboardId, patched);
                return patched;
            }
            catch (LeaderboardException)
            {
                throw;
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "FetchLeaderboardAroundPlayer failed: id='{0}', "
                    + "radius={1}, error={2}",
                    leaderboardId, radius, ex.Message);

                ErrorOccurred?.Invoke("FetchLeaderboardAroundPlayer", ex.Message);
                throw;
            }
        }

        public async Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            CancellationToken ct = default)
        {
            EnsureReady();

            if (_activeProvider == null)
            {
                FireError("FetchPlayerEntry",
                    "No leaderboard provider available");
                return default;
            }

            LeaderboardType type = ResolveLeaderboardType(leaderboardId);

            try
            {
                LeaderboardEntry entry =
                    await _activeProvider.FetchPlayerEntryAsync(
                        leaderboardId, type, ct);

                QuickLog.Info<LeaderboardDirector>(
                    "Fetched player entry: id='{0}', rank={1}, score={2}",
                    leaderboardId, entry.Rank, entry.Score);

                PlayerEntryFetched?.Invoke(leaderboardId, entry);
                return entry;
            }
            catch (LeaderboardException)
            {
                throw;
            }
            catch (Exception ex)
            {
                QuickLog.Error<LeaderboardDirector>(
                    "FetchPlayerEntry failed: id='{0}', error={1}",
                    leaderboardId, ex.Message);

                ErrorOccurred?.Invoke("FetchPlayerEntry", ex.Message);
                throw;
            }
        }

        #endregion

        #region Private Methods

        private void EnsureReady()
        {
            if (Status != LeaderboardManagerStatus.Ready)
            {
                throw new LeaderboardNotInitializedException();
            }
        }

        private void FireError(string operation, string message)
        {
            ErrorOccurred?.Invoke(operation, message);
        }

        private LeaderboardType ResolveLeaderboardType(string leaderboardId)
        {
            if (_config != null)
            {
                LeaderboardDefinition def = _config.Leaderboards
                    .FirstOrDefault(d => d != null && d.Id == leaderboardId);

                if (def != null)
                {
                    return def.Type;
                }

                QuickLog.Warning<LeaderboardDirector>(
                    "No definition found for leaderboard '{0}'. "
                    + "Defaulting to Point type.",
                    leaderboardId);
            }

            return LeaderboardType.Point;
        }

        #endregion

        #region Nested Types

        public enum LeaderboardManagerStatus
        {
            Uninitialized,
            Initializing,
            Ready
        }

        #endregion
    }
}
