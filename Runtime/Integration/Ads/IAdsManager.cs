using System;
using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    public interface IAdsManager
    {
        string DeviceAdvertisingId { get; }
        AdsManagerStatus Status { get; }

        bool IsBannerAvailable { get; }
        bool IsInterstitialAdsAvailable { get; }
        bool IsRewardAdsAvailable { get; }
        bool IsAppOpenAdsAvailable { get; }

        void Initialize(float timeOut = float.MaxValue);
        IEnumerator InitializeCoroutine(float timeOut = float.MaxValue);
        void Shutdown();

        void ShowBanner();
        void HideBanner();
        void ShowInterstitialAds(Action<bool> callback, string placement);
        void ShowRewardAds(Action<bool> callback, string placement);
        void ShowAppOpenAds(Action<bool> callback, string placement);
    }
}