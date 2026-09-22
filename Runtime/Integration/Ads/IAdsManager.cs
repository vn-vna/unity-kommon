using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    public interface IAdsManager
    {
        string DeviceAdvertisingId { get; }
        AdsManagerStatus Status { get; }

        bool IsBannerAvailable { get; }
        AdsBannerState BannerState { get; }
        bool IsInterstitialAdsAvailable { get; }
        bool IsRewardAdsAvailable { get; }
        bool IsAppOpenAdsAvailable { get; }

        void Initialize(float timeOut = float.MaxValue);
        IEnumerator InitializeCoroutine(float timeOut = float.MaxValue);
        void Shutdown();

        AdsInvocationHandler ShowBanner();
        AdsInvocationHandler HideBanner();
        AdsInvocationHandler ShowInterstitialAds(string placement, bool force = false);
        AdsInvocationHandler ShowRewardAds(string placement);
        AdsInvocationHandler ShowAppOpenAds(string placement);
    }
}
