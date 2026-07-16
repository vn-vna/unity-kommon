using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;
using UnityEngine;

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
        public string AdapterId => "google_play_service_save";

        public bool IsAvailable { get; private set; }

        public static ISavedGameClient Client
            => PlayGamesPlatform.Instance?.SavedGame;

        public async Task<bool> InitializeAsync()
        {
            IsAvailable = false;

#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            try
            {
                if (PlayGamesPlatform.Instance == null)
                {
                    Debug.LogWarning(
                        $"[{AdapterId}] PlayGamesPlatform not found. "
                        + "Not available."
                    );
                    return false;
                }

                if (Client == null)
                {
                    Debug.LogWarning(
                        $"[{AdapterId}] SavedGame client not found. "
                        + "Not available."
                    );
                    return false;
                }

                if (!PlayGamesPlatform.Instance.IsAuthenticated())
                {
                    Debug.LogWarning(
                        $"[{AdapterId}] Not authenticated. "
                        + "Adapter unavailable until login."
                    );
                    return false;
                }

                IsAvailable = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{AdapterId}] Init failed: {ex.Message}");
                return false;
            }
#else
            Debug.Log($"[{AdapterId}] Not available on this platform.");
            return false;
#endif
        }

        public void Reset()
        {
            IsAvailable = false;
        }

        public async Task<bool> DeleteAsync(
            string key, CancellationToken ct = default)
        {
            if (!PlayGamesPlatform.Instance.IsAuthenticated())
                return false;
            return true;
        }

        public async Task<bool> ExistsAsync(
            string key, CancellationToken ct = default)
        {
            return (await OpenConnection(key, ct)) != null;
        }

        public async Task<DateTime?> GetLastWriteTimeAsync(
            string key, CancellationToken ct = default)
        {
            return (await OpenConnection(key, ct))
                ?.LastModifiedTimestamp;
        }

        public async Task<Stream> OpenReadAsync(
            string key, CancellationToken ct = default)
        {
            ISavedGameMetadata metadata = await OpenConnection(key, ct);
            if (metadata == null) return null;
            return await OpenByteReadStream(metadata);
        }

        public async Task WriteAsync(
            string key, Stream data, 
            CancellationToken ct = default)
        {
            ISavedGameMetadata metadata = await OpenConnection(key, ct);
            if (metadata == null) return;
            byte[] bytes = null;
            if (data is MemoryStream stream)
                bytes = stream.ToArray();
            else
            {
                using MemoryStream ms = new MemoryStream();
                await data.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }

            await WriteToStorage(metadata, bytes);
        }

        private async Task ValidateConnection()
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
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

        private async Task<ISavedGameMetadata> OpenConnection(
            string key, CancellationToken ct)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            await ValidateConnection();
            TaskCompletionSource<ISavedGameMetadata> tsc =
                new TaskCompletionSource<ISavedGameMetadata>();
            Client.OpenWithAutomaticConflictResolution(
                key, DataSource.ReadCacheOrNetwork,
                ConflictResolutionStrategy.UseLastKnownGood,
                (status, game) =>
                {
                    if (status != SavedGameRequestStatus.Success)
                        tsc.SetResult(null);
                    tsc.SetResult(game);
                }
            );

            ct.Register(() => tsc.TrySetCanceled());
            return await tsc.Task;
#else
            throw new InvalidOperationException("Goole Play Save is not available");
#endif
        }

        private async Task<Stream> OpenByteReadStream(
            ISavedGameMetadata metadata)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            await ValidateConnection();

            TaskCompletionSource<Stream> tsc =
                new TaskCompletionSource<Stream>();
            Client.ReadBinaryData(metadata, (readStatus, data) =>
            {
                if (readStatus != SavedGameRequestStatus.Success)
                    tsc.SetResult(null);
                tsc.SetResult(new MemoryStream(data));
            });
            return await tsc.Task;
#else
            throw new InvalidOperationException("Goole Play Save is not available");
#endif
        }

        private async Task<ISavedGameMetadata> WriteToStorage(
            ISavedGameMetadata metadata, byte[] bytes)
        {
#if UNITY_ANDROID && GOOGLE_PLAY_GAMES
            await ValidateConnection();

            TaskCompletionSource<ISavedGameMetadata> tsc =
                new TaskCompletionSource<ISavedGameMetadata>();
            SavedGameMetadataUpdate update =
                new SavedGameMetadataUpdate.Builder()
                    .WithUpdatedDescription(
                        string.Format(
                            "Update for key [{0}] at: {1}",
                            metadata.Filename,
                            DateTime.UtcNow
                        )
                    )
                    .Build();

            Client.CommitUpdate(
                metadata, update, bytes,
                (status, updated) =>
                {
                    if (status != SavedGameRequestStatus.Success)
                        tsc.SetResult(null);
                    tsc.SetResult(updated);
                }
            );

            return await tsc.Task;
#else
            throw new InvalidOperationException("Goole Play Save is not available");
#endif
        }
    }
}