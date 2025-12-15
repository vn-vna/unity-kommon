#if TRACKING_ADJUST

using System;
using System.Security.Cryptography;
using System.Text;

using AdjustSdk;

using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Segmentation;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public class AdjustTrackingProvider :
        ITrackingProvider
    {
        public ITrackingManager TrackingManager { get; set; }
        public bool IsInitialized { get; private set; }
        public bool IsTrackingEnabled { get; private set; }
        public AdjustConfiguration Configuration { get; private set; }
        public TrackingProviderFeatures Features => TrackingProviderFeatures.Revenue;
        public int Priority => 1;

        private AdjustConfig _config;
        private int _initializationAttempts = 0;

        public AdjustTrackingProvider(AdjustConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public void Initialize()
        {
            _config = new AdjustConfig(
                Configuration.AppToken,
                (AdjustSdk.AdjustEnvironment)Configuration.Environment
            );

            _config.LogLevel = (AdjustSdk.AdjustLogLevel?)Configuration.LogLevel;
            _config.SessionSuccessDelegate = HandleAdjustSessionSuccess;
            _config.SessionFailureDelegate = HandleAdjustSessionFailure;
            _config.AttributionChangedDelegate = HandleAttributionChanged;
            IsTrackingEnabled = true;

#if UNITY_EDITOR
            IsInitialized = true;
#else
            InitializeInternal();
#endif
        }

        public void CleanUp()
        {
            IsInitialized = false;
            IsTrackingEnabled = false;
        }

        private void InitializeInternal()
        {
            if (_initializationAttempts++ > 3)
            {
                QuickLog.Error<AdjustTrackingProvider>(
                    "Adjust SDK initialization failed after multiple attempts."
                );
                return;
            }

            QuickLog.Info<AdjustTrackingProvider>(
                "Start Initializing Adjust SDK"
            );
            ++_initializationAttempts;

            try
            {
                Adjust.InitSdk(_config);
            }
            catch (Exception ex)
            {
                QuickLog.Error<AdjustTrackingProvider>(
                    "Adjust SDK initialization error: {0}",
                    ex.Message
                );
                InitializeInternal();
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
                
                // Set ad impressions count to 1 for proper ad revenue tracking
                // This is required for accurate ad revenue attribution across all platforms
                // Particularly critical for iOS where this was causing 30-40% underreporting
                e.AdImpressionsCount = 1;
                
                // Set additional parameters for better revenue attribution
                if (!string.IsNullOrEmpty(info.AdFormat))
                {
                    e.AddCallbackParameter("ad_format", info.AdFormat);
                }
                
                if (!string.IsNullOrEmpty(info.CreativeIdentifier))
                {
                    e.AddCallbackParameter("creative_id", info.CreativeIdentifier);
                }
                
                if (!string.IsNullOrEmpty(info.Country))
                {
                    e.AddCallbackParameter("country", info.Country);
                }
                
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
                AdjustEvent e = new AdjustEvent(Configuration.IapEventName);
                e.SetRevenue(info.Price * Configuration.IapMultiplier, info.Currency);
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

            Dispatcher.DispatchOnMainThread(InitializeInternal);
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
                return;
            }

            Dispatcher.DispatchOnMainThread(() => RegisterAttributionData(attribution));
        }

        private void HandleAttributionChanged(AdjustAttribution attribution)
        {
            QuickLog.Info<AdjustTrackingProvider>($"Attribution Info Changed");
            if (attribution == null)
            {
                QuickLog.Warning<AdjustTrackingProvider>(
                    "Attribution info received is null"
                );
                return;
            }

            Dispatcher.DispatchOnMainThread(() => RegisterAttributionData(attribution));
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

        private static void RegisterAttributionData(AdjustAttribution attribution)
        {
            if (Integration.UserSegmentation == null)
            {
                QuickLog.Error<AdjustTrackingProvider>(
                    $"User segmentation manager is not found"
                );
                return;
            }

            string campaignName = attribution.Campaign ?? "unknown_campaign";
            string creativeName = attribution.Creative ?? "unknown_creative";

            using SHA256 sha256 = SHA256.Create();

            byte[] cph = sha256.ComputeHash(Encoding.UTF8.GetBytes(campaignName));
            StringBuilder cphBuilder = new StringBuilder();
            foreach (var c in cph)
            {
                cphBuilder.Append(c.ToString("x2"));
            }
            string campaignHash = cphBuilder.ToString();

            byte[] cth = sha256.ComputeHash(Encoding.UTF8.GetBytes(creativeName));
            StringBuilder cthBuilder = new StringBuilder();
            foreach (var c in cth)
            {
                cthBuilder.Append(c.ToString("x2"));
            }

            string creativeHash = cthBuilder.ToString();

            Integration.UserSegmentation.RegisterSegmentation(
                new SegmentationInformation
                {
                    CampaignHash = campaignHash,
                    CreativeHash = creativeHash,
                    CampaignName = campaignName,
                    CreativeName = creativeName
                }
            );
        }
    }
}

#endif