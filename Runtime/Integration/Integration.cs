using System;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Converter;
using Com.Hapiga.Scheherazade.Common.Integration.IAR;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase;
using Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig;
using Com.Hapiga.Scheherazade.Common.Integration.Segmentation;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.Integration
{
    public enum IntegrationStatus
    {
        Uninitialized,
        Initializing,
        Ready
    }

    /// <summary>
    /// Static registry of all integration managers plus typed lookup helpers.
    /// Per-domain static facades (Ads, Tracking, InAppPurchases, ...) build on top
    /// of this registry and are the recommended way to call integration APIs.
    /// </summary>
    public class Integration
    {
        #region Manager Registry

        public static IAdsManager AdsManager { get; private set; }
        public static IInAppPurchaseManager InAppPurchaseManager { get; private set; }
        public static IRemoteConfigManager RemoteConfigManager { get; private set; }
        public static ITrackingManager TrackingManager { get; private set; }
        public static IInAppReviewManager InAppReviewManager { get; set; }
        public static IUserSegmentation UserSegmentation { get; private set; }
        public static ICurrencyConverter CurrencyConverter { get; set; }

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
                default:
                    throw new ArgumentException($"Unknown manager type: {typeof(T)}");
            }

            QuickLog.Info<Integration>("Registered manager of type {0}.", typeof(T).Name);
        }

        #endregion

        #region Status & Typed Lookup

        /// <summary>
        /// Aggregated status across all registered managers.
        /// Ready = every registered manager reports ready; Initializing = at least one
        /// is still working; Uninitialized = none ready and none working.
        /// </summary>
        public static IntegrationStatus Status
        {
            get
            {
                object[] managers = new object[]
                {
                    AdsManager,
                    InAppPurchaseManager,
                    RemoteConfigManager,
                    TrackingManager,
                    InAppReviewManager,
                    UserSegmentation,
                    CurrencyConverter
                };

                bool allReady = true;
                bool anyInitializing = false;

                for (int i = 0; i < managers.Length; i++)
                {
                    object manager = managers[i];
                    if (manager == null)
                    {
                        continue;
                    }

                    if (!IsManagerReady(manager))
                    {
                        allReady = false;
                    }

                    if (IsManagerInitializing(manager))
                    {
                        anyInitializing = true;
                    }
                }

                if (allReady)
                {
                    return IntegrationStatus.Ready;
                }

                if (anyInitializing)
                {
                    return IntegrationStatus.Initializing;
                }

                return IntegrationStatus.Uninitialized;
            }
        }

        public static bool IsReady => Status == IntegrationStatus.Ready;

        /// <summary>
        /// Returns the registered manager assignable to <typeparamref name="T"/>
        /// (interface or concrete type), or null when not registered.
        /// </summary>
        public static T GetManager<T>() where T : class
        {
            if (AdsManager is T adsManager) return adsManager;
            if (InAppPurchaseManager is T inAppPurchaseManager) return inAppPurchaseManager;
            if (RemoteConfigManager is T remoteConfigManager) return remoteConfigManager;
            if (TrackingManager is T trackingManager) return trackingManager;
            if (InAppReviewManager is T inAppReviewManager) return inAppReviewManager;
            if (UserSegmentation is T userSegmentation) return userSegmentation;
            if (CurrencyConverter is T currencyConverter) return currencyConverter;
            return null;
        }

        /// <summary>
        /// Null-safe variant of <see cref="GetManager{T}"/>.
        /// </summary>
        public static bool TryGetManager<T>(out T manager) where T : class
        {
            manager = GetManager<T>();
            return manager != null;
        }

        /// <summary>
        /// Returns an IIntegrationModule by its concrete type from the IntegrationCentre,
        /// or null when the centre or module is not available.
        /// </summary>
        public static T GetModule<T>() where T : class, IIntegrationModule
        {
            IntegrationCentre centre = IntegrationCentre.Instance;
            return centre == null ? null : centre.GetModule<T>();
        }

        /// <summary>
        /// Strict guard used by async facade flavors: throws
        /// <see cref="IntegrationModuleNotFoundException"/> when the manager is not registered.
        /// </summary>
        internal static T RequireManager<T>() where T : class
        {
            T manager = GetManager<T>();
            if (manager == null)
            {
                throw new IntegrationModuleNotFoundException(typeof(T));
            }

            return manager;
        }

        private static bool IsManagerReady(object manager)
        {
            switch (manager)
            {
                case IAdsManager ads:
                    return ads.Status == AdsManagerStatus.Ready;
                case ITrackingManager tracking:
                    return tracking.Status == TrackingManagerStatus.Ready;
                case IInAppPurchaseManager inAppPurchase:
                    return inAppPurchase.Status == InAppPurchaseManagerStatus.Ready;
                case IRemoteConfigManager remoteConfig:
                    return remoteConfig.Status == RemoteConfigStatus.Ready;
                case IUserSegmentation segmentation:
                    return segmentation.Status == UserSegmentationStatus.Initialized;
                case ICurrencyConverter currency:
                    return currency.Status == CurrencyConverterStatus.Initialized;
                case IInAppReviewManager:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsManagerInitializing(object manager)
        {
            switch (manager)
            {
                case IAdsManager ads:
                    return ads.Status == AdsManagerStatus.Initializing;
                case ITrackingManager tracking:
                    return tracking.Status == TrackingManagerStatus.Initializing;
                case IInAppPurchaseManager inAppPurchase:
                    return inAppPurchase.Status == InAppPurchaseManagerStatus.Initializing;
                case IRemoteConfigManager remoteConfig:
                    return remoteConfig.Status == RemoteConfigStatus.Initializing
                        || remoteConfig.Status == RemoteConfigStatus.Refreshing;
                case IUserSegmentation segmentation:
                    return segmentation.Status == UserSegmentationStatus.Initializing;
                case ICurrencyConverter currency:
                    return currency.Status == CurrencyConverterStatus.Initializing;
                default:
                    return false;
            }
        }

        #endregion
    }
}
