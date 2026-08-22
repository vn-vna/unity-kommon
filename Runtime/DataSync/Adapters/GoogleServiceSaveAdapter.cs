using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;
#endif

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [CreateAssetMenu(
        fileName = "GoogleServiceSaveAdapter",
        menuName = "Scheherazade/Data Sync/Google Service Save Adapter"
    )]
    public class GoogleServiceSaveAdapter :
        ScriptableObject,
        ISaveAdapter
    {
        #region Constants

        /// <summary>Minimum time between bounded sign-in waits, so the
        /// director's lazy refresh never stalls an operation.</summary>
        private const float AuthRecheckCooldownSeconds = 3f;

        private const string DefaultDescriptionTemplate =
            "Update for key [{key}] at: {datetime}";

        #endregion

        #region Properties

        public string AdapterId => _adapterId;

        public TimeSpan ReadTimeout => TimeSpan.FromSeconds(_readTimeoutSeconds);

        public bool IsAvailable { get; private set; }

        public SaveAdapterFeature SupportedFeatures
            => SaveAdapterFeature.Read
             | SaveAdapterFeature.Write
             | SaveAdapterFeature.Delete
             | SaveAdapterFeature.Exists
             | SaveAdapterFeature.Cloud;

        #endregion

        #region Serialized Fields

        [Tooltip("Identifier used by the Data Sync director for logging and conflict-plan matching.")]
        [SerializeField] private string _adapterId = "google_play_service_save";

        [Tooltip("Max seconds to wait for a read before the director treats it as a timeout.")]
        [Range(0.1f, 300f)]
        [SerializeField] private float _readTimeoutSeconds = 10f;

        [Tooltip("Max seconds to wait for Play Games sign-in before treating the adapter as unavailable.")]
        [Range(0.5f, 30f)]
        [SerializeField] private float _initTimeoutSeconds = 5f;

        [Header("Open / Conflict")]
        [Tooltip("Automatic uses a Play Games resolution policy. Manual resolves on-device via the configured preset.")]
        [SerializeField] private OpenMode _openMode = OpenMode.Automatic;

        [Tooltip("Where saved-game data is read from.")]
        [SerializeField] private DataSourceOption _dataSource = DataSourceOption.ReadCacheOrNetwork;

        [Tooltip("Conflict resolution strategy used in Automatic open mode.")]
        [SerializeField] private AutoConflictStrategy _autoConflictStrategy = AutoConflictStrategy.UseLastKnownGood;

        [Tooltip("How conflicts are resolved in Manual open mode.")]
        [SerializeField] private ManualConflictStrategy _manualConflictStrategy = ManualConflictStrategy.ChooseMostRecentlyModified;

        [Tooltip("When true, snapshot binary data is prefetched before the manual conflict callback fires.")]
        [SerializeField] private bool _prefetchDataOnConflict = true;

        [Header("Commit Update")]
        [Tooltip("When true, the snapshot description is updated on every write using the template below.")]
        [SerializeField] private bool _updateDescription = true;

        [Tooltip("Supports named tokens: {key}, {datetime}, {content_hash} (7-char content hash).")]
        [Multiline]
        [SerializeField] private string _descriptionTemplate =
            "Update for key [{key}] at: {datetime}";

        [Tooltip("When true, the snapshot total play time is updated on every write.")]
        [SerializeField] private bool _updatePlayedTime;

        [Tooltip("Total play time (seconds) written to the snapshot when the played-time update is enabled.")]
        [Min(0)]
        [SerializeField] private double _playedTimeSeconds;

        #endregion

        #region Private Fields

        private float _lastAuthAttemptTime = float.NegativeInfinity;

        #endregion

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
        public static ISavedGameClient Client => PlayGamesPlatform.Instance?.SavedGame;
#endif

        #region Public Methods

        public async Task<bool> InitializeAsync()
        {
            IsAvailable = false;

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
            try
            {
                return await InitializeInternalAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{AdapterId}] Init failed: {ex.Message}");
                return false;
            }
#else
            if (DataSyncLogging.Verbose)
            {
                Debug.Log($"[{AdapterId}] Not available on this platform.");
            }
            return false;
#endif
        }

        public void Reset()
        {
            IsAvailable = false;
            _lastAuthAttemptTime = float.NegativeInfinity;
        }

        public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
            await ValidateConnection();
            ISavedGameMetadata metadata = await OpenConnection(key, ct);
            ct.ThrowIfCancellationRequested();
            Client.Delete(metadata);
            return true;
#else
            return false;
#endif
        }

        public async Task<bool> ExistsAsync(
            string key, CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
            ISavedGameMetadata metadata = await OpenConnection(key, ct);
            using Stream stream = await OpenByteReadStream(metadata, ct);
            return stream != null && stream.Length > 0;
#else
            return false;
#endif
        }

        public async Task<DateTime?> GetLastWriteTimeAsync(
            string key, CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
            return (await OpenConnection(key, ct))
                ?.LastModifiedTimestamp;
#else
            return null;
#endif
        }

        public async Task<Stream> OpenReadAsync(
            string key, CancellationToken ct = default)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
            ISavedGameMetadata metadata = await OpenConnection(key, ct);
            return await OpenByteReadStream(metadata, ct);
#else
            return null;
#endif
        }

        public async Task WriteAsync(
            string key, Stream data,
            CancellationToken ct = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
            ct.ThrowIfCancellationRequested();
            ISavedGameMetadata metadata = await OpenConnection(
                key,
                ct
            );
            ct.ThrowIfCancellationRequested();
            byte[] bytes = null;
            if (data is MemoryStream stream)
                bytes = stream.ToArray();
            else
            {
                using MemoryStream ms = new MemoryStream();
                await data.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }

            if (bytes.Length == 0)
            {
                throw new DataSyncException(
                    "Google Play Saved Games cannot distinguish an empty "
                    + "payload from a missing save."
                );
            }

            await WriteToStorage(metadata, bytes, ct);
#endif
        }

        #endregion

        #region Private Methods

        private async Task ValidateConnection()
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
            if (PlayGamesPlatform.Instance == null)
            {
                throw new DataSyncException("Cannot find Play Games Platform instance");
            }

            if (!PlayGamesPlatform.Instance.IsAuthenticated())
            {
                throw new DataSyncException("Play Games Platform is not authenticated");
            }

            if (Client == null)
            {
                throw new DataSyncException("Cannot find Play Games Platform Saved Game instance");
            }
#else
            throw new InvalidOperationException("Goole Play Save is not available");
#endif
        }

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES && GOOGLE_SERVICES_SAVE
        private async Task<bool> InitializeInternalAsync()
        {
            if (PlayGamesPlatform.Instance == null)
            {
                if (DataSyncLogging.Verbose)
                {
                    Debug.LogWarning(
                        $"[{AdapterId}] PlayGamesPlatform not found. "
                        + "Not available."
                    );
                }
                return false;
            }

            if (Client == null)
            {
                if (DataSyncLogging.Verbose)
                {
                    Debug.LogWarning(
                        $"[{AdapterId}] SavedGame client not found. "
                        + "Not available."
                    );
                }
                return false;
            }

            if (PlayGamesPlatform.Instance.IsAuthenticated())
            {
                IsAvailable = true;
                return true;
            }

            // Sign-in happens asynchronously at startup; a one-shot snapshot
            // here would mark the adapter unavailable prematurely and skip
            // the cloud loader. Wait a bounded time for auth to complete.
            float now = Time.realtimeSinceStartup;
            if (now - _lastAuthAttemptTime < AuthRecheckCooldownSeconds)
            {
                // A refresh ran recently and auth was still pending; return
                // the current state quickly instead of stalling the op.
                return false;
            }

            _lastAuthAttemptTime = now;
            float deadline = now + _initTimeoutSeconds;
            while (!PlayGamesPlatform.Instance.IsAuthenticated())
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    if (DataSyncLogging.Verbose)
                    {
                        Debug.LogWarning(
                            $"[{AdapterId}] Not authenticated within "
                            + $"{_initTimeoutSeconds}s. Adapter unavailable "
                            + "until login."
                        );
                    }
                    return false;
                }

                await Task.Delay(50);
            }

            IsAvailable = true;
            return true;
        }

        private async Task<ISavedGameMetadata> OpenConnection(
            string key, CancellationToken ct)
        {
            await ValidateConnection();
            TaskCompletionSource<ISavedGameMetadata> tsc =
                new TaskCompletionSource<ISavedGameMetadata>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
            ct.ThrowIfCancellationRequested();

            if (_openMode == OpenMode.Manual)
            {
                Client.OpenWithManualConflictResolution(
                    key, ToDataSource(), _prefetchDataOnConflict,
                    OnConflict, (status, game) => CompleteOpen(tsc, status, game)
                );
            }
            else
            {
                Client.OpenWithAutomaticConflictResolution(
                    key, ToDataSource(), ToAutoStrategy(),
                    (status, game) => CompleteOpen(tsc, status, game)
                );
            }

            using CancellationTokenRegistration registration = ct.Register(
                () => tsc.TrySetCanceled()
            );
            Task completedTask = await Task.WhenAny(
                tsc.Task,
                Task.Delay(ReadTimeout, ct)
            );
            if (completedTask != tsc.Task)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    $"Saved game open timed out after {ReadTimeout.TotalSeconds:F1}s."
                );
            }

            return await tsc.Task;
        }

        private static void CompleteOpen(
            TaskCompletionSource<ISavedGameMetadata> tsc,
            SavedGameRequestStatus status,
            ISavedGameMetadata game)
        {
            if (status != SavedGameRequestStatus.Success)
            {
                tsc.TrySetException(new DataSyncException(
                    $"Saved game open failed with status '{status}'."
                ));
                return;
            }

            tsc.TrySetResult(game);
        }

        private void OnConflict(
            IConflictResolver resolver,
            ISavedGameMetadata original,
            byte[] originalData,
            ISavedGameMetadata unmerged,
            byte[] unmergedData)
        {
            ResolvePreset(resolver, original, unmerged);
        }

        private void ResolvePreset(
            IConflictResolver resolver,
            ISavedGameMetadata original,
            ISavedGameMetadata unmerged)
        {
            switch (_manualConflictStrategy)
            {
                case ManualConflictStrategy.ChooseOriginal:
                    resolver.ChooseMetadata(original);
                    return;
                case ManualConflictStrategy.ChooseUnmerged:
                    resolver.ChooseMetadata(unmerged);
                    return;
                case ManualConflictStrategy.ChooseLongestPlaytime:
                    resolver.ChooseMetadata(
                        original.TotalTimePlayed >= unmerged.TotalTimePlayed
                            ? original
                            : unmerged
                    );
                    return;
                default:
                    resolver.ChooseMetadata(
                        original.LastModifiedTimestamp >= unmerged.LastModifiedTimestamp
                            ? original
                            : unmerged
                    );
                    return;
            }
        }

        private DataSource ToDataSource()
        {
            return _dataSource == DataSourceOption.ReadNetworkOnly
                ? DataSource.ReadNetworkOnly
                : DataSource.ReadCacheOrNetwork;
        }

        private ConflictResolutionStrategy ToAutoStrategy()
        {
            switch (_autoConflictStrategy)
            {
                case AutoConflictStrategy.UseMostRecentlySaved:
                    return ConflictResolutionStrategy.UseMostRecentlySaved;
                case AutoConflictStrategy.UseLongestPlaytime:
                    return ConflictResolutionStrategy.UseLongestPlaytime;
                case AutoConflictStrategy.UseOriginal:
                    return ConflictResolutionStrategy.UseOriginal;
                case AutoConflictStrategy.UseUnmerged:
                    return ConflictResolutionStrategy.UseUnmerged;
                default:
                    return ConflictResolutionStrategy.UseLastKnownGood;
            }
        }

        private async Task<Stream> OpenByteReadStream(
            ISavedGameMetadata metadata,
            CancellationToken cancellationToken)
        {
            await ValidateConnection();

            TaskCompletionSource<Stream> tsc = new TaskCompletionSource<Stream>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            cancellationToken.ThrowIfCancellationRequested();
            Client.ReadBinaryData(metadata, (readStatus, data) =>
            {
                if (readStatus != SavedGameRequestStatus.Success)
                {
                    tsc.TrySetException(new DataSyncException(
                        $"Saved game read failed with status '{readStatus}'."
                    ));
                    return;
                }

                if (data == null || data.Length == 0)
                {
                    tsc.TrySetResult(null);
                    return;
                }

                tsc.TrySetResult(new MemoryStream(data, writable: false));
            });
            using CancellationTokenRegistration registration
                = cancellationToken.Register(() => tsc.TrySetCanceled());
            return await tsc.Task;
        }

        private async Task<ISavedGameMetadata> WriteToStorage(
            ISavedGameMetadata metadata,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            await ValidateConnection();
            cancellationToken.ThrowIfCancellationRequested();

            TaskCompletionSource<ISavedGameMetadata> tsc =
                new TaskCompletionSource<ISavedGameMetadata>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
            SavedGameMetadataUpdate update = BuildUpdate(metadata, bytes);
            cancellationToken.ThrowIfCancellationRequested();

            Client.CommitUpdate(
                metadata, update, bytes,
                (status, updated) =>
                {
                    if (status != SavedGameRequestStatus.Success)
                    {
                        tsc.TrySetException(new DataSyncException(
                            $"Saved game commit failed with status '{status}'."
                        ));
                        return;
                    }

                    tsc.TrySetResult(updated);
                }
            );

            using CancellationTokenRegistration registration
                = cancellationToken.Register(() => tsc.TrySetCanceled());
            Task completedTask = await Task.WhenAny(
                tsc.Task,
                Task.Delay(ReadTimeout, cancellationToken)
            );
            if (completedTask != tsc.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    $"Saved game commit timed out after {ReadTimeout.TotalSeconds:F1}s."
                );
            }

            ISavedGameMetadata result = await tsc.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private SavedGameMetadataUpdate BuildUpdate(
            ISavedGameMetadata metadata, byte[] bytes)
        {
            SavedGameMetadataUpdate.Builder builder =
                new SavedGameMetadataUpdate.Builder();

            if (_updateDescription)
            {
                string template = string.IsNullOrEmpty(_descriptionTemplate)
                    ? DefaultDescriptionTemplate
                    : _descriptionTemplate;
                builder = builder.WithUpdatedDescription(
                    FormatNamedTemplate(
                        template,
                        metadata.Filename,
                        DateTime.UtcNow,
                        ComputeContentHash(bytes)
                    )
                );
            }

            if (_updatePlayedTime)
            {
                builder = builder.WithUpdatedPlayedTime(
                    TimeSpan.FromSeconds(_playedTimeSeconds)
                );
            }

            return builder.Build();
        }

        /// <summary>
        /// Replaces named tokens in the description template. Unknown tokens
        /// are left untouched so the developer keeps full control of the text.
        /// </summary>
        private static string FormatNamedTemplate(
            string template,
            string key,
            DateTime utcNow,
            string contentHash)
        {
            return Regex.Replace(template, @"\{(\w+)\}", match =>
            {
                switch (match.Groups[1].Value)
                {
                    case "key":
                        return key;
                    case "datetime":
                        return utcNow.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture
                        );
                    case "content_hash":
                        return contentHash;
                    default:
                        return match.Value;
                }
            });
        }

        /// <summary>Stable 7-char FNV-1a hash of the saved payload.</summary>
        private static string ComputeContentHash(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "0000000";
            }

            uint hash = 2166136261;
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= 16777619;
            }

            return (hash & 0x0FFFFFFF).ToString(
                "X7", System.Globalization.CultureInfo.InvariantCulture
            );
        }
#endif

        #endregion

        #region Nested Types

        /// <summary>How the Play Games saved-game file is opened.</summary>
        public enum OpenMode
        {
            Automatic,
            Manual
        }

        /// <summary>Mirrors <c>GooglePlayGames.BasicApi.DataSource</c> so the
        /// config serializes without depending on the Google assembly.</summary>
        public enum DataSourceOption
        {
            ReadCacheOrNetwork,
            ReadNetworkOnly
        }

        /// <summary>Mirrors <c>GooglePlayGames.BasicApi.SavedGame.ConflictResolutionStrategy</c>
        /// values valid for automatic resolution.</summary>
        public enum AutoConflictStrategy
        {
            UseLastKnownGood,
            UseMostRecentlySaved,
            UseLongestPlaytime,
            UseOriginal,
            UseUnmerged
        }

        /// <summary>Presets applied on-device in Manual open mode.</summary>
        public enum ManualConflictStrategy
        {
            ChooseOriginal,
            ChooseUnmerged,
            ChooseMostRecentlyModified,
            ChooseLongestPlaytime
        }

        #endregion
    }
}
