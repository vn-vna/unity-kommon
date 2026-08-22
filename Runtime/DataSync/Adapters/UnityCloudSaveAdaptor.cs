#if UNITY_CLOUDSAVE

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [CreateAssetMenu(
        fileName = "UnityCloudSaveAdaptor",
        menuName = "Scheherazade/Data Sync/Unity Cloud Save Adaptor"
    )]
    public class UnityCloudSaveAdaptor :
        ScriptableObject,
        ISaveAdapter
    {
        public string AdapterId => "unity-cloud-save";

        public TimeSpan ReadTimeout => TimeSpan.FromSeconds(10);

        public bool IsAvailable { get; private set; }

        public SaveAdapterFeature SupportedFeatures
            => SaveAdapterFeature.Read
             | SaveAdapterFeature.Write
             | SaveAdapterFeature.Delete
             | SaveAdapterFeature.Exists
             | SaveAdapterFeature.Cloud;

        [Tooltip("Max seconds to retry initializing Unity Cloud Save before treating the adapter as unavailable.")]
        [Range(0.5f, 30f)]
        [SerializeField] private float _initRetryTimeoutSeconds = 5f;

        public void Reset()
        { }

        public async Task<bool> InitializeAsync()
        {
            IsAvailable = false;

            float deadline = Time.realtimeSinceStartup + _initRetryTimeoutSeconds;
            while (true)
            {
                try
                {
                    List<FileItem> files = await CloudSaveService
                        .Instance
                        .Files
                        .Player
                        .ListAllAsync();

                    IsAvailable = true;
                    return true;
                }
                catch (Exception ex)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        QuickLog.Warning<UnityCloudSaveAdaptor>(
                            "Cannot initialize unity cloud save adaptor: {0}",
                            ex.Message
                        );
                        return false;
                    }

                    // Unity Services / auth may still be initializing; retry
                    // within the budget instead of failing immediately.
                    await Task.Delay(100);
                }
            }
        }

        public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            if (!await ExistsAsync(key, ct)) return false;

            try
            {
                ct.ThrowIfCancellationRequested();
                await CloudSaveService.Instance
                    .Files
                    .Player.DeleteAsync(key);
                ct.ThrowIfCancellationRequested();
                return true;
            }
            catch (Exception exception)
            {
                QuickLog.Warning<UnityCloudSaveAdaptor>(
                    "Cannot delete cloud save file '{0}': {1}",
                    key,
                    exception.Message
                );
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            List<FileItem> files = await CloudSaveService.Instance
                .Files
                .Player
                .ListAllAsync();
            ct.ThrowIfCancellationRequested();
            return files.Exists(file => string.Equals(
                file.Key,
                key,
                StringComparison.Ordinal
            ));
        }

        public async Task<DateTime?> GetLastWriteTimeAsync(string key, CancellationToken ct = default)
        {
            if (!await ExistsAsync(key, ct)) return null;

            ct.ThrowIfCancellationRequested();
            FileItem metadata = await CloudSaveService.Instance
                .Files
                .Player.GetMetadataAsync(key);
            ct.ThrowIfCancellationRequested();
            return metadata?.Modified;
        }

        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        {
            if (!await ExistsAsync(key, ct)) return null;

            ct.ThrowIfCancellationRequested();
            Stream stream = await CloudSaveService.Instance
                .Files
                .Player
                .LoadStreamAsync(key);
            if (ct.IsCancellationRequested)
            {
                stream?.Dispose();
                ct.ThrowIfCancellationRequested();
            }

            return stream;
        }

        public async Task WriteAsync(string key, Stream data, CancellationToken ct = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            try
            {
                ct.ThrowIfCancellationRequested();
                using var payloadBuffer = new MemoryStream();
                await data.CopyToAsync(payloadBuffer, 81920, ct);
                var uploadStream = new MemoryStream(
                    payloadBuffer.ToArray(),
                    writable: false
                );
                Task saveTask;
                try
                {
                    saveTask = CloudSaveService.Instance
                        .Files
                        .Player
                        .SaveAsync(key, uploadStream);
                }
                catch
                {
                    uploadStream.Dispose();
                    throw;
                }

                Task completedTask = await Task.WhenAny(
                    saveTask,
                    Task.Delay(ReadTimeout, ct)
                );
                if (completedTask != saveTask)
                {
                    ObservePendingUpload(saveTask, uploadStream);
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException(
                        $"Cloud save write timed out for '{key}'."
                    );
                }

                try
                {
                    await saveTask;
                    ct.ThrowIfCancellationRequested();
                }
                finally
                {
                    uploadStream.Dispose();
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                QuickLog.Warning<UnityCloudSaveAdaptor>(
                    "Cannot write cloud save file '{0}': {1}",
                    key,
                    exception.Message
                );
                throw;
            }
        }

        private static void ObservePendingUpload(
            Task saveTask,
            Stream uploadStream)
        {
            _ = saveTask.ContinueWith(
                completedTask =>
                {
                    if (completedTask.IsFaulted)
                    {
                        _ = completedTask.Exception;
                    }

                    uploadStream.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
    }
}

#endif
