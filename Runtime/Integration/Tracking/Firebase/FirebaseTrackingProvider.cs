#if FIREBASE_ANALYTICS

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Converter;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [Flags]
    public enum FirebaseRevenueTrackingOptions
    {
        None = 0,
        BannerAdsRevenue = 1 << 0,
        InterstitialAdsRevenue = 1 << 1,
        RewardedAdsRevenue = 1 << 2,
        AppOpenAdsRevenue = 1 << 3,
        IapRevenue = 1 << 4,
    }

    [Serializable]
    public struct FirebaseRevenueTrackingEvent
    {
        public string eventName;
        public FirebaseRevenueTrackingOptions trackingOptions;
    }

    [CreateAssetMenu(
        fileName = "FirebaseTrackingProvider",
        menuName = "Scheherazade/Tracking Providers/Firebase"
    )]
    public class FirebaseTrackingProvider :
        ScriptableObject,
        ITrackingProvider
    {
        public bool IsInitialized { get; private set; }
        public ITrackingManager TrackingManager { get; set; }
        public bool IsTrackingEnabled { get; private set; }
        public TrackingProviderFeatures Features => TrackingProviderFeatures.AllFeatures;
        public TrackingProviderFeatures EnabledFeatures => enabledFeatures;
        public int Priority => 0;
        public ActionSeverity MinimumActionSeverity => minimumActionSeverity;
        public FirebaseRevenueTrackingEvent[] RevenueTrackingConfig => adsRevenueTrackingConfig;
        public float IapMultiplier => iapMultiplier;

        [SerializeField]
        private FirebaseRevenueTrackingEvent[] adsRevenueTrackingConfig;

        [SerializeField]
        private float iapMultiplier = 0.7f;

        [SerializeField]
        [TrackingFeatureFilter]
        private TrackingProviderFeatures enabledFeatures = TrackingProviderFeatures.AllFeatures;

        [SerializeField]
        private ActionSeverity minimumActionSeverity = ActionSeverity.Debug;

        [SerializeField]
        private int providerMaskNumber = 0;

        [SerializeField]
        private float initializationTimeout = 10f;

        [SerializeField]
        private int retryAttempt = 3;

        [SerializeField]
        private float retryDelay = 1.0f;

        private int _initializationAttempts = 0;
        private WaitForSeconds _waitForRetry;

        public ProviderIdentity ProviderIdentity => (ProviderIdentity)(1 << providerMaskNumber);


        public void Initialize()
        {
            IsInitialized = false;
            IsTrackingEnabled = false;
            _waitForRetry = new WaitForSeconds(retryDelay);
            _initializationAttempts = 0;
            Dispatcher.DispatchCoroutine(InitializeInternal());
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
                QuickLog.Error<FirebaseTrackingProvider>(
                    "Firebase SDK initialization failed after multiple attempts."
                );
                yield break;
            }

            QuickLog.Info<FirebaseTrackingProvider>(
                "Start Initializing Firebase SDK"
            );

            bool succeed = false;
            float initCountdown = initializationTimeout;

            try
            {
                Firebase.Analytics.FirebaseAnalytics
                    .GetSessionIdAsync()
                    .ContinueTaskOnMainThread(task =>
                    {
                        succeed = task.IsCompletedSuccessfully;
                        if (succeed)
                        {
                            IsInitialized = true;
                            IsTrackingEnabled = true;
                            QuickLog.Info<FirebaseTrackingProvider>(
                                "Firebase Tracking initialized successfully."
                            );
                        }
                    });
            }
            catch (Exception ex)
            {
                QuickLog.Error<FirebaseTrackingProvider>(
                    "Firebase SDK initialization error: {0}",
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
                QuickLog.Warning<FirebaseTrackingProvider>(
                    "Firebase SDK initialization timed out. Retrying..."
                );
                yield return _waitForRetry;
                Dispatcher.DispatchCoroutine(InitializeInternal());
            }
        }

        public void TrackScreen(string screenId)
        {
            QuickLog.Debug<FirebaseTrackingProvider>(
                "Start pushing screen event: {0}", screenId
            );

            TrackAction(new TrackingActionInfo
            {
                ActionId = Firebase.Analytics.FirebaseAnalytics.EventScreenView,
                Parameters = new Dictionary<string, object>
                {
                    { Firebase.Analytics.FirebaseAnalytics.ParameterScreenName, screenId }
                }
            });

            QuickLog.Debug<FirebaseTrackingProvider>(
                "Finished pushing screen event: {0}", screenId
            );
        }

        public void TrackAction(TrackingActionInfo info)
        {
            QuickLog.Debug<FirebaseTrackingProvider>(
                "Start pushing action event: {0}", info.ActionId
            );

            List<Firebase.Analytics.Parameter> parameters = new List<Firebase.Analytics.Parameter>();
            CollectTrackingParameters(info, parameters);

            try
            {
                Firebase.Analytics.FirebaseAnalytics.LogEvent(info.ActionId, parameters.ToArray());
            }
            catch (Exception ex)
            {
                QuickLog.Warning<FirebaseTrackingProvider>(
                    "Failed to log event {0} to Firebase Analytics: {1}",
                    info.ActionId, ex.Message
                );
            }

            QuickLog.Debug<FirebaseTrackingProvider>(
                "Finished pushing action event: {0}", info.ActionId
            );
        }

        private static void CollectTrackingParameters(
            TrackingActionInfo info,
            List<Firebase.Analytics.Parameter> parameters
        )
        {
            if (info.Parameters == null) return;

            foreach (KeyValuePair<string, object> kvp in info.Parameters)
            {
                CollectSingleTrackingVariable(parameters, kvp);
            }
        }

        private static void CollectSingleTrackingVariable(
            List<Firebase.Analytics.Parameter> parameters,
            KeyValuePair<string, object> kvp
        )
        {
            switch (kvp.Value)
            {
                case string strValue:
                    parameters.Add(new Firebase.Analytics.Parameter(kvp.Key, strValue));
                    break;
                case int intValue:
                    parameters.Add(new Firebase.Analytics.Parameter(kvp.Key, intValue));
                    break;
                case float floatValue:
                    parameters.Add(new Firebase.Analytics.Parameter(kvp.Key, floatValue));
                    break;
                case double doubleValue:
                    parameters.Add(new Firebase.Analytics.Parameter(kvp.Key, doubleValue));
                    break;
                case long longValue:
                    parameters.Add(new Firebase.Analytics.Parameter(kvp.Key, longValue));
                    break;
                case decimal decimalValue:
                    parameters.Add(new Firebase.Analytics.Parameter(kvp.Key, (double)decimalValue));
                    break;
                case bool boolValue:
                    parameters.Add(new Firebase.Analytics.Parameter(kvp.Key, boolValue ? 1 : 0));
                    break;
                default:
                    parameters.Add(new Firebase.Analytics.Parameter(kvp.Key, kvp.Value.ToString()));
                    break;
            }
        }

        public void TrackAdRevenue(AdTrackingInfo info)
        {
            QuickLog.Debug<FirebaseTrackingProvider>(
                "Start pushing ad revenue event: {0} ({1} {2})",
                info.NetworkName, info.Revenue, info.RevenueUnit
            );

            Dictionary<string, object> parameters = new Dictionary<string, object>
            {
                {"ad_platform",     info.NetworkName},
                {"ad_source",       info.NetworkName},
                {"ad_unit_name",    info.RevenueUnit},
                {"ad_format",       info.AdFormat},
                {"ad_creative",     info.CreativeIdentifier},
                {"country",         info.Country},
                {"value",           info.Revenue},
                {"currency",        CurrencyCode.USD.ToUpper()},
            };

            foreach (FirebaseRevenueTrackingEvent conf in RevenueTrackingConfig)
            {
                if (!CheckAvailableTrackingFlag(info.AdType, conf.trackingOptions))
                {
                    continue;
                }

                TrackAction(new TrackingActionInfo
                {
                    ActionId = conf.eventName,
                    Parameters = parameters
                });
            }

            QuickLog.Debug<FirebaseTrackingProvider>(
                "Finished pushing ad revenue event: {0}", info.NetworkName
            );
        }

        public void TrackPurchaseRevenue(PurchaseTrackingInfo info)
        {
            QuickLog.Debug<FirebaseTrackingProvider>(
                "Start pushing purchase revenue event: {0} ({1} {2})",
                info.ProductId, info.Price, info.Currency
            );

            if (Integration.CurrencyConverter != null)
            {
                TryConvertRevenueToUsd(ref info);
            }

            info.Price *= IapMultiplier;

            Dictionary<string, object> parameters = new Dictionary<string, object>
            {
                {"product_id",      info.ProductId},
                {"value",           info.Price},
                {"currency",        info.Currency},
            };

            foreach (FirebaseRevenueTrackingEvent conf in RevenueTrackingConfig)
            {
                if (!conf.trackingOptions.HasFlag(FirebaseRevenueTrackingOptions.IapRevenue))
                {
                    continue;
                }

                TrackAction(new TrackingActionInfo
                {
                    ActionId = conf.eventName,
                    Parameters = parameters
                });
            }

            QuickLog.Debug<FirebaseTrackingProvider>(
                "Finished pushing purchase revenue event: {0}", info.ProductId
            );
        }

        private void TryConvertRevenueToUsd(ref PurchaseTrackingInfo info)
        {
            if (Integration.CurrencyConverter == null) return;
            if (info.Currency == CurrencyCode.USD) return;

            decimal? usdAmount = Integration.CurrencyConverter
                .Convert(
                    info.Currency, CurrencyCode.USD, (decimal)info.Price
                );

            if (usdAmount.HasValue)
            {
                info.Price = (double)usdAmount.Value;
                info.Currency = CurrencyCode.USD.ToUpper();
            }
        }

        private bool CheckAvailableTrackingFlag(
            AdsType adType, FirebaseRevenueTrackingOptions options
        )
        {
            return adType switch
            {
                AdsType.Interstitial
                    => options.HasFlag(FirebaseRevenueTrackingOptions.InterstitialAdsRevenue),
                AdsType.Rewarded
                    => options.HasFlag(FirebaseRevenueTrackingOptions.RewardedAdsRevenue),
                AdsType.OpenApp
                    => options.HasFlag(FirebaseRevenueTrackingOptions.AppOpenAdsRevenue),
                AdsType.Banner
                    => options.HasFlag(FirebaseRevenueTrackingOptions.BannerAdsRevenue),

                _ => false,
            };
        }
    }
}

#endif