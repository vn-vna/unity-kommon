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
        public ITrackingContextProvider TrackingContextProvider { get; set; }

        public bool IsInterstitialAvailable
        {
            get
            {
                if (!IsInitialized) return false;
                return IsAnyInterstitialReady();
            }
        }

        public bool IsRewardedAvailable
        {
            get
            {
                if (!IsInitialized) return false;
                return IsAnyRewardedReady();
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

        private string _lastInterstitialPlacement;
        private string _lastRewardPlacement;
        private string _lastBannerPlacement;
        private string _lastAppOpenPlacement;

        private int _interstitialTierIndex = -1;
        private int _rewardedTierIndex = -1;

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
            _lastBannerPlacement = "ad_banner";
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
            _lastInterstitialPlacement = placement;

            string unitId = GetBestInterstitial();

            if (unitId == null)
            {
                callback?.Invoke(false);
                return false;
            }

            _intersitialFulfilled = false;

            MaxSdk.ShowInterstitial(unitId, placement);
            _interstitialCallback = callback;

            return true;
        }
        private string GetBestInterstitial()
        {
            foreach (var id in Configuration.InterstitialUnitIds)
            {
                if (MaxSdk.IsInterstitialReady(id))
                    return id;
            }
            return null;
        }

        public bool ShowRewardAds(Action<bool> callback, string placement)
        {
            _lastRewardPlacement = placement;

            if (!IsInitialized)
            {
                callback?.Invoke(false);
                return false;
            }

            string rewardedUnitId = GetBestRewarded();

            if (rewardedUnitId != null)
            {
                _rewardFulfilled = false;

                try
                {
                    MaxSdk.ShowRewardedAd(rewardedUnitId, placement);
                    _rewardedCallback = callback;

                    SendAdsCallShowTrackingEvent(AdsType.Rewarded, placement);
                    return true;
                }
                catch (Exception ex)
                {
                    QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                        $"ShowRewarded failed: {ex.Message}"
                    );
                }
            }

            string interstitialUnitId = GetBestInterstitial();

            if (interstitialUnitId != null)
            {
                try
                {
                    MaxSdk.ShowInterstitial(interstitialUnitId, placement);
                    _interstitialCallback = callback;

                    SendAdsCallShowTrackingEvent(AdsType.Rewarded, placement);
                    return true;
                }
                catch (Exception ex)
                {
                    QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                        $"Fallback interstitial failed: {ex.Message}"
                    );
                }
            }

            QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                "No rewarded or interstitial available"
            );

            callback?.Invoke(false);
            return false;
        }

        private string GetBestRewarded()
        {
            foreach (var id in Configuration.RewardedUnitIds)
            {
                if (MaxSdk.IsRewardedAdReady(id))
                    return id;
            }
            return null;
        }

        #endregion

        #region Private Methods

        private bool IsAnyInterstitialReady()
        {
            foreach (var id in Configuration.InterstitialUnitIds)
            {
                if (MaxSdk.IsInterstitialReady(id))
                    return true;
            }
            return false;
        }
        private bool IsAnyRewardedReady()
        {
            foreach (var id in Configuration.RewardedUnitIds)
            {
                if (MaxSdk.IsRewardedAdReady(id))
                    return true;
            }
            return false;
        }

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
                    },
                    Severity = ActionSeverity.Debug
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
            if (!Configuration.EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Interstitial))
                return;

            if (IsAnyInterstitialReady())
                return;

            if (_interstitialTierIndex >= 0)
                return;

            TryLoadInterstitialTier(0);
        }

        private void TryLoadInterstitialTier(int index)
        {
            var unitIds = Configuration.InterstitialUnitIds;

            if (unitIds == null || unitIds.Length == 0)
                return;

            if (index >= unitIds.Length)
            {
                int lastIndex = unitIds.Length - 1;

                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "[Waterfall] Interstitial fallback → retry DEFAULT"
                );

                _interstitialTierIndex = -1;

                LoadWithDelay(() => TryLoadInterstitialTier(lastIndex), 1.5f);
                return;
            }

            string unitId = unitIds[index];

            if (MaxSdk.IsInterstitialReady(unitId))
            {
                _interstitialTierIndex = -1;
                return;
            }

            _interstitialTierIndex = index;

            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                $"[Waterfall] Loading Interstitial tier {index} | {unitId}"
            );

            MaxSdk.LoadInterstitial(unitId);

            LoadWithDelay(() =>
            {
                if (_interstitialTierIndex == index)
                {
                    QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                        $"[Waterfall] TIMEOUT Interstitial tier {index} → next"
                    );

                    _interstitialTierIndex = -1;
                    TryLoadInterstitialTier(index + 1);
                }
            }, 5.0f);
        }

        private void LoadRewardedAds()
        {
            if (!Configuration.EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Rewarded))
                return;

            if (IsAnyRewardedReady())
                return;

            if (_rewardedTierIndex >= 0)
                return;

            TryLoadRewardedTier(0);
        }

        private void TryLoadRewardedTier(int index)
        {
            var unitIds = Configuration.RewardedUnitIds;

            if (unitIds == null || unitIds.Length == 0)
                return;

            if (index >= unitIds.Length)
            {
                int lastIndex = unitIds.Length - 1;

                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "[Waterfall] Rewarded fallback → retry DEFAULT"
                );

                _rewardedTierIndex = -1;

                LoadWithDelay(() => TryLoadRewardedTier(lastIndex), 1.5f);
                return;
            }

            string unitId = unitIds[index];

            if (MaxSdk.IsRewardedAdReady(unitId))
            {
                _rewardedTierIndex = -1;
                return;
            }

            _rewardedTierIndex = index;

            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                $"[Waterfall] Loading Rewarded tier {index} | {unitId}"
            );

            try
            {
                MaxSdk.LoadRewardedAd(unitId);
            }
            catch (Exception ex)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                    $"[Waterfall] Rewarded exception tier {index} | {ex.Message}"
                );

                _rewardedTierIndex = -1;

                TryLoadRewardedTier(index + 1);
            }

            LoadWithDelay(() =>
            {
                if (_rewardedTierIndex == index)
                {
                    QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                        $"[Waterfall] TIMEOUT Rewarded tier {index} → next"
                    );

                    _rewardedTierIndex = -1;
                    TryLoadRewardedTier(index + 1);
                }
            }, 5.0f);
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
                    ActionId = $"{kind}_{action}",
                    Severity = ActionSeverity.Debug,
                });
        }

        //private void SendAdImpressionTracking(MaxSdkBase.AdInfo adInfo, AdsType type)
        //{
        //    if (Integration.TrackingManager == null)
        //        return;

        //    int stage = TrackingContextProvider?.GetStageNumber() ?? 0;
        //    string placement = GetPlacement(type);

        //    var parameters = new Dictionary<string, object>
        //    {
        //        { "stage_number", stage },
        //        { "ad_platform", "AppLovin" },
        //        { "ad_source", adInfo.NetworkName },
        //        { "ad_format", adInfo.AdFormat },
        //        { "ad_unit_name", adInfo.AdUnitIdentifier },
        //        { "placement", placement },
        //        { "value", adInfo.Revenue },
        //        { "currency", "USD" },
        //        { "precision", adInfo.RevenuePrecision }
        //    };

        //    Integration.TrackingManager.TrackAction(new TrackingActionInfo
        //    {
        //        ActionId = "ad_impression", 
        //        Parameters = parameters,
        //        Severity = ActionSeverity.Info
        //    });
        //}

        private void SendRevenueTracking(MaxSdkBase.AdInfo info, AdsType type)
        {
            int stage = TrackingContextProvider?.GetStageNumber() ?? 0;
            string placement = GetPlacement(type);

            Integration.TrackingManager?.TrackAdRevenue(new AdTrackingInfo
            {
                Provider = this,

                NetworkName = info.NetworkName,        
                AdType = type,
                RevenueUnit = info.AdUnitIdentifier,    
                Placement = placement,
                Revenue = info.Revenue,
                AdFormat = info.AdFormat,
                CreativeIdentifier = info.CreativeIdentifier,
                Country = MaxSdk.GetSdkConfiguration().CountryCode,

                CustomParams = new Dictionary<string, object>
                {
                    { "stage_number", stage },
                    { "precision", info.RevenuePrecision }
                }
            });
        }

        private string GetPlacement(AdsType type)
        {
            return type switch
            {
                AdsType.Interstitial => _lastInterstitialPlacement,
                AdsType.Rewarded => _lastRewardPlacement,
                AdsType.Banner => _lastBannerPlacement,
                AdsType.OpenApp => _lastAppOpenPlacement,
                _ => "unknown"
            };
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
            //SendAdImpressionTracking(info, AdsType.Rewarded);
            SendRevenueTracking(info, AdsType.Rewarded);
        }

        private void HandleRewardedAdFailedToLoad(string adUnitId, MaxSdkBase.ErrorInfo info)
        {
            SendTrackingAction("AdsReward", "FailedToLoad");

            var unitIds = Configuration.RewardedUnitIds;
            int failedIndex = Array.IndexOf(unitIds, adUnitId);

            QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                $"[Waterfall] Rewarded FAILED | tier={failedIndex} | unitId={adUnitId}"
            );

            if (failedIndex == _rewardedTierIndex)
            {
                _rewardedTierIndex = -1;
                TryLoadRewardedTier(failedIndex + 1);
            }
        }

        private void HandleRewardedAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsReward", "Loaded");
            _rewardedTierIndex = -1;
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
            _intersitialFulfilled = true;
        }

        private void HandleInterstitialAdClicked(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "Clicked");
            _intersitialFulfilled = true;
        }

        private void HandleInterstitialAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "RevenuePaid");
            //SendAdImpressionTracking(info, AdsType.Interstitial);
            SendRevenueTracking(info, AdsType.Interstitial);
            _intersitialFulfilled = true;
        }

        private void HandleInterstitialAdFailedToLoad(string adUnitId, MaxSdkBase.ErrorInfo info)
        {
            SendTrackingAction("AdsInter", "FailedToLoad");

            var unitIds = Configuration.InterstitialUnitIds;
            int failedIndex = Array.IndexOf(unitIds, adUnitId);

            if (failedIndex == _interstitialTierIndex)
            {
                _interstitialTierIndex = -1;
                TryLoadInterstitialTier(failedIndex + 1);
            }
        }

        private void HandleInterstitialAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "Loaded");
            _interstitialTierIndex = -1;
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Interstitial Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #region Banner Ad Callbacks

        private void HandleBannerAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsBanner", "RevenuePaid");
            //SendAdImpressionTracking(info, AdsType.Banner);
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
            //SendAdImpressionTracking(info, AdsType.OpenApp);
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

        private async void LoadWithDelay(Action action, float delay)
        {
            await System.Threading.Tasks.Task.Delay((int)(delay * 1000));

            Dispatcher.DispatchOnMainThread(() =>
            {
                action?.Invoke();
            });
        }
        #endregion
    }
}

#endif