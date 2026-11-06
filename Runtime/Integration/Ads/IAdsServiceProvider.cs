namespace Com.Scheherazade.Common.Integration.Ads
{
    public interface IAdsServiceProvider
    {
        IAdsManager AdsManager { get; set; }
        bool IsInitialized { get; }
        bool IsInterstitialAvailable { get; }
        bool IsRewardedAvailable { get; }
        bool IsBannerAvailable { get; }
        AdsBannerState BannerState { get; }
        bool IsOpenAppAdAvailable { get; }
        string DeviceAdvertisingId { get; }

        void Initialize();
        void CleanUp();

        void LoadAds();
        AdsInvocationHandler ShowBanner();
        AdsInvocationHandler HideBanner();
        AdsInvocationHandler ShowInterstitialAds(string placement);
        AdsInvocationHandler ShowRewardAds(string placement);
        AdsInvocationHandler ShowAppOpenAds(string placement);
    }
}
