#if APPLOVIN_MAX

using System;
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
    public partial class ApplovinMaxAdsServiceProvider :
        ScriptableObject,
        IAdsServiceProvider
    {
        #region Interfaces & Properties

        // Do not expose an identifier before an explicit consent-safe policy exists.
        public string DeviceAdvertisingId => string.Empty;

        public IAdsManager AdsManager { get; set; }
        public bool IsInitialized { get; private set; }

        public bool IsInterstitialAvailable
        {
            get
            {
                if (!IsInitialized || _fullscreenChannelQuarantined ||
                    (_activeFullscreen != null && !_activeFullscreen.Handler.IsTerminal)) return false;
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
                if (!IsInitialized || _fullscreenChannelQuarantined ||
                    (_activeFullscreen != null && !_activeFullscreen.Handler.IsTerminal)) return false;
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
                if (!IsInitialized || _fullscreenChannelQuarantined ||
                    (_activeFullscreen != null && !_activeFullscreen.Handler.IsTerminal)) return false;
                if (!UnitIdsMapping.TryGetValue(AdsType.OpenApp, out var unitId)
                    || string.IsNullOrEmpty(unitId.UnitId))
                    return false;
                return MaxSdk.IsAppOpenAdReady(unitId.UnitId);
            }
        }

        public bool IsBannerAvailable { get; private set; }
        public AdsBannerState BannerState { get; private set; } =
            AdsBannerState.Unavailable("Banner has not loaded.");
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

        public MappedList<ApplovinMaxAdsTrackingEventType, ApplovinMaxAdsTrackingEventConfig> TrackingEventsMapping
        {
            get
            {
                if (_trackingEventsMapping == null)
                {
                    _trackingEventsMapping = new Lazy<MappedList<ApplovinMaxAdsTrackingEventType, ApplovinMaxAdsTrackingEventConfig>>(ConstructTrackingEventsMappedList);
                }
                return _trackingEventsMapping.Value;
            }
        }

        private Lazy<MappedList<ApplovinMaxAdsTrackingEventType, ApplovinMaxAdsTrackingEventConfig>> _trackingEventsMapping;

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
        private ApplovinMaxAdsTrackingEventConfig[] trackingEvents;

        [SerializeField]
        private bool allowSdkInEditor;

        [SerializeField, Min(1)]
        private float showStartTimeoutSeconds = 15;

        [SerializeField, Min(1)]
        private float requestTimeoutSeconds = 300;

        [SerializeField, Min(1)]
        private float rewardCallbackTimeoutSeconds = 10;

        [SerializeField]
        private string[] testDeviceAdvertisingIdentifiers = Array.Empty<string>();

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

        private MappedList<ApplovinMaxAdsTrackingEventType, ApplovinMaxAdsTrackingEventConfig> ConstructTrackingEventsMappedList()
            => new MappedList<ApplovinMaxAdsTrackingEventType, ApplovinMaxAdsTrackingEventConfig>(
                trackingEvents ?? Array.Empty<ApplovinMaxAdsTrackingEventConfig>(),
                (config) => config.Type
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
                config._trackingEventsMapping = null;
            }
        }
#endif


        #endregion

        #region Private Fields

        private bool _subscribed;
        private bool _bannerAutoRefreshing;
        private bool _isBannerCreated;
        private bool _bannerVisibleRequested;

        private RetryHandle _openAppHandle;
        private RetryHandle _interstitialHandle;
        private RetryHandle _rewardedHandle;
        private RetryHandle _bannerHandle;
        private int _openAppLoadGen;
        private int _interstitialLoadGen;
        private int _rewardedLoadGen;
        private int _bannerLoadGen;
        private static bool _fullscreenChannelQuarantined;
        private FullscreenInvocation _activeFullscreen;

        #endregion

        #region Public Methods

        public void Initialize()
        {
            CleanUp();
#if UNITY_EDITOR
            if (!allowSdkInEditor)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "AppLovin MAX is disabled in the Editor for this provider."
                );
                return;
            }
#endif
            SubscribeCallbacks();
            try
            {
                string[] identifiers = (testDeviceAdvertisingIdentifiers ?? Array.Empty<string>())
                    .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
                    .Select(identifier => identifier.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                MaxSdk.SetTestDeviceAdvertisingIdentifiers(identifiers);

                if (MaxSdk.IsInitialized())
                    HandleMaxSdkInitializedEvents(MaxSdk.GetSdkConfiguration());
                else
                    MaxSdk.InitializeSdk();
            }
            catch (Exception exception)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                    "Failed to initialize AppLovin MAX: {0}",
                    exception.Message
                );
                CleanUp();
            }
        }

        public void CleanUp()
        {
            // Mark unavailable before terminal observers can reenter the provider.
            IsInitialized = false;
            IsBannerAvailable = false;
            _bannerVisibleRequested = false;
            BannerState = AdsBannerState.Unavailable("Ads provider was cleaned up.");
            TerminateActiveInvocationForCleanup();
            _bannerAutoRefreshing = false;

            _openAppHandle?.Cancel();
            _interstitialHandle?.Cancel();
            _rewardedHandle?.Cancel();
            _bannerHandle?.Cancel();
            _openAppHandle = null;
            _interstitialHandle = null;
            _rewardedHandle = null;
            _bannerHandle = null;

            UnsubscribeCallbacks();
        }

        public void LoadAds()
        {
            AdvanceFullscreenInvocation(Time.unscaledDeltaTime);
        }

        public AdsInvocationHandler ShowAppOpenAds(string placement)
        {
            if (!IsInitialized || !IsOpenAppAdAvailable)
                return AdsInvocationHandler.Failed(AdsType.OpenApp, placement, "App-open ads are not available.");
            if (!UnitIdsMapping.TryGetValue(AdsType.OpenApp, out var unitId) || string.IsNullOrEmpty(unitId.UnitId))
                return AdsInvocationHandler.Failed(AdsType.OpenApp, placement, "App-open ad unit ID is not set.");

            AdsInvocationHandler handler = BeginFullscreenInvocation(AdsType.OpenApp, placement, unitId.UnitId);
            if (handler.IsTerminal) return handler;
            try
            {
                MaxSdk.ShowAppOpenAd(unitId.UnitId, placement);
                SendAdsCallShowTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenCallShow, placement);
            }
            catch (Exception exception)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>("Failed to show app-open ad: {0}", exception.Message);
                FailInvocationStart(handler, "Failed to start app-open ad: " + exception.Message);
            }
            return handler;
        }

        public AdsInvocationHandler ShowBanner()
        {
            var handler = new AdsInvocationHandler(AdsType.Banner);
            if (!IsInitialized || !EnabledAds.HasFlag(ApplovinMaxAdsEnabledAds.Banner) ||
                !IsBannerAvailable || !BannerState.IsAvailable)
            {
                handler.CompleteFailed("Banner ads are not available.");
                return handler;
            }
            if (!UnitIdsMapping.TryGetValue(AdsType.Banner, out var unitId) || string.IsNullOrEmpty(unitId.UnitId))
            {
                handler.CompleteFailed("Banner ad unit ID is not set.");
                return handler;
            }
            try
            {
                MaxSdk.ShowBanner(unitId.UnitId);
                _bannerVisibleRequested = true;
                UpdateBannerState(AdsBannerStatus.Showing, "Banner show command accepted.", true);
                handler.MarkShowing();
                handler.CompleteSucceeded("Banner show command accepted.");
            }
            catch (Exception exception)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>("Failed to show banner ad: {0}", exception.Message);
                handler.CompleteFailed("Failed to show banner: " + exception.Message);
            }
            return handler;
        }

        public AdsInvocationHandler HideBanner()
        {
            var handler = new AdsInvocationHandler(AdsType.Banner);
            if (!IsInitialized || !IsBannerAvailable || !BannerState.IsAvailable)
            {
                handler.CompleteFailed("Banner ads are not available.");
                return handler;
            }
            if (!UnitIdsMapping.TryGetValue(AdsType.Banner, out var unitId) || string.IsNullOrEmpty(unitId.UnitId))
            {
                handler.CompleteFailed("Banner ad unit ID is not set.");
                return handler;
            }
            try
            {
                MaxSdk.HideBanner(unitId.UnitId);
                _bannerVisibleRequested = false;
                UpdateBannerState(AdsBannerStatus.Hidden, "Banner hide command accepted.", true);
                handler.CompleteSucceeded("Banner hide command accepted.", true);
            }
            catch (Exception exception)
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>("Failed to hide banner ad: {0}", exception.Message);
                handler.CompleteFailed("Failed to hide banner: " + exception.Message);
            }
            return handler;
        }

        public AdsInvocationHandler ShowInterstitialAds(string placement)
        {
            if (!IsInitialized || !IsInterstitialAvailable)
                return AdsInvocationHandler.Failed(AdsType.Interstitial, placement, "Interstitial ads are not available.");
            if (!UnitIdsMapping.TryGetValue(AdsType.Interstitial, out var unitId) || string.IsNullOrEmpty(unitId.UnitId))
                return AdsInvocationHandler.Failed(AdsType.Interstitial, placement, "Interstitial ad unit ID is not set.");

            AdsInvocationHandler handler = BeginFullscreenInvocation(AdsType.Interstitial, placement, unitId.UnitId);
            if (handler.IsTerminal) return handler;
            try
            {
                MaxSdk.ShowInterstitial(unitId.UnitId, placement);
                SendAdsCallShowTrackingEvent(ApplovinMaxAdsTrackingEventType.InterCallShow, placement);
            }
            catch (Exception exception)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>("Failed to show interstitial ad: {0}", exception.Message);
                FailInvocationStart(handler, "Failed to start interstitial ad: " + exception.Message);
            }
            return handler;
        }

        public AdsInvocationHandler ShowRewardAds(string placement)
        {
            if (!IsInitialized || !IsRewardedAvailable)
                return AdsInvocationHandler.Failed(AdsType.Rewarded, placement, "Rewarded ads are not available.");
            if (!UnitIdsMapping.TryGetValue(AdsType.Rewarded, out var unitId) || string.IsNullOrEmpty(unitId.UnitId))
                return AdsInvocationHandler.Failed(AdsType.Rewarded, placement, "Rewarded ad unit ID is not set.");

            AdsInvocationHandler handler = BeginFullscreenInvocation(AdsType.Rewarded, placement, unitId.UnitId);
            if (handler.IsTerminal) return handler;
            try
            {
                MaxSdk.ShowRewardedAd(unitId.UnitId, placement);
                SendAdsCallShowTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardCallShow, placement);
            }
            catch (Exception exception)
            {
                QuickLog.Error<ApplovinMaxAdsServiceProvider>("Failed to show rewarded ad: {0}", exception.Message);
                FailInvocationStart(handler, "Failed to start rewarded ad: " + exception.Message);
            }
            return handler;
        }

        #endregion

        #region Private Methods

        private void SendTrackingEvent(
            ApplovinMaxAdsTrackingEventType type,
            params (string key, object value)[] parameters)
        {
            if (!TryResolveTrackingEvent(type, out string actionId, out ActionSeverity severity))
            {
                return;
            }

            Integration.TrackingManager
                ?.TrackAction(new TrackingActionInfo
                {
                    ActionId = actionId,
                    Parameters = parameters.Length > 0
                        ? TrackingActionInfo.CreateParametersDictionary(parameters)
                        : null,
                    Severity = severity
                });
        }

        private bool TryResolveTrackingEvent(
            ApplovinMaxAdsTrackingEventType type,
            out string actionId,
            out ActionSeverity severity)
        {
            if (TrackingEventsMapping.TryGetValue(type, out var config))
            {
                if (!config.IsEnabled)
                {
                    actionId = null;
                    severity = default;
                    return false;
                }

                actionId = string.IsNullOrEmpty(config.ActionId)
                    ? ApplovinMaxAdsTrackingEventDefaults.Get(type).ActionId
                    : config.ActionId;
                severity = config.Severity;
                return true;
            }

            if (ApplovinMaxAdsTrackingEventDefaults.TryGet(type, out var defaults))
            {
                actionId = defaults.ActionId;
                severity = defaults.Severity;
                return true;
            }

            QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                "No tracking configuration or default found for event '{0}'.",
                type
            );

            actionId = null;
            severity = default;
            return false;
        }

        private void SendAdsCallShowTrackingEvent(
            ApplovinMaxAdsTrackingEventType type,
            string placement)
        {
            if (string.IsNullOrEmpty(placement))
            {
                QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                    "Placement for CallShow tracking is null or empty."
                );
                return;
            }

            SendTrackingEvent(type, ("placement", placement));
        }

        private void SubscribeCallbacks()
        {
            if (_subscribed) return;
            _subscribed = true;
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
        }

        private void UnsubscribeCallbacks()
        {
            if (!_subscribed) return;
            _subscribed = false;
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
        }

        private void HandleMaxSdkInitializedEvents(MaxSdkBase.SdkConfiguration configuration)
        {
            if (!_subscribed || IsInitialized) return;
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
                SendTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenCallLoad);
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
                SendTrackingEvent(ApplovinMaxAdsTrackingEventType.InterCallLoad);
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
                SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardCallLoad);
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
                BannerState = new AdsBannerState(AdsBannerStatus.Failed, BannerState.Size,
                    "Banner ad unit ID is not configured.");
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
                UpdateBannerState(AdsBannerStatus.Loading, "Banner load requested.");
                MaxSdk.LoadBanner(bannerUnitId.UnitId);
                MaxSdk.StartBannerAutoRefresh(bannerUnitId.UnitId);
                _bannerAutoRefreshing = true;
                SendTrackingEvent(ApplovinMaxAdsTrackingEventType.BannerCallLoad);
                QuickLog.Info<ApplovinMaxAdsServiceProvider>("Loading Banner Ad");
            }
            catch (Exception ex)
            {
                IsBannerAvailable = false;
                BannerState = new AdsBannerState(AdsBannerStatus.Failed, BannerState.Size,
                    "Banner load request failed: " + ex.Message);
                QuickLog.Error<ApplovinMaxAdsServiceProvider>(
                    "Failed to load banner ad: {0}",
                    ex.Message
                );
            }
        }

        private void UpdateBannerState(AdsBannerStatus status, string reason, bool refreshLayout = false)
        {
            Vector2 size = BannerState.Size;
            if (refreshLayout && UnitIdsMapping.TryGetValue(AdsType.Banner, out var unit) &&
                !string.IsNullOrEmpty(unit.UnitId))
            {
                try { size = MaxSdk.GetBannerLayout(unit.UnitId).size; }
                catch (Exception exception)
                {
                    QuickLog.Warning<ApplovinMaxAdsServiceProvider>(
                        "Could not read banner layout: {0}", exception.Message
                    );
                }
            }
            BannerState = new AdsBannerState(status, size, reason);
        }

        private bool IsConfiguredBannerUnit(string unitId) =>
            UnitIdsMapping.TryGetValue(AdsType.Banner, out var configured) &&
            string.Equals(configured.UnitId, unitId, StringComparison.Ordinal);

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFullscreenChannel() => _fullscreenChannelQuarantined = false;

        private AdsInvocationHandler BeginFullscreenInvocation(
            AdsType type,
            string placement,
            string unitId)
        {
            if (_fullscreenChannelQuarantined)
                return AdsInvocationHandler.Failed(type, placement,
                    "A previous fullscreen ad did not close safely; restart the app before showing another ad.");
            if (_activeFullscreen != null && !_activeFullscreen.Handler.IsTerminal)
            {
                return AdsInvocationHandler.Failed(
                    type,
                    placement,
                    "Another fullscreen ad invocation is already active."
                );
            }

            var handler = new AdsInvocationHandler(type, placement);
            _activeFullscreen = new FullscreenInvocation(handler, unitId);
            return handler;
        }

        private bool IsCurrentInvocation(AdsInvocationHandler handler) =>
            handler != null && ReferenceEquals(_activeFullscreen?.Handler, handler) && !handler.IsTerminal;

        private FullscreenInvocation GetActiveInvocation(AdsType type, string unitId, string eventPlacement = null)
        {
            FullscreenInvocation invocation = _activeFullscreen;
            if (invocation == null || invocation.Handler.IsTerminal || invocation.Handler.AdType != type)
                return null;
            if (!string.IsNullOrEmpty(unitId) &&
                !string.Equals(invocation.UnitId, unitId, StringComparison.Ordinal))
                return null;
            if (!string.IsNullOrEmpty(eventPlacement) &&
                !string.Equals(invocation.Handler.Placement, eventPlacement, StringComparison.Ordinal))
                return null;
            return invocation;
        }

        private void MarkFullscreenDisplayed(AdsType type, string unitId, string placement)
        {
            GetActiveInvocation(type, unitId, placement)?.Handler.MarkShowing();
        }

        private void MarkRewardEarned(string unitId, string placement)
        {
            FullscreenInvocation invocation = GetActiveInvocation(AdsType.Rewarded, unitId, placement);
            if (invocation == null) return;
            // Record economic credit before Showing can invoke reentrant user code.
            invocation.Handler.MarkRewardEarned();
            if (!invocation.Handler.WasDisplayed) invocation.Handler.MarkShowing();
            if (invocation.Hidden)
                CompleteInvocationSucceeded(invocation, "Reward earned after ad close.");
        }

        private void MarkFullscreenHidden(AdsType type, string unitId, string placement)
        {
            FullscreenInvocation invocation = GetActiveInvocation(type, unitId, placement);
            if (invocation == null) return;

            invocation.Hidden = true;
            invocation.RewardWaitElapsed = 0;
            invocation.SkipRewardWaitTick = true;
            invocation.Handler.MarkClosed();

            if (type != AdsType.Rewarded)
            {
                CompleteInvocationSucceeded(invocation, "Fullscreen ad closed.");
                return;
            }

            if (invocation.Handler.RewardEarned)
                CompleteInvocationSucceeded(invocation, "Reward earned and ad closed.");
        }

        private void FailFullscreenInvocation(AdsType type, string unitId, string placement, string reason)
        {
            FullscreenInvocation invocation = GetActiveInvocation(type, unitId, placement);
            if (invocation == null) return;
            if (invocation.Handler.RewardEarned)
                invocation.Handler.CompleteSucceeded(reason, invocation.Handler.WasClosed);
            else
                invocation.Handler.CompleteFailed(reason, invocation.Handler.WasClosed);
            ReleaseInvocation(invocation);
        }

        private void FailInvocationStart(AdsInvocationHandler handler, string reason)
        {
            if (!IsCurrentInvocation(handler)) return;
            handler.CompleteFailed(reason, handler.WasClosed);
            ReleaseInvocation(_activeFullscreen);
        }

        private void CompleteInvocationSucceeded(FullscreenInvocation invocation, string reason)
        {
            if (invocation == null || !IsCurrentInvocation(invocation.Handler)) return;
            invocation.Handler.CompleteSucceeded(reason, invocation.Handler.WasClosed);
            ReleaseInvocation(invocation);
        }

        private void CancelInvocation(FullscreenInvocation invocation, string reason)
        {
            if (invocation == null || !IsCurrentInvocation(invocation.Handler)) return;
            invocation.Handler.CompleteCancelled(reason, invocation.Handler.WasClosed);
            ReleaseInvocation(invocation);
        }

        private void ReleaseInvocation(FullscreenInvocation invocation)
        {
            if (!ReferenceEquals(_activeFullscreen, invocation)) return;
            _activeFullscreen = null;
            if (_fullscreenChannelQuarantined) return;
            switch (invocation.Handler.AdType)
            {
                case AdsType.Rewarded:
                    _rewardedHandle?.Execute();
                    break;
                case AdsType.Interstitial:
                    _interstitialHandle?.Execute();
                    break;
                case AdsType.OpenApp:
                    _openAppHandle?.Execute();
                    break;
            }
        }

        private void AdvanceFullscreenInvocation(float unscaledDeltaTime)
        {
            FullscreenInvocation invocation = _activeFullscreen;
            if (invocation == null || invocation.Handler.IsTerminal)
            {
                _activeFullscreen = null;
                return;
            }

            float delta = float.IsNaN(unscaledDeltaTime) || float.IsInfinity(unscaledDeltaTime)
                ? 0 : Mathf.Max(0, unscaledDeltaTime);
            invocation.Elapsed += delta;

            if (invocation.Hidden && invocation.Handler.AdType == AdsType.Rewarded &&
                !invocation.Handler.RewardEarned)
            {
                // The first frame after native close can include the whole video.
                // Start an independent grace clock on the following provider tick.
                if (invocation.SkipRewardWaitTick)
                {
                    invocation.SkipRewardWaitTick = false;
                    return;
                }
                invocation.RewardWaitElapsed += delta;
                if (invocation.RewardWaitElapsed >= rewardCallbackTimeoutSeconds)
                    CancelInvocation(invocation, "Reward callback did not arrive after the ad closed.");
                return;
            }

            if (!invocation.Handler.WasDisplayed && !invocation.Hidden &&
                invocation.Elapsed >= showStartTimeoutSeconds)
            {
                _fullscreenChannelQuarantined = true;
                invocation.Handler.CompleteFailed("The ad did not start before its timeout; native closure is unknown.");
                ReleaseInvocation(invocation);
                return;
            }

            if (invocation.Elapsed < requestTimeoutSeconds) return;
            if (!invocation.Hidden) _fullscreenChannelQuarantined = true;
            if (invocation.Handler.RewardEarned)
                CompleteInvocationSucceeded(invocation, "Reward was earned before the invocation timeout.");
            else
                CancelInvocation(invocation, "The fullscreen ad invocation timed out; native closure is unknown.");
        }

        private void TerminateActiveInvocationForCleanup()
        {
            FullscreenInvocation invocation = _activeFullscreen;
            if (invocation == null)
                return;

            if (!invocation.Hidden) _fullscreenChannelQuarantined = true;
            if (invocation.Handler.RewardEarned)
                invocation.Handler.CompleteSucceeded(
                    "Provider cleanup followed a confirmed reward.",
                    invocation.Handler.WasClosed
                );
            else
                invocation.Handler.CompleteCancelled(
                    "Provider was cleaned up before the invocation completed.",
                    invocation.Handler.WasClosed
                );

            _activeFullscreen = null;
        }

        #region Rewarded Ad Callbacks

        private void HandleRewardedAdReceivedReward(string arg1, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardRewardReceived);
            MarkRewardEarned(arg1, info?.Placement);
        }

        private void HandleRewardedAdHidden(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardHidden);
            MarkFullscreenHidden(AdsType.Rewarded, arg1, info?.Placement);
        }

        private void HandleRewardedAdDisplayFailed(string arg1, MaxSdkBase.ErrorInfo info1, MaxSdkBase.AdInfo info2)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardDisplayFailed);
            FailFullscreenInvocation(AdsType.Rewarded, arg1, info2?.Placement, "Rewarded ad display failed.");
        }

        private void HandleRewardedAdDisplayed(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardDisplayed);
            MarkFullscreenDisplayed(AdsType.Rewarded, arg1, info?.Placement);
        }

        private void HandleRewardedAdClicked(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardClicked);
        }

        private void HandleRewardedAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardRevenuePaid);
            SendRevenueTracking(info, AdsType.Rewarded);
        }

        private void HandleRewardedAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            _rewardedHandle?.Fail(_rewardedLoadGen);
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardFailedToLoad);
        }

        private void HandleRewardedAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            _rewardedHandle?.Complete(_rewardedLoadGen);
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.RewardLoaded);
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Rewarded Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #region Interstitial Ad Callbacks

        private void HandleInterstitialAdHidden(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.InterHidden);
            MarkFullscreenHidden(AdsType.Interstitial, arg1, info?.Placement);
        }

        private void HandleInterstitialAdDisplayFailed(string arg1, MaxSdkBase.ErrorInfo info1, MaxSdkBase.AdInfo info2)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.InterDisplayFailed);
            FailFullscreenInvocation(AdsType.Interstitial, arg1, info2?.Placement, "Interstitial ad display failed.");
        }

        private void HandleInterstitialAdDisplayed(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.InterDisplayed);
            MarkFullscreenDisplayed(AdsType.Interstitial, arg1, info?.Placement);
        }

        private void HandleInterstitialAdClicked(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.InterClicked);
        }

        private void HandleInterstitialAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.InterRevenuePaid);
            SendRevenueTracking(info, AdsType.Interstitial);
        }

        private void HandleInterstitialAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            _interstitialHandle?.Fail(_interstitialLoadGen);
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.InterFailedToLoad);
        }

        private void HandleInterstitialAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            _interstitialHandle?.Complete(_interstitialLoadGen);
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.InterLoaded);
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Interstitial Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #region Banner Ad Callbacks

        private void HandleBannerAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            if (!IsConfiguredBannerUnit(arg1)) return;
            UpdateBannerState(_bannerVisibleRequested ? AdsBannerStatus.Showing : AdsBannerStatus.Available,
                "Banner impression callback received.", true);
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.BannerRevenuePaid);
            SendRevenueTracking(info, AdsType.Banner);
        }

        private void HandleBannerAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            if (!IsConfiguredBannerUnit(arg1)) return;
            _bannerAutoRefreshing = false;
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.BannerFailedToLoad);
            IsBannerAvailable = false;
            BannerState = new AdsBannerState(AdsBannerStatus.Failed, BannerState.Size,
                "Banner failed to load: " + (info?.Message ?? "unknown error"));
            _bannerHandle?.Fail(_bannerLoadGen);
        }

        private void HandleBannerAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            if (!IsConfiguredBannerUnit(arg1)) return;
            UpdateBannerState(_bannerVisibleRequested ? AdsBannerStatus.Showing : AdsBannerStatus.Available,
                "Banner loaded.", true);
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.BannerLoaded);
            IsBannerAvailable = true;
            _bannerHandle?.Complete(_bannerLoadGen);
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Banner Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #region App Open Ad Callbacks

        private void HandleAppOpenAdHidden(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenHidden);
            MarkFullscreenHidden(AdsType.OpenApp, arg1, info?.Placement);
        }

        private void HandleAppOpenAdDisplayFailed(string arg1, MaxSdkBase.ErrorInfo info1, MaxSdkBase.AdInfo info2)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenDisplayFailed);
            FailFullscreenInvocation(AdsType.OpenApp, arg1, info2?.Placement, "App-open ad display failed.");
        }

        private void HandleAppOpenAdDisplayed(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenDisplayed);
            MarkFullscreenDisplayed(AdsType.OpenApp, arg1, info?.Placement);
        }

        private void HandleAppOpenAdClicked(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenClicked);
        }

        private void HandleAppOpenAdRevenuePaid(string arg1, MaxSdkBase.AdInfo info)
        {
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenRevenuePaid);
            SendRevenueTracking(info, AdsType.OpenApp);
        }

        private void HandleAppOpenAdFailedToLoad(string arg1, MaxSdkBase.ErrorInfo info)
        {
            _openAppHandle?.Fail(_openAppLoadGen);
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenFailedToLoad);
        }

        private void HandleAppOpenAdLoaded(string arg1, MaxSdkBase.AdInfo info)
        {
            _openAppHandle?.Complete(_openAppLoadGen);
            SendTrackingEvent(ApplovinMaxAdsTrackingEventType.AppOpenLoaded);
            QuickLog.Info<ApplovinMaxAdsServiceProvider>(
                "Open App Ad is loaded and ready to be shown."
            );
        }

        #endregion

        #endregion

        #region Nested Types
        private sealed class FullscreenInvocation
        {
            internal readonly AdsInvocationHandler Handler;
            internal readonly string UnitId;
            internal float Elapsed;
            internal float RewardWaitElapsed;
            internal bool Hidden;
            internal bool SkipRewardWaitTick;

            internal FullscreenInvocation(AdsInvocationHandler handler, string unitId)
            {
                Handler = handler;
                UnitId = unitId ?? string.Empty;
            }
        }
        #endregion
    }
}

#endif
