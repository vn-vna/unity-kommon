using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public static class DataSync
    {
        #region Initialization Guard

        /// <summary>
        /// Ensures the DataSyncDirector is fully initialized before
        /// any operation is dispatched. Safe to call before the bootstrap
        /// has completed — will await asynchronously.
        /// </summary>
        private static async Task EnsureReadyAsync()
        {
            await DataSyncDirector.ReadyTask;
        }

        #endregion

        #region Typed API (async)

        public static async Task SaveAsync<T>(
            string key,
            T data,
            CancellationToken ct = default
        ) where T : IVersionedData
        {
            await EnsureReadyAsync();
            await DataSyncDirector.Instance.SaveAsync(key, data, ct);
        }

        public static async Task<T> LoadAsync<T>(
            string key,
            CancellationToken ct = default
        ) where T : IVersionedData, new()
        {
            await EnsureReadyAsync();
            return await DataSyncDirector.Instance.LoadAsync<T>(
                key, ct
            );
        }

        public static async Task DeleteAsync(
            string key,
            CancellationToken ct = default
        )
        {
            await EnsureReadyAsync();
            await DataSyncDirector.Instance.DeleteAsync(key, ct);
        }

        public static async Task<bool> ExistsAsync(
            string key,
            CancellationToken ct = default
        )
        {
            await EnsureReadyAsync();
            return await DataSyncDirector.Instance.ExistsAsync(
                key, ct
            );
        }

        #endregion

        #region Raw Stream API (async)

        public static async Task<Stream> OpenReadStreamAsync(
            string key,
            CancellationToken ct = default
        )
        {
            await EnsureReadyAsync();
            return await DataSyncDirector.Instance
                .OpenReadStreamAsync(key, ct);
        }

        public static async Task WriteStreamAsync(
            string key,
            Stream data,
            CancellationToken ct = default
        )
        {
            await EnsureReadyAsync();
            await DataSyncDirector.Instance.WriteStreamAsync(
                key, data, ct
            );
        }

        #endregion

        #region Fire-and-Forget (safe for Unity Update callers)

        public static async void Save<T>(string key, T data)
            where T : IVersionedData
        {
            try
            {
                await EnsureReadyAsync();
                await DataSyncDirector.Instance.SaveAsync(
                    key, data
                );
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[DataSync] Save failed for '{key}': {ex}"
                );
            }
        }

        public static async void Load<T>(
            string key,
            Action<T> onLoaded
        ) where T : IVersionedData, new()
        {
            try
            {
                await EnsureReadyAsync();
                T result = await DataSyncDirector.Instance
                    .LoadAsync<T>(key);
                onLoaded?.Invoke(result);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[DataSync] Load failed for '{key}': {ex}"
                );
            }
        }

        #endregion
    }
}
