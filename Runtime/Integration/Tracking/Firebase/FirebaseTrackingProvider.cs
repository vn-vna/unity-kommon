#if FIREBASE_ANALYTICS

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Converter;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public class FirebaseTrackingProvider :
        ITrackingProvider
    {
        public bool IsInitialized { get; private set; }
        public ITrackingManager TrackingManager { get; set; }
        public bool IsTrackingEnabled { get; private set; }
        public TrackingProviderFeatures Features => TrackingProviderFeatures.AllFeatures;
        public int Priority => 0;

        private FirebaseTrackingConfiguration _configuration;

        public FirebaseTrackingProvider(FirebaseTrackingConfiguration configuration)
        {
            _configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        public void Initialize()
        {
            IsInitialized = false;
            Firebase.Analytics.FirebaseAnalytics
                .GetSessionIdAsync()
                .ContinueTaskOnMainThread(HandleFirebaseInitializedCompleted);
        }

        public void CleanUp()
        {
            IsInitialized = false;
            IsTrackingEnabled = false;
        }

        private void HandleFirebaseInitializedCompleted(Task<long> task)
        {
            if (!task.IsCompletedSuccessfully) return;
            IsInitialized = true;
            IsTrackingEnabled = true;
            QuickLog.Info<FirebaseTrackingProvider>(
                "Firebase Tracking initialized successfully."
            );
        }

        public void TrackScreen(string screenId)
        {
            TrackAction(new TrackingActionInfo
            {
                ActionId = Firebase.Analytics.FirebaseAnalytics.EventScreenView,
                Parameters = new Dictionary<string, object>
                {
                    { Firebase.Analytics.FirebaseAnalytics.ParameterScreenName, screenId }
                }
            });
        }

        public void TrackAction(TrackingActionInfo info)
        {
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

            foreach (FirebaseRevenueTrackingEvent conf in _configuration.RevenueTrackingConfig)
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

        }

        public void TrackPurchaseRevenue(PurchaseTrackingInfo info)
        {
            if (Integration.CurrencyConverter != null)
            {
                TryConvertRevenueToUsd(ref info);
            }

            info.Price *= _configuration.IapMultiplier;

            Dictionary<string, object> parameters = new Dictionary<string, object>
            {
                {"product_id",      info.ProductId},
                {"value",           info.Price},
                {"currency",        info.Currency},
            };

            foreach (FirebaseRevenueTrackingEvent conf in _configuration.RevenueTrackingConfig)
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
                AdsType.AppOpen
                    => options.HasFlag(FirebaseRevenueTrackingOptions.AppOpenAdsRevenue),
                AdsType.Banner
                    => options.HasFlag(FirebaseRevenueTrackingOptions.BannerAdsRevenue),

                _ => false,
            };
        }
    }
}

#endif