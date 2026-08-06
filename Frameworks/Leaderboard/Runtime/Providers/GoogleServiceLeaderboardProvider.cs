#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

#pragma warning disable CS0618 // IScore is obsolete but required by GPGS API

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;
using UnityEngine.SocialPlatforms;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public class GoogleServiceLeaderboardProvider :
        ScriptableObject, ILeaderboardProvider
    {
        #region Constants

        private const int DefaultPageSize = 25;
        private const int MaxPaginationSteps = 20;
        private const string TimeframePrefix = "__tf_";
        private const string AroundPlayerCacheSuffix = "__around_player";
        private const string PlayerEntryCacheSuffix = "__player_entry";

        #endregion

        #region Interfaces & Properties

        public string ProviderId => providerId;
        public bool IsAvailable { get; private set; }
        public bool IsInitialized => _isInitialized;

        public LeaderboardProviderFeatures Features
        {
            get
            {
#if UNITY_ANDROID
                return
                    LeaderboardProviderFeatures.ReportScore
                    | LeaderboardProviderFeatures.FetchTopScores
                    | LeaderboardProviderFeatures.FetchAroundPlayer
                    | LeaderboardProviderFeatures.FetchPlayerEntry
                    | LeaderboardProviderFeatures.TimeFrameDaily
                    | LeaderboardProviderFeatures.TimeFrameWeekly
                    | LeaderboardProviderFeatures.TimeFrameAllTime;
#else
                return LeaderboardProviderFeatures.None;
#endif
            }
        }

        #endregion

        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        private string providerId = "google_service";

        [SerializeField]
        private List<GooglePlayServiceLeaderBoardIdMapping> idMapping;

        [SerializeField]
        [Tooltip("Timeout in seconds for lightweight GPGS calls (init, report score).")]
        private float timeoutSeconds = 1f;

        [SerializeField]
        [Tooltip("Timeout in seconds for leaderboard fetch operations (per GPGS call).")]
        private float fetchTimeoutSeconds = 10f;

        [SerializeField]
        [Tooltip("Seconds before cached leaderboard data is considered stale.")]
        private float cacheTimeoutSeconds = 300f;

        #endregion

        #region Private Fields

        private bool _isInitialized;
        private Dictionary<string, string> _mappedIds;
        private Dictionary<string, CachedLeaderboardData> _cache;
        private readonly object _cacheLock = new object();

        #endregion

        #region Public Methods

        public async Task<bool> InitializeAsync()
        {
            _isInitialized = false;
            IsAvailable = false;

            BuildMappedDictionary();
            lock (_cacheLock)
            {
                _cache = new Dictionary<string, CachedLeaderboardData>();
            }

            QuickLog.Info<GoogleServiceLeaderboardProvider>(
                "[{0}] Starting initialization...", providerId);

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            try
            {
                return await InitializeInternal();
            }
            catch (Exception ex)
            {
                QuickLog.Error<GoogleServiceLeaderboardProvider>(
                    "[{0}] Initialization failed: {1}",
                    providerId, ex.Message
                );
                return false;
            }
#else
            QuickLog.Info<GoogleServiceLeaderboardProvider>(
                "[{0}] Not available on this platform.",
                providerId
            );
            return false;
#endif
        }

        public async Task ReportScoreAsync(
            string leaderboardId,
            long score,
            string metadata,
            LeaderboardType type,
            LeaderboardScoreSubmissionMode mode,
            CancellationToken ct = default
        )
        {
            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] ReportScore: id='{1}', score={2}, mode={3}",
                providerId, leaderboardId, score, mode);

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            try
            {
                string gpgsId = ResolveMappedId(leaderboardId);

                bool success = await ReportScoreInternal(gpgsId, score, metadata, ct);

                if (success)
                {
                    QuickLog.Info<GoogleServiceLeaderboardProvider>(
                        "[{0}] ReportScore success: id='{1}', score={2}",
                        providerId, leaderboardId, score);
                }
                else
                {
                    QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                        "[{0}] ReportScore returned false: id='{1}', score={2}",
                        providerId, leaderboardId, score);
                }
            }
            catch (TimeoutException tex)
            {
                QuickLog.Error<GoogleServiceLeaderboardProvider>(
                    "[{0}] ReportScore timeout: id='{1}', score={2}, msg={3}",
                    providerId, leaderboardId, score, tex.Message);
                throw;
            }
            catch (Exception ex)
            {
                QuickLog.Error<GoogleServiceLeaderboardProvider>(
                    "[{0}] ReportScore failed: id='{1}', score={2}, error={3}",
                    providerId, leaderboardId, score, ex.Message);
                throw;
            }
            finally
            {
                // Invalidate cache for this leaderboard after score report
                InvalidateCacheInternal(leaderboardId);
            }
#else
            await Task.CompletedTask;
#endif
        }

        public async Task<LeaderboardResult> FetchLeaderboardAsync(
            string leaderboardId,
            int index,
            int size,
            LeaderboardType type,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] FetchLeaderboard: id='{1}', index={2}, size={3}, tf={4}",
                providerId, leaderboardId, index, size, timeframe);

            if (index < 0) index = 0;
            if (size <= 0) size = 1;

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            return await FetchLeaderboardInternal(leaderboardId, index, size, timeframe, ct);
#else
            await Task.CompletedTask;
            return LeaderboardResult.Empty;
#endif
        }

        public async Task<LeaderboardResult> FetchLeaderboardAroundPlayerAsync(
            string leaderboardId,
            int radius,
            LeaderboardType type,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] FetchLeaderboardAroundPlayer: id='{1}', radius={2}, tf={3}",
                providerId, leaderboardId, radius, timeframe);

            if (radius < 0) radius = 0;

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            return await FetchLeaderboardAroundPlayerInternal(leaderboardId, radius, timeframe, ct);
#else
            await Task.CompletedTask;
            return LeaderboardResult.Empty;
#endif
        }

        public async Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            LeaderboardType type,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] FetchPlayerEntry: id='{1}', tf={2}",
                providerId, leaderboardId, timeframe);

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            return await FetchPlayerEntryInternal(leaderboardId, timeframe, ct);
#else
            await Task.CompletedTask;
            return default;
#endif
        }

        /// <summary>
        /// Invalidate cached data for a specific leaderboard, or all leaderboards
        /// if <paramref name="leaderboardId"/> is null.
        /// </summary>
        public void InvalidateCache(string leaderboardId = null)
        {
            InvalidateCacheInternal(leaderboardId);
        }

        #endregion

        #region Private Methods — Cache Management

        private CachedLeaderboardData GetCacheEntry(string cacheKey)
        {
            lock (_cacheLock)
            {
                if (_cache == null) return null;
                _cache.TryGetValue(cacheKey, out CachedLeaderboardData entry);
                return entry;
            }
        }

        private bool IsCacheValid(CachedLeaderboardData entry)
        {
            if (entry == null) return false;
            double ageSeconds = (DateTime.UtcNow - entry.CachedAt).TotalSeconds;
            bool valid = ageSeconds < cacheTimeoutSeconds;
            if (!valid)
            {
                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] Cache expired (age={1:F1}s, ttl={2}s)",
                    providerId, ageSeconds, cacheTimeoutSeconds);
            }
            return valid;
        }

        private CachedLeaderboardData GetOrCreateCacheEntry(string cacheKey)
        {
            lock (_cacheLock)
            {
                if (_cache == null)
                    _cache = new Dictionary<string, CachedLeaderboardData>();

                if (!_cache.TryGetValue(cacheKey, out CachedLeaderboardData entry))
                {
                    entry = new CachedLeaderboardData();
                    _cache[cacheKey] = entry;
                }
                return entry;
            }
        }

        private void UpdateCacheTimestamp(string cacheKey)
        {
            lock (_cacheLock)
            {
                if (_cache != null && _cache.TryGetValue(cacheKey, out CachedLeaderboardData entry))
                {
                    entry.CachedAt = DateTime.UtcNow;
                }
            }
        }

        private void InvalidateCacheInternal(string leaderboardId)
        {
            lock (_cacheLock)
            {
                if (_cache == null) return;

                if (string.IsNullOrEmpty(leaderboardId))
                {
                    QuickLog.Info<GoogleServiceLeaderboardProvider>(
                        "[{0}] Invalidating entire cache ({1} entries)",
                        providerId, _cache.Count);
                    _cache.Clear();
                }
                else
                {
                    // Remove every key whose prefix matches this leaderboard ID
                    // (covers all timeframe variants: {id}, {id}__tf_daily, etc.)
                    List<string> keysToRemove = new List<string>();
                    foreach (string key in _cache.Keys)
                    {
                        if (key == leaderboardId
                            || key.StartsWith(leaderboardId + TimeframePrefix)
                            || key.StartsWith(leaderboardId + AroundPlayerCacheSuffix)
                            || key.StartsWith(leaderboardId + PlayerEntryCacheSuffix))
                        {
                            keysToRemove.Add(key);
                        }
                    }

                    foreach (string key in keysToRemove)
                    {
                        _cache.Remove(key);
                    }

                    if (keysToRemove.Count > 0)
                    {
                        QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                            "[{0}] Cache invalidated {1} keys for id='{2}'",
                            providerId, keysToRemove.Count, leaderboardId);
                    }
                }
            }
        }

        #endregion

        #region Private Methods — GPGS Helpers

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES

        private Task<bool> InitializeInternal()
        {
            if (PlayGamesPlatform.Instance == null)
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] PlayGamesPlatform not found. Not available.",
                    providerId
                );
                return Task.FromResult(false);
            }

            if (!PlayGamesPlatform.Instance.IsAuthenticated())
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] Not authenticated. Provider unavailable until login.",
                    providerId
                );
                return Task.FromResult(false);
            }

            IsAvailable = true;
            _isInitialized = true;

            QuickLog.Info<GoogleServiceLeaderboardProvider>(
                "[{0}] Initialized successfully.",
                providerId
            );

            return Task.FromResult(true);
        }

        private Task<bool> ReportScoreInternal(
            string gpgsId, long score, string metadata, CancellationToken ct)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            ct.Register(() =>
            {
                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] ReportScore cancelled for id='{1}'",
                    providerId, gpgsId);
                tcs.TrySetCanceled(ct);
            });

            try
            {
                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] Calling PlayGamesPlatform.ReportScore: id='{1}', score={2}",
                    providerId, gpgsId, score);

                PlayGamesPlatform.Instance.ReportScore(
                    score, gpgsId, metadata,
                    (success) =>
                    {
                        QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                            "[{0}] ReportScore callback: id='{1}', success={2}",
                            providerId, gpgsId, success);
                        tcs.TrySetResult(success);
                    }
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<GoogleServiceLeaderboardProvider>(
                    "[{0}] ReportScore threw synchronously: id='{1}', error={2}",
                    providerId, gpgsId, ex.Message);
                tcs.TrySetException(ex);
            }

            return WithTimeout(tcs.Task, timeoutSeconds);
        }

        private Task<LeaderboardScoreData> LoadScoresTask(
            string gpgsId,
            LeaderboardStart start,
            int rowCount,
            LeaderboardTimeSpan timeSpan,
            CancellationToken ct)
        {
            TaskCompletionSource<LeaderboardScoreData> tcs =
                new TaskCompletionSource<LeaderboardScoreData>();

            ct.Register(() =>
            {
                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] LoadScores cancelled for id='{1}'",
                    providerId, gpgsId);
                tcs.TrySetCanceled(ct);
            });

            try
            {
                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] LoadScores: id='{1}', start={2}, rowCount={3}",
                    providerId, gpgsId, start, rowCount);

                PlayGamesPlatform.Instance.LoadScores(
                    gpgsId,
                    start,
                    rowCount,
                    LeaderboardCollection.Public,
                    timeSpan,
                    (data) =>
                    {
                        if (data == null)
                        {
                            QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                                "[{0}] LoadScores returned null data for id='{1}'",
                                providerId, gpgsId);
                            tcs.TrySetResult(null);
                            return;
                        }

                        QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                            "[{0}] LoadScores callback: id='{1}', valid={2}, "
                            + "status={3}, approxCount={4}, scores={5}, "
                            + "hasNext={6}, hasPrev={7}",
                            providerId, gpgsId,
                            data.Valid, data.Status, data.ApproximateCount,
                            data.Scores?.Length ?? 0,
                            data.NextPageToken != null,
                            data.PrevPageToken != null);

                        tcs.TrySetResult(data);
                    }
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<GoogleServiceLeaderboardProvider>(
                    "[{0}] LoadScores threw synchronously: id='{1}', error={2}",
                    providerId, gpgsId, ex.Message);
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task<LeaderboardScoreData> LoadMoreScoresTask(
            ScorePageToken pageToken,
            int rowCount,
            CancellationToken ct)
        {
            TaskCompletionSource<LeaderboardScoreData> tcs =
                new TaskCompletionSource<LeaderboardScoreData>();

            ct.Register(() => tcs.TrySetCanceled(ct));

            try
            {
                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] LoadMoreScores: rowCount={1}, leaderboard='{2}'",
                    providerId, rowCount,
                    pageToken?.LeaderboardId ?? "null");

                PlayGamesPlatform.Instance.LoadMoreScores(
                    pageToken,
                    rowCount,
                    (data) =>
                    {
                        if (data == null)
                        {
                            QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                                "[{0}] LoadMoreScores returned null data",
                                providerId);
                            tcs.TrySetResult(null);
                            return;
                        }

                        QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                            "[{0}] LoadMoreScores callback: valid={1}, "
                            + "status={2}, scores={3}, hasNext={4}, hasPrev={5}",
                            providerId,
                            data.Valid, data.Status,
                            data.Scores?.Length ?? 0,
                            data.NextPageToken != null,
                            data.PrevPageToken != null);

                        tcs.TrySetResult(data);
                    }
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<GoogleServiceLeaderboardProvider>(
                    "[{0}] LoadMoreScores threw synchronously: error={1}",
                    providerId, ex.Message);
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private async Task<T> WithTimeout<T>(Task<T> task, float timeoutSecs)
        {
            if (task.IsCompleted) return await task;

            Task delayTask = Task.Delay((int)(timeoutSecs * 1000));
            Task completed = await Task.WhenAny(task, delayTask);

            if (completed == task)
            {
                return await task;
            }

            QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                "[{0}] Operation timed out after {1:F1}s",
                providerId, timeoutSecs);
            throw new TimeoutException(
                $"GPGS operation timed out after {timeoutSecs:F1}s");
        }

        private static LeaderboardEntry ConvertScoreToEntry(IScore score)
        {
            if (score == null) return default;

            return new LeaderboardEntry(
                score.rank,
                score.userID ?? string.Empty,
                score.formattedValue ?? score.value.ToString(),
                score.value,
                score.date
            );
        }

#endif

        #endregion

        #region Private Methods — Fetch Implementations

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES

        private async Task<LeaderboardResult> FetchLeaderboardInternal(
            string leaderboardId, int index, int size,
            LeaderboardTimeframe timeframe, CancellationToken ct)
        {
            string cacheKey = BuildCacheKey(leaderboardId, timeframe);
            LeaderboardTimeSpan timeSpan = ToLeaderboardTimeSpan(timeframe);
            CachedLeaderboardData cacheEntry = GetCacheEntry(cacheKey);

            // Check if we can serve from cache
            if (IsCacheValid(cacheEntry) && cacheEntry.RangeEntries != null)
            {
                int cacheEnd = cacheEntry.RangeStartIndex + cacheEntry.RangeEntries.Count;
                if (index >= cacheEntry.RangeStartIndex && (index + size) <= cacheEnd)
                {
                    QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                        "[{0}] Cache hit for id='{1}', range=[{2},{3})",
                        providerId, leaderboardId, index, index + size);

                    return SliceFromCache(cacheEntry, index, size, leaderboardId);
                }

                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] Cache miss (range): requested=[{1},{2}), "
                    + "cached=[{3},{4})",
                    providerId, index, index + size,
                    cacheEntry.RangeStartIndex, cacheEnd);
            }

            // Fetch from GPGS
            string gpgsId = ResolveMappedId(leaderboardId);

            // Determine how many entries to load initially
            int loadCount = Math.Max(DefaultPageSize, index + size);
            loadCount = Math.Min(loadCount, DefaultPageSize * 4); // cap at 100

            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] Loading from top: gpgsId='{1}', loadCount={2}",
                providerId, gpgsId, loadCount);

            LeaderboardScoreData firstPage = await WithTimeout(
                LoadScoresTask(
                    gpgsId, LeaderboardStart.TopScores, loadCount, timeSpan, ct),
                fetchTimeoutSeconds);

            if (firstPage == null || !firstPage.Valid)
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] LoadScores returned invalid data: id='{1}', "
                    + "status={2}",
                    providerId, leaderboardId,
                    firstPage?.Status.ToString() ?? "null");
                return LeaderboardResult.Empty;
            }

            cacheEntry = GetOrCreateCacheEntry(cacheKey);
            ParseAndCacheTopScores(cacheEntry, firstPage, gpgsId);

            // If range extends beyond first page, paginate forward
            ScorePageToken nextToken = firstPage.NextPageToken;
            int paginationSteps = 0;

            while (nextToken != null
                   && (cacheEntry.RangeStartIndex + cacheEntry.RangeEntries.Count) < (index + size)
                   && paginationSteps < MaxPaginationSteps)
            {
                ct.ThrowIfCancellationRequested();
                paginationSteps++;

                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] Paginating forward (step {1}): id='{2}'",
                    providerId, paginationSteps, leaderboardId);

                LeaderboardScoreData nextPage = await WithTimeout(
                    LoadMoreScoresTask(nextToken, DefaultPageSize, ct),
                    fetchTimeoutSeconds);

                if (nextPage == null || !nextPage.Valid)
                {
                    QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                        "[{0}] LoadMoreScores returned invalid at step {1}: "
                        + "id='{2}'",
                        providerId, paginationSteps, leaderboardId);
                    break;
                }

                AppendTopScoresToCache(cacheEntry, nextPage);
                nextToken = nextPage.NextPageToken;
            }

            if (paginationSteps >= MaxPaginationSteps)
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] Reached max pagination steps ({1}) for id='{2}'",
                    providerId, MaxPaginationSteps, leaderboardId);
            }

            UpdateCacheTimestamp(cacheKey);

            return SliceFromCache(cacheEntry, index, size, leaderboardId);
        }

        private async Task<LeaderboardResult> FetchLeaderboardAroundPlayerInternal(
            string leaderboardId, int radius,
            LeaderboardTimeframe timeframe, CancellationToken ct)
        {
            string cacheKey = BuildCacheKey(leaderboardId, timeframe, AroundPlayerCacheSuffix);
            LeaderboardTimeSpan timeSpan = ToLeaderboardTimeSpan(timeframe);
            CachedLeaderboardData cacheEntry = GetCacheEntry(cacheKey);

            if (IsCacheValid(cacheEntry) && cacheEntry.AroundPlayerResult != null)
            {
                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] Cache hit for around-player: id='{1}'",
                    providerId, leaderboardId);
                return cacheEntry.AroundPlayerResult.Value;
            }

            string gpgsId = ResolveMappedId(leaderboardId);
            int rowCount = (radius * 2) + 1;

            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] Loading around player: gpgsId='{1}', radius={2}, rowCount={3}",
                providerId, gpgsId, radius, rowCount);

            LeaderboardScoreData data = await WithTimeout(
                LoadScoresTask(
                    gpgsId, LeaderboardStart.PlayerCentered, rowCount, timeSpan, ct),
                fetchTimeoutSeconds);

            if (data == null || !data.Valid)
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] LoadScores (PlayerCentered) invalid: id='{1}', status={2}",
                    providerId, leaderboardId,
                    data?.Status.ToString() ?? "null");
                return LeaderboardResult.Empty;
            }

            // Build entries from Scores array
            IScore[] scores = data.Scores ?? Array.Empty<IScore>();
            LeaderboardEntry[] entries = new LeaderboardEntry[scores.Length];

            for (int i = 0; i < scores.Length; i++)
            {
                entries[i] = ConvertScoreToEntry(scores[i]);
            }

            // Find player in the returned entries
            int playerEntryIndex = -1;
            string playerId = PlayGamesPlatform.Instance.GetUserId();

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].PlayerId == playerId)
                {
                    playerEntryIndex = i;
                    break;
                }
            }

            // Get player rank from PlayerScore if not found in entries
            int? playerRank = null;
            if (playerEntryIndex >= 0)
            {
                playerRank = entries[playerEntryIndex].Rank;
            }
            else if (data.PlayerScore != null)
            {
                playerRank = data.PlayerScore.rank;
            }

            LeaderboardResult result = new LeaderboardResult(
                entries,
                (int)data.ApproximateCount,
                playerEntryIndex,
                playerRank);

            // Cache the result
            cacheEntry = GetOrCreateCacheEntry(cacheKey);
            cacheEntry.AroundPlayerResult = result;
            UpdateCacheTimestamp(cacheKey);

            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] Around-player fetch complete: id='{1}', entries={2}, "
                + "playerIndex={3}, playerRank={4}",
                providerId, leaderboardId, entries.Length,
                playerEntryIndex, playerRank);

            return result;
        }

        private async Task<LeaderboardEntry> FetchPlayerEntryInternal(
            string leaderboardId,
            LeaderboardTimeframe timeframe,
            CancellationToken ct)
        {
            string cacheKey = BuildCacheKey(leaderboardId, timeframe, PlayerEntryCacheSuffix);
            LeaderboardTimeSpan timeSpan = ToLeaderboardTimeSpan(timeframe);
            CachedLeaderboardData cacheEntry = GetCacheEntry(cacheKey);

            if (IsCacheValid(cacheEntry) && cacheEntry.PlayerEntry.HasValue)
            {
                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] Cache hit for player entry: id='{1}'",
                    providerId, leaderboardId);
                return cacheEntry.PlayerEntry.Value;
            }

            string gpgsId = ResolveMappedId(leaderboardId);

            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] Loading player entry: gpgsId='{1}'",
                providerId, gpgsId);

            // Load 1 score centered on player — PlayerScore gives us what we need
            LeaderboardScoreData data = await WithTimeout(
                LoadScoresTask(
                    gpgsId, LeaderboardStart.PlayerCentered, 1, timeSpan, ct),
                fetchTimeoutSeconds);

            if (data == null || !data.Valid)
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] LoadScores (PlayerCentered) invalid for player entry: "
                    + "id='{1}', status={2}",
                    providerId, leaderboardId,
                    data?.Status.ToString() ?? "null");
                return default;
            }

            IScore playerScore = data.PlayerScore;
            LeaderboardEntry entry;

            if (playerScore != null)
            {
                entry = ConvertScoreToEntry(playerScore);

                QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                    "[{0}] Player entry fetched: id='{1}', rank={2}, score={3}",
                    providerId, leaderboardId, entry.Rank, entry.Score);
            }
            else
            {
                // Check if player exists in Scores array as fallback
                IScore[] scores = data.Scores ?? Array.Empty<IScore>();
                string playerId = PlayGamesPlatform.Instance.GetUserId();

                IScore found = null;
                for (int i = 0; i < scores.Length; i++)
                {
                    if (scores[i].userID != playerId) continue;
                    found = scores[i];
                    break;
                }

                if (found != null)
                {
                    entry = ConvertScoreToEntry(found);
                }
                else
                {
                    QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                        "[{0}] No player score found: id='{1}'",
                        providerId, leaderboardId
                    );
                    entry = default;
                }
            }

            // Cache the entry
            cacheEntry = GetOrCreateCacheEntry(cacheKey);
            cacheEntry.PlayerEntry = entry;
            UpdateCacheTimestamp(cacheKey);

            return entry;
        }

#endif

        #endregion

        #region Private Methods — Cache Parsing & Slicing

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES

        private void ParseAndCacheTopScores(
            CachedLeaderboardData cacheEntry,
            LeaderboardScoreData data,
            string gpgsId)
        {
            IScore[] scores = data.Scores ?? Array.Empty<IScore>();

            cacheEntry.RangeEntries = new List<LeaderboardEntry>(scores.Length);
            cacheEntry.RangeStartIndex = 0;
            cacheEntry.NextPageToken = data.NextPageToken;
            cacheEntry.ApproximateTotalCount = (int)data.ApproximateCount;

            for (int i = 0; i < scores.Length; i++)
            {
                cacheEntry.RangeEntries.Add(ConvertScoreToEntry(scores[i]));
            }

            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] Cached top scores: gpgsId='{1}', entries={2}, "
                + "approxTotal={3}, hasNext={4}",
                providerId, gpgsId, cacheEntry.RangeEntries.Count,
                cacheEntry.ApproximateTotalCount,
                data.NextPageToken != null);
        }

        private void AppendTopScoresToCache(
            CachedLeaderboardData cacheEntry,
            LeaderboardScoreData data)
        {
            IScore[] scores = data.Scores ?? Array.Empty<IScore>();

            for (int i = 0; i < scores.Length; i++)
            {
                cacheEntry.RangeEntries.Add(ConvertScoreToEntry(scores[i]));
            }

            cacheEntry.NextPageToken = data.NextPageToken;

            // Update approximate count if larger
            int approxCount = (int)data.ApproximateCount;
            if (approxCount > cacheEntry.ApproximateTotalCount)
            {
                cacheEntry.ApproximateTotalCount = approxCount;
            }
        }

        private LeaderboardResult SliceFromCache(
            CachedLeaderboardData cacheEntry, int index, int size,
            string leaderboardId)
        {
            int cacheStart = cacheEntry.RangeStartIndex;
            List<LeaderboardEntry> allEntries = cacheEntry.RangeEntries;
            int totalEntries = allEntries.Count;

            int localStart = Math.Max(0, index - cacheStart);
            int localEnd = Math.Min(totalEntries, localStart + size);
            localStart = Math.Min(localStart, totalEntries);

            int resultCount = localEnd - localStart;
            LeaderboardEntry[] sliced = new LeaderboardEntry[resultCount];

            int playerEntryIndex = -1;
            string playerId = PlayGamesPlatform.Instance.GetUserId();

            for (int i = 0; i < resultCount; i++)
            {
                sliced[i] = allEntries[localStart + i];

                if (playerEntryIndex < 0 && sliced[i].PlayerId == playerId)
                {
                    playerEntryIndex = i;
                }
            }

            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] Sliced from cache: id='{1}', range=[{2},{3}), "
                + "entries={4}, playerIndex={5}",
                providerId, leaderboardId,
                cacheStart + localStart, cacheStart + localEnd,
                resultCount, playerEntryIndex
            );

            return new LeaderboardResult(
                sliced,
                cacheEntry.ApproximateTotalCount,
                playerEntryIndex,
                null);
        }

#endif

        #endregion

        #region Private Methods — Mapping

        private void BuildMappedDictionary()
        {
            _mappedIds = new Dictionary<string, string>();
            if (idMapping == null)
            {
                QuickLog.Warning<GoogleServiceLeaderboardProvider>(
                    "[{0}] idMapping is null. Leaderboard ID resolution will passthrough.",
                    providerId);
                return;
            }

            foreach (GooglePlayServiceLeaderBoardIdMapping mapping in idMapping)
            {
                if (mapping == null) continue;
                _mappedIds[mapping.leaderboardId] = mapping.playServiceLeaderboardId;
            }

            QuickLog.Debug<GoogleServiceLeaderboardProvider>(
                "[{0}] Built ID mapping with {1} entries",
                providerId, _mappedIds.Count);
        }

        private string ResolveMappedId(string id)
        {
            if (_mappedIds != null
                && _mappedIds.TryGetValue(id, out string value))
            {
                return value;
            }
            return id;
        }

        #endregion

        #region Private Methods — Timeframe Helpers

        private static string BuildCacheKey(
            string leaderboardId,
            LeaderboardTimeframe timeframe,
            string suffix = null)
        {
            string key = leaderboardId + TimeframePrefix + timeframe.ToString().ToLowerInvariant();
            if (!string.IsNullOrEmpty(suffix))
            {
                key += suffix;
            }
            return key;
        }

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
        private static LeaderboardTimeSpan ToLeaderboardTimeSpan(
            LeaderboardTimeframe timeframe)
        {
            switch (timeframe)
            {
                case LeaderboardTimeframe.Daily:
                    return LeaderboardTimeSpan.Daily;
                case LeaderboardTimeframe.Weekly:
                    return LeaderboardTimeSpan.Weekly;
                default:
                    return LeaderboardTimeSpan.AllTime;
            }
        }
#endif

        #endregion

        #region Nested Types

        /// <summary>
        /// Holds cached leaderboard data with timestamp for TTL-based expiration.
        /// Different fields are populated by different fetch strategies.
        /// </summary>
        private sealed class CachedLeaderboardData
        {
            /// <summary>Entries loaded from top (range-based fetches).</summary>
            public List<LeaderboardEntry> RangeEntries;

            /// <summary>0-based index of the first entry in RangeEntries.</summary>
            public int RangeStartIndex;

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            /// <summary>Token for loading the next page of top scores.</summary>
            public ScorePageToken NextPageToken;
#endif

            /// <summary>Approximate total entries in the leaderboard (from GPGS).</summary>
            public int ApproximateTotalCount;

            /// <summary>Cached around-player result (from PlayerCentered fetches).</summary>
            public LeaderboardResult? AroundPlayerResult;

            /// <summary>Cached single player entry.</summary>
            public LeaderboardEntry? PlayerEntry;

            /// <summary>UTC time when this cache entry was last updated.</summary>
            public DateTime CachedAt;
        }

        #endregion
    }

    [Serializable]
    internal class GooglePlayServiceLeaderBoardIdMapping
    {
        public string leaderboardId;
        public string playServiceLeaderboardId;
    }
}
