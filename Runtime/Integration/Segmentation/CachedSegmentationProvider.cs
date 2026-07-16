using System;
using System.IO;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common;
using Com.Hapiga.Scheherazade.Common.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    [CreateAssetMenu(
        fileName = "CachedSegmentationProvider",
        menuName = "Scheherazade/Segmentation Providers/Cached"
    )]
    public class CachedSegmentationProvider :
        ScriptableObject,
        IUserSegmentationProvider,
        ITickableModule
    {
        [SerializeField]
        private string saveKey = "__segment_data__";

        [SerializeField]
        private float tickInterval = 1.0f;

        public IUserSegmentation Manager { get; set; }
        public bool IsInitialized { get; private set; }

        public event Action<SegmentationInformation> SegmentationDataAcquired;

        private DateTime _lastSavedTime;
        private float _tickCountdown;

    public async void Initialize()
    {
        string filePath = GetCacheFilePath(saveKey);

        if (!File.Exists(filePath))
        {
            QuickLog.Info<CachedSegmentationProvider>(
                "No cached segmentation data found."
            );
            IsInitialized = true;
            return;
        }

        SegmentationInformation cachedData;
        try
        {
            string json = await File.ReadAllTextAsync(filePath);
            cachedData =
                JsonConvert.DeserializeObject<SegmentationInformation>(
                    json
                );
        }
        catch (Exception ex)
        {
            QuickLog.Warning<CachedSegmentationProvider>(
                "Failed to load cached segmentation data: {0}",
                ex.Message
            );
            IsInitialized = true;
            return;
        }

        if (cachedData == null)
        {
            QuickLog.Warning<CachedSegmentationProvider>(
                "Loaded cached segmentation data was null."
            );
            IsInitialized = true;
            return;
        }

        QuickLog.Info<CachedSegmentationProvider>(
            "Loaded cached segmentation data."
        );

        _lastSavedTime = DateTime.UtcNow;
        _tickCountdown = tickInterval;
        IsInitialized = true;
        SegmentationDataAcquired?.Invoke(cachedData);
    }

    public void Tick(float deltaTime)
    {
        if (Manager == null || !IsInitialized) return;

        if (_tickCountdown > 0)
        {
            _tickCountdown -= deltaTime;
            return;
        }

        if (Manager.SegmentInformation == null) return;
        if (Manager.LastSegmentationUpdateTime == null) return;
        if (Manager.LastSegmentationUpdateTime.Value <= _lastSavedTime) return;

        QuickLog.Info<CachedSegmentationProvider>(
            "Segmentation data changed. Saving to cache."
        );

        SaveToFile(saveKey, Manager.SegmentInformation);
        _lastSavedTime = Manager.LastSegmentationUpdateTime.Value;
    }

        public void CleanUp()
        {
            IsInitialized = false;
        }

        private static void SaveToFile(
            string key, object data)
        {
            string filePath = GetCacheFilePath(key);
            string dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json =
                JsonConvert.SerializeObject(data);
            File.WriteAllText(filePath, json);
        }

        private static string GetCacheFilePath(string key)
        {
            return Path.Combine(
                Application.persistentDataPath,
                "com.hapiga.cache",
                key + ".json"
            );
        }
    }
}
