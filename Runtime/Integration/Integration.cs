using System;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Converter;
using Com.Hapiga.Scheherazade.Common.Integration.IAR;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase;
using Com.Hapiga.Scheherazade.Common.Integration.L18n;
using Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig;
using Com.Hapiga.Scheherazade.Common.Integration.Segmentation;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;

namespace Com.Hapiga.Scheherazade.Common.Integration
{
    public static class Integration
    {
        public static IAdsManager AdsManager { get; private set; }
        public static IInAppPurchaseManager InAppPurchaseManager { get; private set; }
        public static IRemoteConfigManager RemoteConfigManager { get; private set; }
        public static ITrackingManager TrackingManager { get; private set; }
        public static IInAppReviewManager InAppReviewManager { get; set; }
        public static IUserSegmentation UserSegmentation { get; private set; }
        public static ICurrencyConverter CurrencyConverter { get; set; }
        public static ILocalizationManager LocalizationManager { get; private set; }

        internal static void RegisterManager<T>(T manager) where T : class
        {
            switch (manager)
            {
                case IAdsManager adsManager:
                    AdsManager = adsManager;
                    break;
                case IInAppPurchaseManager inAppPurchaseManager:
                    InAppPurchaseManager = inAppPurchaseManager;
                    break;
                case IRemoteConfigManager remoteConfigManager:
                    RemoteConfigManager = remoteConfigManager;
                    break;
                case ITrackingManager trackingManager:
                    TrackingManager = trackingManager;
                    break;
                case IInAppReviewManager inAppReviewManager:
                    InAppReviewManager = inAppReviewManager;
                    break;
                case IUserSegmentation userSegmentation:
                    UserSegmentation = userSegmentation;
                    break;
                case ICurrencyConverter currencyConverter:
                    CurrencyConverter = currencyConverter;
                    break;
                case ILocalizationManager localizationManager:
                    LocalizationManager = localizationManager;
                    break;
                default:
                    throw new ArgumentException($"Unknown manager type: {typeof(T)}"); 
            }
        }
    }
}