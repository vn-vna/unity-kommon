using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    public interface IAdsServiceProvider
    {
        IAdsManager AdsManager { get; set; }
        bool IsInitialized { get; }
        bool IsInterstitialAvailable { get; }
        bool IsRewardedAvailable { get; }
        bool IsBannerAvailable { get; }
        bool IsOpenAppAdAvailable { get; }
        string DeviceAdvertisingId { get; }
        string AdSessionId { get; }

        void Initialize();
        void CleanUp();

        void LoadAds();
        void ShowBanner();
        void HideBanner();
        void OnAdDisplayedEvent();
        void OnAdRevenuePaidEvent();

        bool ShowInterstitialAds(Action<bool> callback, string placement);
        bool ShowRewardAds(Action<bool> callback, string placement);
        bool ShowAppOpenAds(Action<bool> callback, string placement);
    }
}