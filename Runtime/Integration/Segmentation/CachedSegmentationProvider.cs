using System;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common;
using Com.Hapiga.Scheherazade.Common.DataSync;
using DS = global::Com.Hapiga.Scheherazade.Common.DataSync.DataSync;
using Com.Hapiga.Scheherazade.Common.Logging;
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
        if (!await DS.ExistsAsync(saveKey))
        {
            QuickLog.Info<CachedSegmentationProvider>(
                "No cached segmentation data found."
            );
            IsInitialized = true;
            return;
        }

        SegmentationInformation cachedData = await DS.LoadAsync<SegmentationInformation>(saveKey);

        if (cachedData == null)
        {
            QuickLog.Warning<CachedSegmentationProvider>(
                "Failed to load cached segmentation data."
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

        DS.Save(saveKey, Manager.SegmentInformation);
        _lastSavedTime = Manager.LastSegmentationUpdateTime.Value;
    }

        public void CleanUp()
        {
            IsInitialized = false;
        }
    }
}
