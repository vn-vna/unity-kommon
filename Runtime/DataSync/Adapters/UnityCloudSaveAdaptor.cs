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
            try
            {

                await CloudSaveService.Instance
                    .Files
                    .Player.DeleteAsync(key);
                return true;
            }
            catch
            {
                QuickLog.Warning<UnityCloudSaveAdaptor>(
                    "Cannot delete file saved by unity cloud save adaptor"
                );
            }

            return false;
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            try
            {
                FileItem metadata = await CloudSaveService.Instance
                    .Files
                    .Player.GetMetadataAsync(key);
                return metadata != null;
            }
            catch
            {
                QuickLog.Warning<UnityCloudSaveAdaptor>(
                    "Cannot delete file saved by unity cloud save adaptor"
                );
            }

            return false;
        }

        public async Task<DateTime?> GetLastWriteTimeAsync(string key, CancellationToken ct = default)
        {
            try
            {
                FileItem metadata = await CloudSaveService.Instance
                    .Files
                    .Player.GetMetadataAsync(key);

                return metadata?.Modified;
            }
            catch
            {
                QuickLog.Warning<UnityCloudSaveAdaptor>(
                    "Cannot delete file saved by unity cloud save adaptor"
                );
            }

            return null;
        }

        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        {
            try
            {
                return await CloudSaveService.Instance
                    .Files
                    .Player
                    .LoadStreamAsync(key);
            }
            catch
            {
                QuickLog.Warning<UnityCloudSaveAdaptor>(
                    "Cannot delete file saved by unity cloud save adaptor"
                );
            }

            return null;
        }

        public async Task WriteAsync(string key, Stream data, CancellationToken ct = default)
        {
            try
            {
                await CloudSaveService.Instance
                    .Files
                    .Player
                    .SaveAsync(key, data);
            }
            catch
            {
                QuickLog.Warning<UnityCloudSaveAdaptor>(
                    "Cannot delete file saved by unity cloud save adaptor"
                );
            }
        }
    }
}

#endif