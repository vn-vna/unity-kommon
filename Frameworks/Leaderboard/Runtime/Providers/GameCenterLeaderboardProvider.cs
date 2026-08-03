#if UNITY_IOS && APPLE_GAMEKIT
using Apple.Core;
using Apple.Core.Runtime;
using Apple.GameKit;
using Apple.GameKit.Leaderboards;
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    /// <summary>
    /// Apple Game Center provider.
    /// The class is always compiled on every platform; the
    /// <c>UNITY_IOS &amp;&amp; APPLE_GAMEKIT</c> guards only change
    /// <see cref="Features"/> and method behavior:
    /// <list type="bullet">
    /// <item>with the define — full Apple GameKit integration.</item>
    /// <item>without it — <c>Features = None</c>, initialization returns
    /// <c>false</c>, and every operation throws <see cref="LeaderboardException"/>.</item>
    /// </list>
    /// </summary>
    public class GameCenterLeaderboardProvider :
        ScriptableObject, ILeaderboardProvider
    {
        #region Constants

        private const int MaxRangeSize = 100;
        private const int AroundPlayerMaxRadius = 49;
        private const string TimeframePrefix = "__tf_";
        private const string AroundPlayerCacheSuffix = "__around_player";
        private const string PlayerEntryCacheSuffix = "__player_entry";

        #endregion

        #region Interfaces & Properties

        public string ProviderId => _providerId;
        public bool IsAvailable { get; private set; }
        public bool IsInitialized => _isInitialized;

        public LeaderboardProviderFeatures Features =>
#if UNITY_IOS && APPLE_GAMEKIT
            LeaderboardProviderFeatures.ReportScore
            | LeaderboardProviderFeatures.FetchTopScores
            | LeaderboardProviderFeatures.FetchAroundPlayer
            | LeaderboardProviderFeatures.FetchPlayerEntry
            | LeaderboardProviderFeatures.TimeFrameDaily
            | LeaderboardProviderFeatures.TimeFrameWeekly
            | LeaderboardProviderFeatures.TimeFrameAllTime;
#else
            LeaderboardProviderFeatures.None;
#endif

        #endregion

        #region Serialized Fields

        [SerializeField]
        [HideInInspector]
        private string _providerId = "gamecenter";

        [SerializeField]
        [Tooltip("Timeout in seconds for initialization and score reporting.")]
        private float timeoutSeconds = 3f;

        [SerializeField]
        [Tooltip("Timeout in seconds per GameKit LoadEntries request.")]
        private float fetchTimeoutSeconds = 10f;

        [SerializeField]
        [Tooltip("Seconds before cached leaderboard data is considered stale.")]
        private float cacheTimeoutSeconds = 300f;

        [SerializeField]
        private List<GameKitLeaderboardIdMapping> idMapping;

        #endregion

        #region Private Fields

        private bool _isInitialized;
        private Dictionary<string, string> _mappedIds;
        private Dictionary<string, CachedGameKitData> _cache;
        private readonly object _cacheLock = new object();

#if UNITY_IOS && APPLE_GAMEKIT
        private Dictionary<string, GKLeaderboard> _instances;
#endif

        #endregion

        #region Public Methods

        public async Task<bool> InitializeAsync()
        {
            _isInitialized = false;
            IsAvailable = false;

            BuildMappedDictionary();
            lock (_cacheLock)
            {
                _cache = new Dictionary<string, CachedGameKitData>();
            }

            QuickLog.Info<GameCenterLeaderboardProvider>(
                "[{0}] Starting initialization...", _providerId);

#if UNITY_IOS && APPLE_GAMEKIT
            try
            {
                bool authResult = await AuthenticateAsync();
                if (authResult)
                {
                    _instances = new Dictionary<string, GKLeaderboard>();
                    IsAvailable = true;
                    _isInitialized = true;
                    QuickLog.Info<GameCenterLeaderboardProvider>(
                        "[{0}] Initialized successfully.", _providerId);
                    return true;
                }

                QuickLog.Warning<GameCenterLeaderboardProvider>(
                    "[{0}] Authentication failed or unavailable.", _providerId);
                return false;
            }
            catch (Exception ex)
            {
                QuickLog.Error<GameCenterLeaderboardProvider>(
                    "[{0}] Initialization failed: {1}",
                    _providerId, ex.Message);
                return false;
            }
#else
            QuickLog.Warning<GameCenterLeaderboardProvider>(
                "[{0}] Game Center unavailable. Install the Apple.GameKit package "
                + "and define APPLE_GAMEKIT to enable it.",
                _providerId);
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
            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] ReportScore: id='{1}', score={2}, mode={3}",
                _providerId, leaderboardId, score, mode);

#if UNITY_IOS && APPLE_GAMEKIT
            try
            {
                long context = long.TryParse(metadata, out long ctx) ? ctx : 0L;

                GKLeaderboard lb = await ResolveLeaderboardInstanceAsync(leaderboardId);
                if (lb == null)
                {
                    QuickLog.Warning<GameCenterLeaderboardProvider>(
                        "[{0}] Leaderboard not found for id='{1}'",
                        _providerId, leaderboardId);
                    return;
                }

                await WithTimeout(
                    lb.SubmitScore(score, context, GKLocalPlayer.Local),
                    timeoutSeconds);

                QuickLog.Info<GameCenterLeaderboardProvider>(
                    "[{0}] ReportScore success: id='{1}', score={2}",
                    _providerId, leaderboardId, score);
            }
            catch (TimeoutException tex)
            {
                QuickLog.Error<GameCenterLeaderboardProvider>(
                    "[{0}] ReportScore timeout: id='{1}', msg={2}",
                    _providerId, leaderboardId, tex.Message);
                throw;
            }
            catch (Exception ex)
            {
                QuickLog.Error<GameCenterLeaderboardProvider>(
                    "[{0}] ReportScore failed: id='{1}', error={2}",
                    _providerId, leaderboardId, ex.Message);
                throw;
            }
            finally
            {
                InvalidateCacheInternal(leaderboardId);
            }
#else
            throw Unavailable("ReportScore");
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
            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] FetchLeaderboard: id='{1}', index={2}, size={3}, tf={4}",
                _providerId, leaderboardId, index, size, timeframe);

            if (index < 0) index = 0;
            if (size <= 0) size = 1;

#if UNITY_IOS && APPLE_GAMEKIT
            return await FetchRangeInternal(leaderboardId, index, size, timeframe, ct);
#else
            throw Unavailable("FetchLeaderboard");
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
            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] FetchAroundPlayer: id='{1}', radius={2}, tf={3}",
                _providerId, leaderboardId, radius, timeframe);

            if (radius < 0) radius = 0;
            if (radius > AroundPlayerMaxRadius)
            {
                QuickLog.Warning<GameCenterLeaderboardProvider>(
                    "[{0}] Radius {1} clamped to {2} (GameKit 100-entry cap).",
                    _providerId, radius, AroundPlayerMaxRadius);
                radius = AroundPlayerMaxRadius;
            }

#if UNITY_IOS && APPLE_GAMEKIT
            return await FetchAroundPlayerInternal(leaderboardId, radius, timeframe, ct);
#else
            throw Unavailable("FetchLeaderboardAroundPlayer");
#endif
        }

        public async Task<LeaderboardEntry> FetchPlayerEntryAsync(
            string leaderboardId,
            LeaderboardType type,
            CancellationToken ct = default,
            LeaderboardTimeframe timeframe = LeaderboardTimeframe.AllTime
        )
        {
            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] FetchPlayerEntry: id='{1}', tf={2}",
                _providerId, leaderboardId, timeframe);

#if UNITY_IOS && APPLE_GAMEKIT
            return await FetchPlayerEntryInternal(leaderboardId, timeframe, ct);
#else
            throw Unavailable("FetchPlayerEntry");
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

        private CachedGameKitData GetCacheEntry(string cacheKey)
        {
            lock (_cacheLock)
            {
                if (_cache == null) return null;
                _cache.TryGetValue(cacheKey, out CachedGameKitData entry);
                return entry;
            }
        }

        private bool IsCacheValid(CachedGameKitData entry)
        {
            if (entry == null) return false;
            return (DateTime.UtcNow - entry.CachedAt).TotalSeconds < cacheTimeoutSeconds;
        }

        private CachedGameKitData GetOrCreateCacheEntry(string cacheKey)
        {
            lock (_cacheLock)
            {
                _cache ??= new Dictionary<string, CachedGameKitData>();

                if (!_cache.TryGetValue(cacheKey, out CachedGameKitData entry))
                {
                    entry = new CachedGameKitData();
                    _cache[cacheKey] = entry;
                }
                return entry;
            }
        }

        private void UpdateCacheTimestamp(string cacheKey)
        {
            lock (_cacheLock)
            {
                if (_cache != null
                    && _cache.TryGetValue(cacheKey, out CachedGameKitData entry))
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
                    QuickLog.Info<GameCenterLeaderboardProvider>(
                        "[{0}] Invalidating entire cache ({1} entries)",
                        _providerId, _cache.Count);
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
                        QuickLog.Debug<GameCenterLeaderboardProvider>(
                            "[{0}] Cache invalidated {1} keys for id='{2}'",
                            _providerId, keysToRemove.Count, leaderboardId);
                    }
                }
            }
        }

        #endregion

        #region Private Methods — GameKit Helpers

#if UNITY_IOS && APPLE_GAMEKIT

        private async Task<bool> AuthenticateAsync()
        {
            if (GKLocalPlayer.Local == null || GKLocalPlayer.Local.IsAuthenticated)
            {
                bool authed = GKLocalPlayer.Local?.IsAuthenticated ?? false;
                QuickLog.Debug<GameCenterLeaderboardProvider>(
                    "[{0}] Already authenticated: {1}", _providerId, authed);
                return authed;
            }

            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] Starting GameKit authentication...", _providerId);

            GKLocalPlayer.AuthenticateUpdate += HandleAuthUpdate;
            GKLocalPlayer.AuthenticateError += HandleAuthError;

            try
            {
                GKLocalPlayer player = await GKLocalPlayer.Authenticate();
                bool success = player != null && player.IsAuthenticated;
                QuickLog.Debug<GameCenterLeaderboardProvider>(
                    "[{0}] Authentication result: {1}", _providerId, success);
                return success;
            }
            catch (Exception ex)
            {
                QuickLog.Warning<GameCenterLeaderboardProvider>(
                    "[{0}] Authentication threw: {1}", _providerId, ex.Message);
                return false;
            }
            finally
            {
                GKLocalPlayer.AuthenticateUpdate -= HandleAuthUpdate;
                GKLocalPlayer.AuthenticateError -= HandleAuthError;
            }
        }

        private void HandleAuthUpdate(GKLocalPlayer player)
        {
            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] GameKit auth update: displayName='{1}'",
                _providerId,
                player?.DisplayName ?? "null");
        }

        private void HandleAuthError(NSError error)
        {
            QuickLog.Warning<GameCenterLeaderboardProvider>(
                "[{0}] GameKit auth error: code={1}, description='{2}'",
                _providerId,
                error?.Code ?? -1,
                error?.LocalizedDescription ?? "null");
        }

        private async Task<GKLeaderboard> ResolveLeaderboardInstanceAsync(
            string leaderboardId)
        {
            if (_instances == null) return null;

            if (_instances.TryGetValue(leaderboardId, out GKLeaderboard cached))
            {
                return cached;
            }

            string gkId = ResolveMappedId(leaderboardId);

            try
            {
                NSArray<GKLeaderboard> boards = await GKLeaderboard.LoadLeaderboards(
                    new string[] { gkId });

                if (boards == null || boards.Count == 0)
                {
                    QuickLog.Warning<GameCenterLeaderboardProvider>(
                        "[{0}] LoadLeaderboards returned empty for gkId='{1}' / id='{2}'",
                        _providerId, gkId, leaderboardId);
                    return null;
                }

                GKLeaderboard lb = boards[0];
                _instances[leaderboardId] = lb;

                QuickLog.Debug<GameCenterLeaderboardProvider>(
                    "[{0}] Resolved leaderboard instance: id='{1}', gkId='{2}', "
                    + "baseId='{3}', title='{4}'",
                    _providerId, leaderboardId, gkId,
                    lb.BaseLeaderboardId, lb.Title);

                return lb;
            }
            catch (Exception ex)
            {
                QuickLog.Error<GameCenterLeaderboardProvider>(
                    "[{0}] LoadLeaderboards failed for id='{1}': {2}",
                    _providerId, leaderboardId, ex.Message);
                return null;
            }
        }

        private static LeaderboardResult BuildResultFromResponse(
            GKLeaderboardLoadEntriesResponse response,
            out int playerEntryIndex)
        {
            playerEntryIndex = -1;
            if (response.Entries == null || response.Entries.Count == 0)
            {
                return LeaderboardResult.Empty;
            }

            var entries = new LeaderboardEntry[response.Entries.Count];
            string localId = GKLocalPlayer.Local?.GamePlayerId
                ?? GKLocalPlayer.Local?.TeamPlayerId ?? string.Empty;

            for (int i = 0; i < response.Entries.Count; i++)
            {
                entries[i] = ConvertEntry(response.Entries[i]);
                if (playerEntryIndex < 0 && entries[i].PlayerId == localId)
                {
                    playerEntryIndex = i;
                }
            }

            return new LeaderboardResult(
                entries,
                (int)response.TotalPlayerCount,
                playerEntryIndex,
                null);
        }

        private static LeaderboardEntry ConvertEntry(GKLeaderboard.Entry entry)
        {
            if (entry == null) return default;

            return new LeaderboardEntry(
                (int)entry.Rank,
                entry.Player?.GamePlayerId
                    ?? entry.Player?.TeamPlayerId
                    ?? string.Empty,
                entry.Player?.DisplayName ?? entry.FormattedScore ?? string.Empty,
                entry.Score,
                entry.Date.UtcDateTime,
                entry.Context.ToString()
            );
        }

        private static GKLeaderboard.TimeScope ToTimeScope(
            LeaderboardTimeframe timeframe)
        {
            switch (timeframe)
            {
                case LeaderboardTimeframe.Daily:
                    return GKLeaderboard.TimeScope.Today;
                case LeaderboardTimeframe.Weekly:
                    return GKLeaderboard.TimeScope.Week;
                default:
                    return GKLeaderboard.TimeScope.AllTime;
            }
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

            QuickLog.Warning<GameCenterLeaderboardProvider>(
                "[{0}] Operation timed out after {1:F1}s",
                _providerId, timeoutSecs);
            throw new TimeoutException(
                $"GameKit operation timed out after {timeoutSecs:F1}s");
        }

        private async Task WithTimeout(Task task, float timeoutSecs)
        {
            if (task.IsCompleted) { await task; return; }

            Task delayTask = Task.Delay((int)(timeoutSecs * 1000));
            Task completed = await Task.WhenAny(task, delayTask);

            if (completed == task)
            {
                await task;
                return;
            }

            QuickLog.Warning<GameCenterLeaderboardProvider>(
                "[{0}] Operation timed out after {1:F1}s",
                _providerId, timeoutSecs);
            throw new TimeoutException(
                $"GameKit operation timed out after {timeoutSecs:F1}s");
        }

#endif

        #endregion

        #region Private Methods — Fetch Implementations

#if UNITY_IOS && APPLE_GAMEKIT

        private async Task<LeaderboardResult> FetchRangeInternal(
            string leaderboardId, int index, int size,
            LeaderboardTimeframe timeframe, CancellationToken ct)
        {
            string cacheKey = BuildCacheKey(leaderboardId, timeframe,
                $"__r{index}-{index + size}");
            CachedGameKitData cacheEntry = GetCacheEntry(cacheKey);

            if (IsCacheValid(cacheEntry) && cacheEntry.RangeResult != null)
            {
                QuickLog.Debug<GameCenterLeaderboardProvider>(
                    "[{0}] Cache hit for range: id='{1}', r=[{2},{3})",
                    _providerId, leaderboardId, index, index + size);
                return cacheEntry.RangeResult.Value;
            }

            GKLeaderboard lb = await ResolveLeaderboardInstanceAsync(leaderboardId);
            if (lb == null) return LeaderboardResult.Empty;

            GKLeaderboard.TimeScope timeScope = ToTimeScope(timeframe);

            int remaining = size;
            int currentStart = index + 1;
            int playerEntryIndex = -1;
            long totalPlayerCount = 0;
            var allEntries = new List<LeaderboardEntry>();

            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();

                int rankMax = Math.Min(
                    currentStart + MaxRangeSize - 1,
                    currentStart + remaining - 1);

                GKLeaderboardLoadEntriesResponse response = await WithTimeout(
                    lb.LoadEntries(
                        GKLeaderboard.PlayerScope.Global,
                        timeScope,
                        currentStart,
                        rankMax),
                    fetchTimeoutSeconds);

                LeaderboardResult partial = BuildResultFromResponse(
                    response, out int localIndex);

                if (partial.Entries.Length == 0) break;

                if (allEntries.Count == 0) totalPlayerCount = partial.TotalPlayers;

                int offset = allEntries.Count;
                allEntries.AddRange(partial.Entries);

                if (playerEntryIndex < 0 && localIndex >= 0)
                {
                    playerEntryIndex = offset + localIndex;
                }

                remaining -= partial.Entries.Length;
                currentStart += MaxRangeSize;

                if (partial.Entries.Length < MaxRangeSize) break;
            }

            var result = new LeaderboardResult(
                allEntries.ToArray(),
                (int)totalPlayerCount,
                playerEntryIndex,
                null);

            cacheEntry = GetOrCreateCacheEntry(cacheKey);
            cacheEntry.RangeResult = result;
            UpdateCacheTimestamp(cacheKey);

            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] Range fetch complete: id='{1}', r=[{2},{3}), "
                + "returned={4}, total={5}",
                _providerId, leaderboardId, index, index + size,
                result.Entries.Length, totalPlayerCount);

            return result;
        }

        private async Task<LeaderboardResult> FetchAroundPlayerInternal(
            string leaderboardId, int radius,
            LeaderboardTimeframe timeframe, CancellationToken ct)
        {
            string cacheKey = BuildCacheKey(leaderboardId, timeframe,
                AroundPlayerCacheSuffix);
            CachedGameKitData cacheEntry = GetCacheEntry(cacheKey);

            if (IsCacheValid(cacheEntry) && cacheEntry.AroundPlayerResult != null)
            {
                QuickLog.Debug<GameCenterLeaderboardProvider>(
                    "[{0}] Cache hit for around-player: id='{1}'",
                    _providerId, leaderboardId);
                return cacheEntry.AroundPlayerResult.Value;
            }

            GKLeaderboard lb = await ResolveLeaderboardInstanceAsync(leaderboardId);
            if (lb == null) return LeaderboardResult.Empty;

            GKLeaderboard.TimeScope timeScope = ToTimeScope(timeframe);

            GKLeaderboardLoadEntriesResponse probe = await WithTimeout(
                lb.LoadEntries(
                    GKLeaderboard.PlayerScope.Global, timeScope, 1, 1),
                fetchTimeoutSeconds);

            if (probe.LocalPlayerEntry == null)
            {
                int fetchSize = (radius * 2) + 1;
                GKLeaderboardLoadEntriesResponse fallback = await WithTimeout(
                    lb.LoadEntries(
                        GKLeaderboard.PlayerScope.Global, timeScope, 1, fetchSize),
                    fetchTimeoutSeconds);

                LeaderboardResult topResult = BuildResultFromResponse(
                    fallback, out int topIndex);

                cacheEntry = GetOrCreateCacheEntry(cacheKey);
                cacheEntry.AroundPlayerResult = topResult;
                UpdateCacheTimestamp(cacheKey);
                return topResult;
            }

            long playerRank = probe.LocalPlayerEntry.Rank;

            long rankMin = Math.Max(1L, playerRank - radius);
            long rankMax = rankMin + (radius * 2L);

            GKLeaderboardLoadEntriesResponse response = await WithTimeout(
                lb.LoadEntries(
                    GKLeaderboard.PlayerScope.Global, timeScope, rankMin, rankMax),
                fetchTimeoutSeconds);

            var entries = new LeaderboardEntry[response.Entries?.Count ?? 0];
            int playerEntryIndex = -1;
            string localId = GKLocalPlayer.Local?.GamePlayerId
                ?? GKLocalPlayer.Local?.TeamPlayerId ?? string.Empty;

            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = ConvertEntry(response.Entries[i]);
                if (playerEntryIndex < 0 && entries[i].PlayerId == localId)
                {
                    playerEntryIndex = i;
                }
            }

            var aroundResult = new LeaderboardResult(
                entries,
                (int)response.TotalPlayerCount,
                playerEntryIndex,
                (int)playerRank);

            cacheEntry = GetOrCreateCacheEntry(cacheKey);
            cacheEntry.AroundPlayerResult = aroundResult;
            UpdateCacheTimestamp(cacheKey);

            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] Around-player fetch complete: id='{1}', radius={2}, "
                + "entries={3}, playerIndex={4}, rank={5}",
                _providerId, leaderboardId, radius,
                entries.Length, playerEntryIndex, playerRank);

            return aroundResult;
        }

        private async Task<LeaderboardEntry> FetchPlayerEntryInternal(
            string leaderboardId,
            LeaderboardTimeframe timeframe,
            CancellationToken ct)
        {
            string cacheKey = BuildCacheKey(leaderboardId, timeframe,
                PlayerEntryCacheSuffix);
            CachedGameKitData cacheEntry = GetCacheEntry(cacheKey);

            if (IsCacheValid(cacheEntry) && cacheEntry.PlayerEntry.HasValue)
            {
                QuickLog.Debug<GameCenterLeaderboardProvider>(
                    "[{0}] Cache hit for player entry: id='{1}'",
                    _providerId, leaderboardId);
                return cacheEntry.PlayerEntry.Value;
            }

            GKLeaderboard lb = await ResolveLeaderboardInstanceAsync(leaderboardId);
            if (lb == null) return default;

            GKLeaderboard.TimeScope timeScope = ToTimeScope(timeframe);

            GKLeaderboardLoadEntriesResponse response = await WithTimeout(
                lb.LoadEntries(
                    GKLeaderboard.PlayerScope.Global, timeScope, 1, 1),
                fetchTimeoutSeconds);

            LeaderboardEntry entry;
            if (response.LocalPlayerEntry != null)
            {
                entry = ConvertEntry(response.LocalPlayerEntry);
            }
            else
            {
                QuickLog.Warning<GameCenterLeaderboardProvider>(
                    "[{0}] No player score found: id='{1}'",
                    _providerId, leaderboardId);
                entry = default;
            }

            cacheEntry = GetOrCreateCacheEntry(cacheKey);
            cacheEntry.PlayerEntry = entry;
            UpdateCacheTimestamp(cacheKey);

            return entry;
        }

#endif

        #endregion

        #region Private Methods — Timeframe Helpers

        private static string BuildCacheKey(
            string leaderboardId,
            LeaderboardTimeframe timeframe,
            string suffix = null)
        {
            string key = leaderboardId + TimeframePrefix
                + timeframe.ToString().ToLowerInvariant();
            if (!string.IsNullOrEmpty(suffix))
            {
                key += suffix;
            }
            return key;
        }

        #endregion

        #region Private Methods — Mapping

        private void BuildMappedDictionary()
        {
            _mappedIds = new Dictionary<string, string>();
            if (idMapping == null)
            {
                QuickLog.Warning<GameCenterLeaderboardProvider>(
                    "[{0}] idMapping is null. ID resolution will passthrough.",
                    _providerId);
                return;
            }

            foreach (GameKitLeaderboardIdMapping mapping in idMapping)
            {
                if (mapping == null) continue;
                _mappedIds[mapping.leaderboardId] = mapping.gameKitLeaderboardId;
            }

            QuickLog.Debug<GameCenterLeaderboardProvider>(
                "[{0}] Built ID mapping with {1} entries",
                _providerId, _mappedIds.Count);
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

        #region Private Methods — Errors

        private LeaderboardException Unavailable(string operation)
        {
            return new LeaderboardException(
                $"Game Center '{operation}' is unavailable because the Apple "
                + "GameKit integration is not compiled in. Install the "
                + "Apple.GameKit package and add the APPLE_GAMEKIT scripting "
                + "define symbol (iOS build).");
        }

        #endregion

        #region Nested Types

        private sealed class CachedGameKitData
        {
            public LeaderboardResult? RangeResult;
            public LeaderboardResult? AroundPlayerResult;
            public LeaderboardEntry? PlayerEntry;
            public DateTime CachedAt;
        }

        #endregion
    }

    [Serializable]
    public class GameKitLeaderboardIdMapping
    {
        public string leaderboardId;
        public string gameKitLeaderboardId;
    }
}
