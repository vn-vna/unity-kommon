#if APPLOVIN_MAX

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.MappedList;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    [CreateAssetMenu(
        fileName = "ApplovinMaxAdsServiceProvider",
        menuName = "Scheherazade/Ads Service Providers/Applovin Max"
    )]
    public class ApplovinMaxAdsServiceProvider :
        ScriptableObject,
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
                if (!UnitIdsMapping.TryGetValue(AdsType.Interstitial, out var unitId)
                    || string.IsNullOrEmpty(unitId.UnitId))
                    return false;
                return MaxSdk.IsInterstitialReady(unitId.UnitId);
            }
        }

        public bool IsRewardedAvailable
        {
            get
            {
                if (!IsInitialized) return false;
                if (!UnitIdsMapping.TryGetValue(AdsType.Rewarded, out var unitId)
                    || string.IsNullOrEmpty(unitId.UnitId))
                    return false;
                return MaxSdk.IsRewardedAdReady(unitId.UnitId);
            }
        }

        public bool IsOpenAppAdAvailable
        {
            get
            {
                if (!IsInitialized) return false;
                if (!UnitIdsMapping.TryGetValue(AdsType.OpenApp, out var unitId)
                    || string.IsNullOrEmpty(unitId.UnitId))
                    return false;
                return MaxSdk.IsAppOpenAdReady(unitId.UnitId);
            }
        }

        public bool IsBannerAvailable { get; private set; }
        public bool IsTestAds => isTestAds;
        public ApplovinMaxAdsEnabledAds EnabledAds => enabledAds;
        public BannerAdsPosition BannerAdPosition => bannerAdsDisplayPosition;
        public bool IsBannerAutoSized => bannerAutoSized;
        public Color BannerBackgroundColor => bannerBackgroundColor;

        public MappedList<AdsType, ApplovinMaxAdsUnitId> UnitIdsMapping
        {
            get
            {
                if (_unitIdsMapping == null)
                {
                    _unitIdsMapping = new Lazy<MappedList<AdsType, ApplovinMaxAdsUnitId>>(ConstructMappedList);
                }
                return _unitIdsMapping.Value;
            }
        }

        private Lazy<MappedList<AdsType, ApplovinMaxAdsUnitId>> _unitIdsMapping;

        [SerializeField]
        private bool isTestAds;

        [SerializeField]
        private ApplovinMaxAdsEnabledAds enabledAds;

        [SerializeField]
        private ApplovinMaxAdsUnitId[] unitIds;

        [SerializeField]
        private BannerAdsPosition bannerAdsDisplayPosition;

        [SerializeField]
        private bool bannerAutoSized = true;

        [SerializeField]
        private Color bannerBackgroundColor = Color.black;

        [SerializeField]
        private RetryStrategyConfig openAppRetryConfig = new()
        {
            strategy = RetryStrategy.FixedInterval,
            baseInterval = 5f,
            maxInterval = 60f,
            jitterFactor = 0.2f,
            timeout = 30f
        };

        [SerializeField]
        private RetryStrategyConfig interstitialRetryConfig = new()
        {
            strategy = RetryStrategy.FixedInterval,
            baseInterval = 5f,
            maxInterval = 60f,
            jitterFactor = 0.2f,
            timeout = 30f
        };

        [SerializeField]
        private RetryStrategyConfig rewardedRetryConfig = new()
        {
            strategy = RetryStrategy.FixedInterval,
            baseInterval = 5f,
            maxInterval = 60f,
            jitterFactor = 0.2f,
            timeout = 30f
        };

        [SerializeField]
        private RetryStrategyConfig bannerRetryConfig = new()
        {
            strategy = RetryStrategy.FixedInterval,
            baseInterval = 5f,
            maxInterval = 60f,
            jitterFactor = 0.2f,
            timeout = 30f
        };

        private MappedList<AdsType, ApplovinMaxAdsUnitId> ConstructMappedList()
            => new MappedList<AdsType, ApplovinMaxAdsUnitId>(
                unitIds, (uid) => uid.Type
            );

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EditorRefreshOnLoad()
        {
            var configurations = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(ApplovinMaxAdsServiceProvider)}")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<ApplovinMaxAdsServiceProvider>)
                .Where(asset => asset != null);

            foreach (var config in configurations)
            {
                config._unitIdsMapping = null;
            }
        }
#endif


        #endregion

        #region Private Fields

        private bool _interstitialFulfilled;
        private bool _rewardFulfilled;
        private Action<bool> _interstitialCallback;
        private Action<bool> _rewardedCallback;
        private bool _bannerAutoRefreshing;
        private bool _isBannerCreated;

        private RetryHandle _openAppHandle;
        private RetryHandle _interstitialHandle;
        private RetryHandle _rewardedHandle;
        private RetryHandle _bannerHandle;
        private int _openAppLoadGen;
        private int _interstitialLoadGen;
        private int _rewardedLoadGen;
        private int _bannerLoadGen;

        #endregion

        #region Public Methods

        public void Initialize()
        {
            CleanUp();

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
            _interstitialFulfilled = false;
            _rewardFulfilled = false;
            _interstitialCallback = null;
            _rewardedCallback = null;
            _bannerAutoRefreshing = false;
            IsBannerAvailable = false;

            _openAppHandle?.Cancel();
            _interstitialHandle?.Cancel();
            _rewardedHandle?.Cancel();
            _bannerHandle?.Cancel();

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

            if (!IsInitialized)
            {
                return;
            }

            IsInitialized = false;
        }

        public void LoadAds()
        {
        }

        public bool ShowAppOpenAds(Action<bool> callback, string placement)
        {
            return false;
        }

        public void ShowBanner()
        {
            if (!IsInitialized)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>("Cannot show banner: SDK is not initialized.");
                return;
            }

            if (!EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Banner))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>("Cannot show banner: Banner ads are disabled.");
                return;
            }

            if (!UnitIdsMapping.TryGetValue(AdsType.Banner, out var unitId)
                || string.IsNullOrEmpty(unitId.UnitId))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>("Cannot show banner: Banner ad unit ID is not set.");
                return;
            }

            try
            {
                MaxSdk.ShowBanner(unitId.UnitId);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>($"Failed to show banner ad: {ex.Message}");
            }
        }

        public void HideBanner()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (!UnitIdsMapping.TryGetValue(AdsType.Banner, out var unitId)
                || string.IsNullOrEmpty(unitId.UnitId))
            {
                return;
            }

            try
            {
                MaxSdk.HideBanner(unitId.UnitId);
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

            if (!UnitIdsMapping.TryGetValue(AdsType.Interstitial, out var unitId)
                || string.IsNullOrEmpty(unitId.UnitId))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>("Interstitial ad unit ID is not set.");
                callback?.Invoke(false);
                return false;
            }

            try
            {
                _interstitialFulfilled = false;
                MaxSdk.ShowInterstitial(
                    unitId.UnitId,
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
                        UnitIdsMapping[AdsType.Rewarded].UnitId,
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
                        UnitIdsMapping[AdsType.Interstitial].UnitId,
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
            if (IsTestAds)
            {
                MaxSdk.ShowMediationDebugger();
            }

#if UNITY_IOS
            if (MaxSdkUtils.CompareVersions(UnityEngine.iOS.Device.systemVersion, "14.5") != MaxSdkUtils.VersionComparisonResult.Lesser)
            {
                SetupAudienceNetwork(configuration);
            }
#endif

            IsInitialized = true;

            _openAppHandle = RetryableActionInvoker.Schedule(h =>
            {
                _openAppLoadGen = h.Generation;
                LoadOpenAppAds();
            }, openAppRetryConfig);

            _interstitialHandle = RetryableActionInvoker.Schedule(h =>
            {
                _interstitialLoadGen = h.Generation;
                LoadInterstitialAds();
            }, interstitialRetryConfig);

            _rewardedHandle = RetryableActionInvoker.Schedule(h =>
            {
                _rewardedLoadGen = h.Generation;
                LoadRewardedAds();
            }, rewardedRetryConfig);

            _bannerHandle = RetryableActionInvoker.Schedule(h =>
            {
                _bannerLoadGen = h.Generation;
                LoadBannerAds();
            }, bannerRetryConfig);

            _openAppHandle.Execute();
            _interstitialHandle.Execute();
            _rewardedHandle.Execute();
            _bannerHandle.Execute();
        }

        private static void SetupAudienceNetwork(MaxSdkBase.SdkConfiguration configuration)
        {
#if UNITY_IOS
            bool isTrackingEnabled = configuration.AppTrackingStatus == MaxSdk.AppTrackingStatus.Authorized;
            Type adSettingsClazz = AppDomain.CurrentDomain.GetAssemblies()
                .Select(asm => asm.GetType("AdSettings"))
                .FirstOrDefault(clz => clz != null && "AudienceNetwork.AdSettings".Equals(clz.FullName));

            if (adSettingsClazz == null)
            {
                QuickLog.Critical<ApplovinMaxAdsServiceProvider>(
                    "Cannot setup Audience Network: class AudienceNetwork.AdSettings not found!"
                );
                return;
            }

            MethodInfo method = adSettingsClazz.GetMethod("SetAdvertiserTrackingEnabled");
            if (method == null)
            {
                QuickLog.Critical<ApplovinMaxAdsServiceProvider>(
                    "Cannot setup Audience Network: method AudienceNetwork.AdSettings.SetAdvertiserTrackingEnabled not found"
                );
                return;
            }

            method.Invoke(null, new object[] { isTrackingEnabled });
#endif
        }

        private void CreateBanner()
        {
            try
            {
                MaxSdk.CreateBanner(
                    UnitIdsMapping[AdsType.Banner].UnitId,
                    AcquireBannerAdsPosition(BannerAdPosition)
                );

                string autoSized = IsBannerAutoSized ? "true" : "false";
                MaxSdk.SetBannerExtraParameter(
                    UnitIdsMapping[AdsType.Banner].UnitId,
                    "adaptive_banner",
                    autoSized
                );

                MaxSdk.SetBannerBackgroundColor(
                    UnitIdsMapping[AdsType.Banner].UnitId,
                    BannerBackgroundColor
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                    "Failed to create banner ad: {0}",
                    ex.Message
                );
            }
        }

        private void LoadOpenAppAds()
        {
            if (IsOpenAppAdAvailable) return;

            if (!EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.OpenApp)) return;

            if (!UnitIdsMapping.ContainsKey(AdsType.OpenApp)
                || string.IsNullOrEmpty(UnitIdsMapping[AdsType.OpenApp].UnitId))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "Open App ad unit ID is not set. Please check the configuration."
                );
                return;
            }

            try
            {
                MaxSdk.LoadAppOpenAd(UnitIdsMapping[AdsType.OpenApp].UnitId);
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
            if (IsInterstitialAvailable) return;

            if (!EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Interstitial)) return;

            if (!UnitIdsMapping.ContainsKey(AdsType.Interstitial)
                || string.IsNullOrEmpty(UnitIdsMapping[AdsType.Interstitial].UnitId))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "Interstitial ad unit ID is not set. Please check the configuration."
                );
                return;
            }

            try
            {
                MaxSdk.LoadInterstitial(UnitIdsMapping[AdsType.Interstitial].UnitId);
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

            if (!EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Rewarded)) return;

            if (!UnitIdsMapping.ContainsKey(AdsType.Rewarded)
                || string.IsNullOrEmpty(UnitIdsMapping[AdsType.Rewarded].UnitId))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "Rewarded ad unit ID is not set. Please check the configuration."
                );
                return;
            }

            try
            {
                MaxSdk.LoadRewardedAd(UnitIdsMapping[AdsType.Rewarded].UnitId);
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
            if (_bannerAutoRefreshing) return;

            if (IsBannerAvailable) return;

            if (!EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Banner)) return;

            if (!UnitIdsMapping.TryGetValue(AdsType.Banner, out var bannerUnitId)
                || string.IsNullOrEmpty(bannerUnitId.UnitId))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "Banner ad unit ID is not set. Please check the configuration."
                );
                return;
            }

            if (!_isBannerCreated)
            {
                CreateBanner();
                _isBannerCreated = true;
            }

            try
            {
                MaxSdk.LoadBanner(bannerUnitId.UnitId);
                MaxSdk.StartBannerAutoRefresh(bannerUnitId.UnitId);
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

        private void SendRevenueTracking(MaxSdkBase.AdInfo info, AdsType type)
        {
            Integration.TrackingManager
                ?.TrackAdRevenue(new AdTrackingInfo
                {
                    Provider = this,
                    NetworkName = "Applovin",
                    AdType = type,
                    RevenueUnit = UnitIdsMapping[type].UnitId,
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
            SendTrackingAction("AdsReward", "RewardReceived");
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
            _rewardedHandle.Execute();
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
            _rewardedHandle.Fail(_rewardedLoadGen);
            SendTrackingAction("AdsReward", "FailedToLoad");
        }

        private void HandleRewardedAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            _rewardedHandle.Complete(_rewardedLoadGen);
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
            InvokeAdsCallbackOnce(ref _interstitialCallback, _interstitialFulfilled);
        }

        private void HandleInterstitialAdDisplayFailed(string arg1, MaxSdkBase.ErrorInfo info1, MaxSdkBase.AdInfo info2)
        {
            SendTrackingAction("AdsInter", "DisplayFailed");
            InvokeAdsCallbackOnce(ref _interstitialCallback, false);
        }

        private void HandleInterstitialAdDisplayed(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "Displayed");
            _interstitialFulfilled = true;
            _interstitialHandle.Execute();
        }

        private void HandleInterstitialAdClicked(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "Clicked");
            _interstitialFulfilled = true;
        }

        private void HandleInterstitialAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsInter", "RevenuePaid");
            SendRevenueTracking(info, AdsType.Interstitial);
            _interstitialFulfilled = true;
        }

        private void HandleInterstitialAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            _interstitialHandle.Fail(_interstitialLoadGen);
            SendTrackingAction("AdsInter", "FailedToLoad");
        }

        private void HandleInterstitialAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            _interstitialHandle.Complete(_interstitialLoadGen);
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
            _bannerAutoRefreshing = false;
            SendTrackingAction("AdsBanner", "FailedToLoad");
            IsBannerAvailable = false;
            _bannerHandle.Fail(_bannerLoadGen);
        }

        private void HandleBannerAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsBanner", "Loaded");
            IsBannerAvailable = true;
            _bannerHandle.Complete(_bannerLoadGen);
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Banner Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #region App Open Ad Callbacks

        private void HandleAppOpenAdHidden(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingAction("AdsAppOpen", "Hidden");
            _openAppHandle.Execute();
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
            _openAppHandle.Fail(_openAppLoadGen);
            SendTrackingAction("AdsAppOpen", "FailedToLoad");
        }

        private void HandleAppOpenAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            _openAppHandle.Complete(_openAppLoadGen);
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