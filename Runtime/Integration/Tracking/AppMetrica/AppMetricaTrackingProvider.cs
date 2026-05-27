#if TRACKING_APPMETRICA

using System;
using Com.Hapiga.Scheherazade.Common.Logging;
using Io.AppMetrica;
using Io.AppMetrica.Ecommerce;
using Newtonsoft.Json;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{

    [CreateAssetMenu(
        fileName = "AppMetricaTrackingProvider",
        menuName = "Scheherazade/Tracking Providers/AppMetrica"
    )]
    public class AppMetricaTrackingProvider :
        ScriptableObject,
        ITrackingProvider
    {
        private string AppMetricaFirstSessionMarkerKey => "APPMETRICA_FIRST_SESSION";

        public ITrackingManager TrackingManager { get; set; }
        public bool IsInitialized { get; private set; }
        public bool IsTrackingEnabled { get; private set; }
        public TrackingProviderFeatures Features => TrackingProviderFeatures.AllFeatures;
        public int Priority => 1;
        public ActionSeverity MinimumActionSeverity => minimumActionSeverity;
        public bool IsFirstSession
        {
            get
            {
                bool isFirstSession = !PlayerPrefs.HasKey(AppMetricaFirstSessionMarkerKey);
                if (isFirstSession)
                {
                    PlayerPrefs.SetInt(AppMetricaFirstSessionMarkerKey, 1);
                    PlayerPrefs.Save();
                }
                return isFirstSession;
            }
        }
        public string ApiKey
        {
            get
            {
#if UNITY_ANDROID
                return androidApiKey;
#elif UNITY_IOS
                return iosApiKey;
#else
                return string.Empty;
#endif
            }
        }

        public float IapMultiplier => iapMultiplier;

        [SerializeField]
        private string androidApiKey;

        [SerializeField]
        private string iosApiKey;

        [SerializeField]
        private float iapMultiplier;

        [SerializeField]
        private ActionSeverity minimumActionSeverity = ActionSeverity.Info;

        public void Initialize()
        {
            try
            {
                AppMetrica.OnActivation += HandleAppMetricaAppActivated;
                AppMetrica.Activate(new AppMetricaConfig(ApiKey)
                {
                    FirstActivationAsUpdate = !IsFirstSession,
                    Logs = true,
                    CrashReporting = true,
                });
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AppMetricaTrackingProvider>(
                    "Failed to initialize AppMetrica: {0}",
                    ex.Message
                );
            }
        }

        public void CleanUp()
        {
            AppMetrica.OnActivation -= HandleAppMetricaAppActivated;
            IsInitialized = false;
            IsTrackingEnabled = false;
        }

        public void TrackAction(TrackingActionInfo info)
        {
            try
            {
                AppMetrica.ReportEvent(info.ActionId, SerializeActionInfo(info));
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AppMetricaTrackingProvider>(
                    "Failed to log action {0} to AppMetrica: {1}",
                    info.ActionId, ex.Message
                );
            }
        }

        private static string SerializeActionInfo(TrackingActionInfo info)
        {
            return info.Parameters == null
                ? "{}"
                : JsonConvert.SerializeObject(
                    info.Parameters,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        DefaultValueHandling = DefaultValueHandling.Ignore
                    }
            );
        }

        public void TrackAdRevenue(AdTrackingInfo info)
        {
            try
            {
                var adrev = new AdRevenue(info.Revenue, "USD");
                adrev.AdNetwork = info.NetworkName;
                adrev.AdPlacementId = info.Placement;
                adrev.AdUnitId = info.RevenueUnit;
                AppMetrica.ReportAdRevenue(adrev);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AppMetricaTrackingProvider>(
                    "Failed to log ad revenue {0} to AppMetrica: {1}",
                    info.RevenueUnit, ex.Message
                );
            }
        }

        public void TrackPurchaseRevenue(PurchaseTrackingInfo info)
        {
            try
            {
                Revenue purrev = AcquireRevenueInfo(ref info);
                AppMetrica.ReportRevenue(purrev);
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AppMetricaTrackingProvider>(
                    "Failed to log purchase {0} to AppMetrica: {1}",
                    info.ProductId, ex.Message
                );
            }
        }

        private Revenue AcquireRevenueInfo(ref PurchaseTrackingInfo info)
        {
            var purrev = new Revenue(
                (long)(info.Price * IapMultiplier * 1000000),
                info.Currency
            )
            {
                ProductID = info.ProductId,
            };

#if UNITY_ANDROID
            purrev.ReceiptValue = new Revenue.Receipt
            {
                Signature = info.Receipt.PayloadAndroid.Signature,
                Data = info.Receipt.PayloadAndroid.Json,
            };
#elif UNITY_IOS
                purrev.ReceiptValue = new Revenue.Receipt
                {
                    Data = info.ReceiptRaw,
                };
#endif
            return purrev;
        }

        public void TrackScreen(string screenId)
        {
            try
            {
                ECommerceScreen screen = new ECommerceScreen() { Name = screenId };
                AppMetrica.ReportECommerce(ECommerceEvent.ShowScreenEvent(screen));
            }
            catch (Exception ex)
            {
                QuickLog.Warning<AppMetricaTrackingProvider>(
                    "Failed to log screen {0} to AppMetrica: {1}",
                    screenId, ex.Message
                );
            }
        }

        private void HandleAppMetricaAppActivated(AppMetricaConfig config)
        {
            IsInitialized = true;
            IsTrackingEnabled = true;
            QuickLog.Info<AppMetricaTrackingProvider>("[AppMetrica] Initialized Successfully");
        }
    }

}

#endif