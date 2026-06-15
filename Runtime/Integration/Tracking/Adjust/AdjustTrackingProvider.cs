#if TRACKING_ADJUST

using System;
using System.Collections;
using AdjustSdk;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [Serializable]
    public enum AdjustEnvironment
    {
        Sandbox,
        Production
    }

    [Serializable]
    public enum AdjustLogLevel
    {
        Verbose = 1,
        Debug,
        Info,
        Warn,
        Error,
        Assert,
        Suppress
    }

    [CreateAssetMenu(
        fileName = "AdjustTrackingProvider",
        menuName = "Scheherazade/Tracking Providers/Adjust"
    )]
    public class AdjustTrackingProvider :
        ScriptableObject,
        ITrackingProvider
    {
        public ITrackingManager TrackingManager { get; set; }
        public bool IsInitialized { get; private set; }
        public bool IsTrackingEnabled { get; private set; }

        public TrackingProviderFeatures Features => TrackingProviderFeatures.Revenue;

        public int Priority => 1;

        public string AppToken
        {
            get
            {
#if UNITY_ANDROID
                return androidAppToken;
#elif UNITY_IOS
                return iosAppToken;
#else
                return string.Empty;
#endif
            }
        }

        public AdjustEnvironment Environment => environment;

        public AdjustLogLevel LogLevel => logLevel;

        public string IapEventName
        {
            get
            {
#if UNITY_ANDROID
                return androidIapEventName;
#elif UNITY_IOS
                return iosIapEventName;
#else
                return string.Empty;
#endif
            }
        }

        public float IapMultiplier => iapMultiplier;
        public ActionSeverity MinimumActionSeverity => ActionSeverity.Error;

        [SerializeField]
        private string androidAppToken;

        [SerializeField]
        private string iosAppToken;

        [SerializeField]
        private AdjustEnvironment environment;

        [SerializeField]
        private string androidIapEventName;

        [SerializeField]
        private string iosIapEventName;

        [SerializeField]
        private float iapMultiplier;

        [SerializeField]
        private float initializationTimeout = 10f;

        [SerializeField]
        private int retryAttempt = 3;

        [SerializeField]
        private float retryDelay = 1.0f;

        [SerializeField]
        private AdjustLogLevel logLevel = AdjustLogLevel.Verbose;

        private AdjustConfig _config;
        private int _initializationAttempts = 0;
        private WaitForSeconds _waitForRetry;

        public void Initialize()
        {
            _waitForRetry = new WaitForSeconds(retryDelay);
            _config = new AdjustConfig(AppToken, (AdjustSdk.AdjustEnvironment)Environment)
            {
                LogLevel = (AdjustSdk.AdjustLogLevel?)LogLevel,
                AttributionChangedDelegate = HandleAttributionChanged,
                SessionSuccessDelegate = HandleAdjustSessionSuccess,
                SessionFailureDelegate = HandleAdjustSessionFailure
            };
            IsTrackingEnabled = true;
            _initializationAttempts = 0;

#if UNITY_EDITOR
            IsInitialized = true;
#else
            Dispatcher.DispatchCoroutine(InitializeInternal());
#endif
        }

        public void CleanUp()
        {
            IsInitialized = false;
            IsTrackingEnabled = false;
            _initializationAttempts = 0;
        }

        private IEnumerator InitializeInternal()
        {
            if (_initializationAttempts++ > retryAttempt)
            {
                QuickLog.Error<AdjustTrackingProvider>(
                    "Adjust SDK initialization failed after multiple attempts."
                );
                yield break;
            }

            QuickLog.Info<AdjustTrackingProvider>(
                "Start Initializing Adjust SDK"
            );

            ++_initializationAttempts;

            bool succeed = false;
            float initCountdown = initializationTimeout;

            try
            {
                Adjust.InitSdk(_config);
                Adjust.GetAttribution((attrib) => succeed = attrib != null);
            }
            catch (Exception ex)
            {
                QuickLog.Error<AdjustTrackingProvider>(
                    "Adjust SDK initialization error: {0}",
                    ex.Message
                );
            }

            while (initCountdown >= 0 && !succeed)
            {
                initCountdown -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (!succeed)
            {
                yield return _waitForRetry;
                Dispatcher.DispatchCoroutine(InitializeInternal());
            }
            else
            {
                IsInitialized = true;
            }
        }

        public void RequireAdvertisingId()
        {
            try
            {
                QuickLog.Info<AdjustTrackingProvider>("Acquiring Advertising ID");

#if UNITY_ANDROID && !UNITY_EDITOR
                Adjust.GetGoogleAdId(AcquireAdvertisingIdCallback);
#endif

#if UNITY_IOS && !UNITY_EDITOR
                Adjust.GetIdfa(AcquireAdvertisingIdCallback);
#endif
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AdjustTrackingProvider>(
                    "Failed to acquire Advertising ID: {0}",
                    ex.Message
                );
            }

        }

        public void RequireAttributions()
        {
            try
            {
                QuickLog.Info<AdjustTrackingProvider>(
                    "Acquiring Attribution Info"
                );
                Adjust.GetAttribution(AcquireAttributionCallback);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AdjustTrackingProvider>(
                    "Failed to acquire Attribution Info: {0}",
                    ex.Message
                );
            }
        }

        public void TrackScreen(string screenId) => throw new NotImplementedException();
        public void TrackAction(TrackingActionInfo info) => throw new NotImplementedException();

        public void TrackAdRevenue(AdTrackingInfo info)
        {
            try
            {
                string adSourceName = AcquireAdSourceName(info);

                if (string.IsNullOrEmpty(adSourceName))
                {
                    QuickLog.Warning<AdjustTrackingProvider>(
                        "Unsupported ad provider for Adjust ad revenue tracking: {0}",
                        info.Provider?.GetType().Name
                    );
                    return;
                }

                AdjustAdRevenue e = new AdjustAdRevenue(adSourceName);
                e.SetRevenue(info.Revenue, "USD");
                e.AdRevenueNetwork = info.NetworkName;
                e.AdRevenueUnit = info.RevenueUnit;
                e.AdRevenuePlacement = info.Placement;
                Adjust.TrackAdRevenue(e);

                QuickLog.Info<AdjustTrackingProvider>(
                    "Logged ad revenue to Adjust: {0} USD from {1} (Placement: {2})",
                    info.Revenue, info.NetworkName, info.Placement
                );
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AdjustTrackingProvider>(
                    "Failed to log ad revenue to Adjust: {0}",
                    ex.Message
                );
            }
        }

        private static string AcquireAdSourceName(AdTrackingInfo info)
        {
            string adSourceName = null;

#if APPLOVIN_MAX
            if (info.Provider is ApplovinMaxAdsServiceProvider)
            {
                adSourceName = "applovin_max_sdk";
            }
#endif
            return adSourceName;
        }

        public void TrackPurchaseRevenue(PurchaseTrackingInfo info)
        {
            try
            {
                AdjustEvent e = new AdjustEvent(IapEventName);
                e.SetRevenue(info.Price * IapMultiplier, info.Currency);
                e.ProductId = info.ProductId;
                e.TransactionId = info.TransactionId;
                e.PurchaseToken = info.ReceiptRaw;
                Adjust.TrackEvent(e);

                QuickLog.Info<AdjustTrackingProvider>(
                    "Logged purchase revenue to Adjust: {0} {1} for product {2} (Transaction ID: {3})",
                    info.Price, info.Currency, info.ProductId, info.TransactionId
                );
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AdjustTrackingProvider>(
                    "Failed to log purchase revenue to Adjust: {0}",
                    ex.Message
                );
            }
        }

        private void HandleAdjustSessionFailure(AdjustSessionFailure failure)
        {
            IsInitialized = false;
            QuickLog.Warning<AdjustTrackingProvider>(
                "Adjust session initialization failed: {0} (Adid: {1})",
                failure.Message, failure.Adid
            );
        }

        private void HandleAdjustSessionSuccess(AdjustSessionSuccess success)
        {
            IsInitialized = true;
            Dispatcher.DispatchOnMainThread(RequireAdvertisingId);
            Dispatcher.DispatchOnMainThread(RequireAttributions);
            QuickLog.Info<AdjustTrackingProvider>(
                "Adjust session initialized successfully (Adid: {0})",
                success.Adid
            );
        }

        private void AcquireAttributionCallback(AdjustAttribution attribution)
        {
            QuickLog.Info<AdjustTrackingProvider>($"Acquired Attribution Info");
            if (attribution == null)
            {
                QuickLog.Warning<AdjustTrackingProvider>(
                    "Attribution info received is null"
                );
            }
        }

        private void HandleAttributionChanged(AdjustAttribution attribution)
        {
            QuickLog.Info<AdjustTrackingProvider>($"Attribution Info Changed");
            IsInitialized = true;

            if (attribution == null)
            {
                QuickLog.Warning<AdjustTrackingProvider>(
                    "Attribution info received is null"
                );
            }
        }

#if !UNITY_EDITOR
        private void AcquireAdvertisingIdCallback(string adid)
        {
            QuickLog.Info<AdjustTrackingProvider>(
                "Acquired Advertising ID: {0}",
                adid
            );

            if (!string.IsNullOrEmpty(adid))
            {
                Dispatcher.DispatchOnMainThread(() =>
                {
                    PlayerPrefs.SetString("advertising_id", adid);
                    PlayerPrefs.Save();
                });
            }
        }
#endif

    }
}


#endif