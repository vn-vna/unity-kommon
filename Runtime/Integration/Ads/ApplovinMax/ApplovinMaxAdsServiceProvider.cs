#if APPLOVIN_MAX

using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    public partial class ApplovinMaxAdsServiceProvider :
        IAdsServiceProvider
    {
        #region Interfaces & Properties

        public string DeviceAdvertisingId => PlayerPrefs.GetString("advertising_id", string.Empty);

        public IAdsManager AdsManager { get; set; }
        public bool IsInitialized { get; private set; }

        public bool IsInterstitialAvailable
        {
            get
            {
                if (!IsInitialized) return false;
                return MaxSdk.IsInterstitialReady(Configuration.UnitIdsMapping[AdsType.Interstitial].UnitId);
            }
        }

        public bool IsRewardedAvailable
        {
            get
            {
                if (!IsInitialized) return false;
                return MaxSdk.IsRewardedAdReady(Configuration.UnitIdsMapping[AdsType.Rewarded].UnitId);
            }
        }

        public bool IsOpenAppAdAvailable
        {
            get
            {
                if (!IsInitialized) return false;
                return MaxSdk.IsAppOpenAdReady(Configuration.UnitIdsMapping[AdsType.OpenApp].UnitId);
            }
        }

        public bool IsBannerAvailable { get; private set; }
        public ApplovinMaxAdsConfiguration Configuration { get; private set; }

        #endregion

        #region Private Fields

        private bool _intersitialFulfilled;
        private bool _rewardFulfilled;
        private Action<bool> _interstitialCallback;
        private Action<bool> _rewardedCallback;
        private float _timer;
        private bool _bannerAutoRefreshing;

        #endregion

        #region Ctor

        public ApplovinMaxAdsServiceProvider(ApplovinMaxAdsConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            Configuration = configuration;
        }

        #endregion

        #region Public Methods

        public void Initialize()
        {
            MaxSdkCallbacks.OnSdkInitializedEvent += HandleMaxSdkInitializedEvents;

            MaxSdkCallbacks.AppOpen.OnAdLoadedEvent += HandleAppOpenAdLoaded;
            MaxSdkCallbacks.AppOpen.OnAdLoadFailedEvent += HandleAppOpenAdFailedToLoad;
            MaxSdkCallbacks.AppOpen.OnAdRevenuePaidEvent += HandleAppOpenAdRevenuePaid;
            MaxSdkCallbacks.AppOpen.OnAdClickedEvent += HandleAppOpenAdClicked;
            MaxSdkCallbacks.AppOpen.OnAdDisplayedEvent += HandleAppOpenAdDisplayed;
            MaxSdkCallbacks.AppOpen.OnAdDisplayFailedEvent += HandleAppOpenAdDisplayFailed;
            MaxSdkCallbacks.AppOpen.OnAdHiddenEvent += HandleAppOpenAdHidden;

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += HandleInterstitialAdLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += HandleInterstitialAdFailedToLoad;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += HandleInterstitialAdRevenuePaid;
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent += HandleInterstitialAdClicked;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += HandleInterstitialAdDisplayed;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += HandleInterstitialAdDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += HandleInterstitialAdHidden;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += HandleRewardedAdLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += HandleRewardedAdFailedToLoad;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += HandleRewardedAdRevenuePaid;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent += HandleRewardedAdClicked;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += HandleRewardedAdDisplayed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += HandleRewardedAdDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += HandleRewardedAdHidden;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += HandleRewardedAdReceivedReward;

            MaxSdkCallbacks.Banner.OnAdLoadedEvent += HandleBannerAdLoaded;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent += HandleBannerAdFailedToLoad;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += HandleBannerAdRevenuePaid;

            MaxSdk.InitializeSdk();
        }

        public void CleanUp()
        {
            if (!IsInitialized)
            {
                return;
            }

            MaxSdkCallbacks.OnSdkInitializedEvent -= HandleMaxSdkInitializedEvents;

            MaxSdkCallbacks.AppOpen.OnAdLoadedEvent -= HandleAppOpenAdLoaded;
            MaxSdkCallbacks.AppOpen.OnAdLoadFailedEvent -= HandleAppOpenAdFailedToLoad;
            MaxSdkCallbacks.AppOpen.OnAdRevenuePaidEvent -= HandleAppOpenAdRevenuePaid;
            MaxSdkCallbacks.AppOpen.OnAdClickedEvent -= HandleAppOpenAdClicked;
            MaxSdkCallbacks.AppOpen.OnAdDisplayedEvent -= HandleAppOpenAdDisplayed;
            MaxSdkCallbacks.AppOpen.OnAdDisplayFailedEvent -= HandleAppOpenAdDisplayFailed;
            MaxSdkCallbacks.AppOpen.OnAdHiddenEvent -= HandleAppOpenAdHidden;

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent -= HandleInterstitialAdLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent -= HandleInterstitialAdFailedToLoad;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent -= HandleInterstitialAdRevenuePaid;
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent -= HandleInterstitialAdClicked;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent -= HandleInterstitialAdDisplayed;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent -= HandleInterstitialAdDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent -= HandleInterstitialAdHidden;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent -= HandleRewardedAdLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent -= HandleRewardedAdFailedToLoad;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent -= HandleRewardedAdRevenuePaid;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent -= HandleRewardedAdClicked;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent -= HandleRewardedAdDisplayed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent -= HandleRewardedAdDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent -= HandleRewardedAdHidden;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent -= HandleRewardedAdReceivedReward;

            MaxSdkCallbacks.Banner.OnAdLoadedEvent -= HandleBannerAdLoaded;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent -= HandleBannerAdFailedToLoad;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent -= HandleBannerAdRevenuePaid;

            IsInitialized = false;
        }

        public void LoadAds()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (_timer < 5.0f)
            {
                _timer += Time.unscaledDeltaTime;
                return;
            }

            _timer = 0;

            LoadOpenAppAds();
            LoadInterstitialAds();
            LoadRewardedAds();
            LoadBannerAds();
        }

        public bool ShowAppOpenAds(Action<bool> callback, string placement)
        {
            return false;
        }

        public void ShowBanner()
        {
            try
            {
                MaxSdk.ShowBanner(Configuration.UnitIdsMapping[AdsType.Banner].UnitId);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>($"Failed to show banner ad: {ex.Message}");
            }
        }

        public void HideBanner()
        {
            try
            {
                MaxSdk.HideBanner(Configuration.UnitIdsMapping[AdsType.Banner].UnitId);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>($"Failed to hide banner ad: {ex.Message}");
            }
        }

        public bool ShowInterstitialAds(Action<bool> callback, string placement)
        {
            if (!IsInitialized || !IsInterstitialAvailable)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>("Interstitial Ads are not available.");
                callback?.Invoke(false);
                return false;
            }

            try
            {
                _intersitialFulfilled = false;
                MaxSdk.ShowInterstitial(
                    Configuration.UnitIdsMapping[AdsType.Interstitial].UnitId,
                    placement
                );
                SendAdsCallShowTrackingEvent(AdsType.Interstitial, placement);
            }
            catch (Exception ex)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>($"Failed to show interstitial ad: {ex.Message}");
                callback?.Invoke(false);
                return false;
            }

            _interstitialCallback = callback;
            return true;
        }

        public bool ShowRewardAds(Action<bool> callback, string placement)
        {
            if (!IsInitialized)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>("Applovin Max SDK is not initialized.");
                callback?.Invoke(false);
                return false;
            }

            if (IsRewardedAvailable)
            {
                _rewardFulfilled = false;
                bool called = false;
                try
                {
                    MaxSdk.ShowRewardedAd(
                        Configuration.UnitIdsMapping[AdsType.Rewarded].UnitId,
                        placement
                    );

                    _rewardedCallback = callback;
                    called = true;
                }
                catch (Exception ex)
                {
                    QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                        "Failed to show rewarded ad: {0}",
                        ex.Message
                    );
                }

                if (called)
                {
                    SendAdsCallShowTrackingEvent(AdsType.Rewarded, placement);
                    return true;
                }
            }

            if (IsInterstitialAvailable)
            {
                bool called = false;
                try
                {
                    MaxSdk.ShowInterstitial(
                        Configuration.UnitIdsMapping[AdsType.Interstitial].UnitId,
                        placement
                    );
                    _interstitialCallback = callback;
                    called = true;
                }
                catch (Exception ex)
                {
                    QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                        "Failed to show interstitial ad: {0}",
                        ex.Message
                    );
                }

                if (called)
                {
                    SendAdsCallShowTrackingEvent(AdsType.Rewarded, placement);
                    return true;
                }
            }

            QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                "Both Rewarded Ads and Interstitial Ads are not available."
            );
            InvokeAdsCallbackOnce(ref callback, false);
            return false;
        }

        #endregion

        #region Private Methods

        private static void SendAdsCallShowTrackingEvent(AdsType format, string placement)
        {
            if (string.IsNullOrEmpty(placement))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "Placement for AdsReward_CallShow tracking is null or empty."
                );
                return;
            }

            if (Integration.TrackingManager == null)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "TrackingManager is not available for tracking."
                );
                return;
            }

            Integration.TrackingManager
                ?.TrackAction(new TrackingActionInfo
                {
                    ActionId = format switch
                    {
                        AdsType.Rewarded => "AdsReward_CallShow",
                        AdsType.Interstitial => "AdsInter_CallShow",
                        AdsType.OpenApp => "AdsAppOpen_CallShow",
                        _ => null
                    },
                    Parameters = new Dictionary<string, object> {
                        { "placement", placement }
                    }
                });
        }

        private void InvokeAdsCallbackOnce(ref Action<bool> callback, bool param)
        {
            lock (this)
            {
                Action<bool> copiedInstance = callback;
                Dispatcher.DispatchOnMainThread(() => { copiedInstance?.Invoke(param); });
                callback = null;
            }
        }

        private void HandleMaxSdkInitializedEvents(MaxSdkBase.SdkConfiguration configuration)
        {
            if (Configuration.IsTestAds)
            {
                MaxSdk.ShowMediationDebugger();
            }

            if (Integration.TrackingManager != null)
            {
                Integration.TrackingManager.AllowTracking = true;
            }

#if UNITY_IOS
            if (MaxSdkUtils.CompareVersions(UnityEngine.iOS.Device.systemVersion, "14.5") != MaxSdkUtils.VersionComparisonResult.Lesser)
            {
                bool isTrackingEnabled = configuration.AppTrackingStatus == MaxSdk.AppTrackingStatus.Authorized;
                AudienceNetwork.AdSettings.SetAdvertiserTrackingEnabled(isTrackingEnabled);
            }
#endif
            try
            {
                MaxSdk.CreateBanner(
                    Configuration.UnitIdsMapping[AdsType.Banner].UnitId,
                    AcquireBannerAdsPosition(Configuration.BannerAdPosition)
                );

                string autoSized = Configuration.IsBannerAutoSized ? "true" : "false";
                MaxSdk.SetBannerExtraParameter(
                    Configuration.UnitIdsMapping[AdsType.Banner].UnitId,
                    "adaptive_banner",
                    autoSized
                );

                MaxSdk.SetBannerBackgroundColor(
                    Configuration.UnitIdsMapping[AdsType.Banner].UnitId,
                    Configuration.BannerBackgroundColor
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                    "Failed to create banner ad: {0}",
                    ex.Message
                );
            }


            IsInitialized = true;
        }

        private void LoadOpenAppAds()
        {
            if (IsOpenAppAdAvailable)
            {
                return;
            }

            if (!Configuration.EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.OpenApp))
            {
                return;
            }

            try
            {
                MaxSdk.LoadAppOpenAd(Configuration.UnitIdsMapping[AdsType.OpenApp].UnitId);
                SendTrackingAction("AdsAppOpen", "CallLoad");
                QuickLog.Info<ApplovinMaxAdsServiceProvider>("Loading Open App Ad");
            }
            catch (Exception ex)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "Failed to load app open ad: {0}",
                    ex.Message
                );
            }
        }

        private void LoadInterstitialAds()
        {
            if (IsInterstitialAvailable)
            {
                return;
            }

            if (!Configuration.EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Interstitial))
            {
                return;
            }

            try
            {
                MaxSdk.LoadInterstitial(Configuration.UnitIdsMapping[AdsType.Interstitial].UnitId);
                SendTrackingAction("AdsInter", "CallLoad");
                QuickLog.Info<ApplovinMaxAdsServiceProvider>("Loading Interstitial Ad");
            }
            catch (Exception ex)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "Failed to load interstitial ad: {0}",
                    ex.Message
                );
            }
        }

        private void LoadRewardedAds()
        {
            if (IsRewardedAvailable) return;

            if (!Configuration.EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Rewarded))
            {
                return;
            }

            try
            {
                MaxSdk.LoadRewardedAd(Configuration.UnitIdsMapping[AdsType.Rewarded].UnitId);
                SendTrackingAction("AdsReward", "CallLoad");
                QuickLog.Info<ApplovinMaxAdsServiceProvider>("Loading Rewarded Ad");
            }
            catch (Exception ex)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                    "Failed to load rewarded ad: {0}",
                    ex.Message
                );
            }
        }

        private void LoadBannerAds()
        {
            if (_bannerAutoRefreshing)
            {
                return;
            }

            if (IsBannerAvailable)
            {
                return;
            }

            if (!Configuration.EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Banner))
            {
                return;
            }

            try
            {
                MaxSdk.LoadBanner(Configuration.UnitIdsMapping[AdsType.Banner].UnitId);
                MaxSdk.StartBannerAutoRefresh(Configuration.UnitIdsMapping[AdsType.Banner].UnitId);
                _bannerAutoRefreshing = true;
                SendTrackingAction("AdsBanner", "CallLoad");
                QuickLog.Info<ApplovinMaxAdsServiceProvider>("Loading Banner Ad");
            }
            catch (Exception ex)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                    "Failed to load banner ad: {0}",
                    ex.Message
                );
            }
        }

        private void SendTrackingAction(string kind, string action)
        {
            Integration.TrackingManager
                ?.TrackAction(new TrackingActionInfo
                {
                    ActionId = $"{kind}_{action}"
                });
        }

        private void SendRevenueTracking(MaxSdkBase.AdInfo info, AdsType type)
        {
            Integration.TrackingManager
                ?.TrackAdRevenue(new AdTrackingInfo
                {
                    Provider = this,
                    NetworkName = "Applovin",
                    AdType = type,
                    RevenueUnit = Configuration.UnitIdsMapping[type].UnitId,
                    Placement = info.Placement,
                    Revenue = info.Revenue,
                    AdFormat = info.AdFormat,
                    CreativeIdentifier = info.CreativeIdentifier,
                    Country = MaxSdk.GetSdkConfiguration().CountryCode
                });
        }

        private MaxSdkBase.AdViewConfiguration AcquireBannerAdsPosition(BannerAdsPosition bannerAdPosition)
            => new MaxSdk.AdViewConfiguration(bannerAdPosition switch
            {
                BannerAdsPosition.TopCenter => MaxSdkBase.AdViewPosition.TopCenter,
                BannerAdsPosition.BottomCenter => MaxSdkBase.AdViewPosition.BottomCenter,
                BannerAdsPosition.TopLeft => MaxSdkBase.AdViewPosition.TopLeft,
                BannerAdsPosition.TopRight => MaxSdkBase.AdViewPosition.TopRight,
                BannerAdsPosition.Centered => MaxSdkBase.AdViewPosition.Centered,
                BannerAdsPosition.CenterLeft => MaxSdkBase.AdViewPosition.CenterLeft,
                BannerAdsPosition.CenterRight => MaxSdkBase.AdViewPosition.CenterRight,
                BannerAdsPosition.BottomLeft => MaxSdkBase.AdViewPosition.BottomLeft,
                BannerAdsPosition.BottomRight => MaxSdkBase.AdViewPosition.BottomRight,
                _ => MaxSdkBase.AdViewPosition.BottomCenter
            });

        #region Rewarded Ad Callbacks

        private void HandleRewardedAdReceivedReward(string arg1, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsReward", "Hidden");
            _rewardFulfilled = true;
        }

        private void HandleRewardedAdHidden(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsReward", "Hidden");
            InvokeAdsCallbackOnce(ref _rewardedCallback, _rewardFulfilled);
        }

        private void HandleRewardedAdDisplayFailed(string arg1, MaxSdkBase.ErrorInfo info1, MaxSdkBase.AdInfo info2)
        {
            SendTrackingAction("AdsReward", "DisplayFailed");
            InvokeAdsCallbackOnce(ref _rewardedCallback, false);
        }

        private void HandleRewardedAdDisplayed(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsReward", "Displayed");
            _rewardFulfilled = false;
        }

        private void HandleRewardedAdClicked(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsReward", "Clicked");
        }

        private void HandleRewardedAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsReward", "RevenuePaid");
            SendRevenueTracking(info, AdsType.Rewarded);
        }

        private void HandleRewardedAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            SendTrackingAction("AdsReward", "FailedToLoad");
        }

        private void HandleRewardedAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsReward", "Loaded");
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Rewarded Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #region Interstitial Ad Callbacks

        private void HandleInterstitialAdHidden(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "Hidden");
            InvokeAdsCallbackOnce(ref _interstitialCallback, _intersitialFulfilled);
        }

        private void HandleInterstitialAdDisplayFailed(string arg1, MaxSdkBase.ErrorInfo info1, MaxSdkBase.AdInfo info2)
        {
            SendTrackingAction("AdsInter", "DisplayFailed");
            InvokeAdsCallbackOnce(ref _interstitialCallback, false);
        }

        private void HandleInterstitialAdDisplayed(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "Displayed");
            _intersitialFulfilled = false;
        }

        private void HandleInterstitialAdClicked(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "Clicked");
        }

        private void HandleInterstitialAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "RevenuePaid");
            SendRevenueTracking(info, AdsType.Interstitial);
            _intersitialFulfilled = true;
        }

        private void HandleInterstitialAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            SendTrackingAction("AdsInter", "FailedToLoad");
        }

        private void HandleInterstitialAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "Loaded");
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Interstitial Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #region Banner Ad Callbacks

        private void HandleBannerAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsBanner", "RevenuePaid");
            SendRevenueTracking(info, AdsType.Banner);
        }

        private void HandleBannerAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            SendTrackingAction("AdsBanner", "FailedToLoad");
            IsBannerAvailable = false;
        }

        private void HandleBannerAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsBanner", "Loaded");
            IsBannerAvailable = true;
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Banner Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #region App Open Ad Callbacks

        private void HandleAppOpenAdHidden(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsAppOpen", "Hidden");
        }

        private void HandleAppOpenAdDisplayFailed(string arg1, MaxSdkBase.ErrorInfo info1, MaxSdkBase.AdInfo info2)
        {
            SendTrackingAction("AdsAppOpen", "DisplayFailed");
        }

        private void HandleAppOpenAdDisplayed(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsAppOpen", "Displayed");
        }

        private void HandleAppOpenAdClicked(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsAppOpen", "Clicked");
        }

        private void HandleAppOpenAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsAppOpen", "RevenuePaid");
            SendRevenueTracking(info, AdsType.OpenApp);
        }

        private void HandleAppOpenAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            SendTrackingAction("AdsAppOpen", "FailedToLoad");
        }

        private void HandleAppOpenAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsAppOpen", "Loaded");
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Open App Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #endregion
    }
}

#endif