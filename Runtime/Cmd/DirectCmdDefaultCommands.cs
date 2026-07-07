using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common
{
    using IntegrationGlobal = Integration.Integration;

    public class DirectCmdDefaultCommands
    {
        #region Private Fields
        private const string DefaultPlacement = "dcf";
        private const string DisabledMessage = "DCF command '{0}' is disabled via DirectCmdSettings.";
        private const string ManagerNotReadyMessage =
            "DCF command '{0}' cannot execute: {1} manager is not ready.";
        #endregion

        #region Public Methods
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAllDefaultCommands()
        {
            RegisterAdsCommands();
            RegisterIapCommands();
            RegisterTrackingCommands();
        }
        #endregion

        #region Private Methods — Ads
        private static void RegisterAdsCommands()
        {
            DirectCmdForwarding.RegisterCommand("ads")
                .WithSubcommand("show", show =>
                {
                    show.WithSubcommand("banner", banner =>
                        {
                            banner.OnExecute((DirectCmdContext _) => HandleAdsBanner());
                        })
                        .WithSubcommand("interstitial", interstitial =>
                        {
                            interstitial
                                .WithParameter<string>(
                                    "placement", "p",
                                    defaultValue: DefaultPlacement)
                                .OnExecute(ctx => HandleAdsInterstitial(ctx));
                        })
                        .WithSubcommand("reward", reward =>
                        {
                            reward
                                .WithParameter<string>(
                                    "placement", "p",
                                    defaultValue: DefaultPlacement)
                                .OnExecute(ctx => HandleAdsReward(ctx));
                        })
                        .WithSubcommand("appopen", appopen =>
                        {
                            appopen
                                .WithParameter<string>(
                                    "placement", "p",
                                    defaultValue: DefaultPlacement)
                                .OnExecute(ctx => HandleAdsAppOpen(ctx));
                        });
                });
        }

        private static void HandleAdsBanner()
        {
            if (!CheckAdsEnabled("ads show banner"))
                return;

            if (IntegrationGlobal.AdsManager == null)
            {
                LogManagerNotReady("ads show banner", "Ads");
                return;
            }

            IntegrationGlobal.AdsManager.ShowBanner();
        }

        private static void HandleAdsInterstitial(DirectCmdContext ctx)
        {
            if (!CheckAdsEnabled("ads show interstitial"))
                return;

            if (IntegrationGlobal.AdsManager == null)
            {
                LogManagerNotReady("ads show interstitial", "Ads");
                return;
            }

            string placement = ctx.GetParam("placement", DefaultPlacement);
            IntegrationGlobal.AdsManager.ShowInterstitialAds(null, placement);
        }

        private static void HandleAdsReward(DirectCmdContext ctx)
        {
            if (!CheckAdsEnabled("ads show reward"))
                return;

            if (IntegrationGlobal.AdsManager == null)
            {
                LogManagerNotReady("ads show reward", "Ads");
                return;
            }

            string placement = ctx.GetParam("placement", DefaultPlacement);
            IntegrationGlobal.AdsManager.ShowRewardAds(null, placement);
        }

        private static void HandleAdsAppOpen(DirectCmdContext ctx)
        {
            if (!CheckAdsEnabled("ads show appopen"))
                return;

            if (IntegrationGlobal.AdsManager == null)
            {
                LogManagerNotReady("ads show appopen", "Ads");
                return;
            }

            string placement = ctx.GetParam("placement", DefaultPlacement);
            IntegrationGlobal.AdsManager.ShowAppOpenAds(null, placement);
        }
        #endregion

        #region Private Methods — IAP
        private static void RegisterIapCommands()
        {
            DirectCmdForwarding.RegisterCommand("iap")
                .WithSubcommand("buy", buy =>
                {
                    buy
                        .WithParameter<string>("product", required: true)
                        .DisablePositional()
                        .OnExecute(ctx => HandleIapBuy(ctx));
                });
        }

        private static void HandleIapBuy(DirectCmdContext ctx)
        {
            if (!CheckIapEnabled("iap buy"))
                return;

            if (IntegrationGlobal.InAppPurchaseManager == null)
            {
                LogManagerNotReady("iap buy", "In-App Purchase");
                return;
            }

            string productId = ctx.GetParam<string>("product");
            if (string.IsNullOrWhiteSpace(productId))
            {
                QuickLog.Error<DirectCmdDefaultCommands>(
                    "Missing required parameter '--product' for 'iap buy'.");
                return;
            }

            IntegrationGlobal.InAppPurchaseManager.BuyProduct(productId);
        }
        #endregion

        #region Private Methods — Tracking
        private static void RegisterTrackingCommands()
        {
            DirectCmdForwarding.RegisterCommand("tracking")
                .WithSubcommand("enabled", enabled =>
                {
                    enabled.WithSubcommand("on", on =>
                        {
                            on.OnExecute((DirectCmdContext _) => HandleTrackingEnabled(true));
                        })
                        .WithSubcommand("off", off =>
                        {
                            off.OnExecute((DirectCmdContext _) => HandleTrackingEnabled(false));
                        });
                })
                .WithSubcommand("filtered", filtered =>
                {
                    filtered.WithSubcommand("on", on =>
                        {
                            on.OnExecute((DirectCmdContext _) => HandleTrackingFiltered(true));
                        })
                        .WithSubcommand("off", off =>
                        {
                            off.OnExecute((DirectCmdContext _) => HandleTrackingFiltered(false));
                        });
                });
        }

        private static void HandleTrackingEnabled(bool enable)
        {
            string label = enable ? "tracking enabled on" : "tracking enabled off";

            if (!CheckTrackingEnabled(label))
                return;

            if (IntegrationGlobal.TrackingManager == null)
            {
                LogManagerNotReady(label, "Tracking");
                return;
            }

            IntegrationGlobal.TrackingManager.AllowTracking = enable;
            QuickLog.Info<DirectCmdDefaultCommands>(
                "Tracking {0}.",
                enable ? "enabled" : "disabled");
        }

        private static void HandleTrackingFiltered(bool enable)
        {
            string label = enable ? "tracking filtered on" : "tracking filtered off";

            if (!CheckTrackingFilterEnabled(label))
                return;

            if (IntegrationGlobal.TrackingManager == null)
            {
                LogManagerNotReady(label, "Tracking");
                return;
            }

            if (enable)
            {
                string deviceId = IntegrationGlobal.TrackingManager.DeviceTrackingIdentifier;
                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    IntegrationGlobal.TrackingManager.AssignFilteredTrackingDevices(deviceId);
                    QuickLog.Info<DirectCmdDefaultCommands>(
                        "Tracking filter enabled for device '{0}'.",
                        deviceId);
                }
                else
                {
                    QuickLog.Warning<DirectCmdDefaultCommands>(
                        "Cannot enable tracking filter: device tracking identifier is not set.");
                }
            }
            else
            {
                IntegrationGlobal.TrackingManager.AssignFilteredTrackingDevices();
                QuickLog.Info<DirectCmdDefaultCommands>(
                    "Tracking filter disabled.");
            }
        }
        #endregion

        #region Private Methods — Helpers
        private static bool CheckAdsEnabled(string commandLabel)
        {
            if (DirectCmdSettings.Exists && !DirectCmdSettings.Instance.EnableAdsCommands)
            {
                QuickLog.Warning<DirectCmdDefaultCommands>(
                    DisabledMessage, commandLabel);
                return false;
            }

            return true;
        }

        private static bool CheckIapEnabled(string commandLabel)
        {
            if (DirectCmdSettings.Exists && !DirectCmdSettings.Instance.EnableIapCommands)
            {
                QuickLog.Warning<DirectCmdDefaultCommands>(
                    DisabledMessage, commandLabel);
                return false;
            }

            return true;
        }

        private static bool CheckTrackingEnabled(string commandLabel)
        {
            if (DirectCmdSettings.Exists &&
                !DirectCmdSettings.Instance.EnableTrackingCommands)
            {
                QuickLog.Warning<DirectCmdDefaultCommands>(
                    DisabledMessage, commandLabel);
                return false;
            }

            return true;
        }

        private static bool CheckTrackingFilterEnabled(string commandLabel)
        {
            if (DirectCmdSettings.Exists &&
                !DirectCmdSettings.Instance.EnableTrackingFilterCommands)
            {
                QuickLog.Warning<DirectCmdDefaultCommands>(
                    DisabledMessage, commandLabel);
                return false;
            }

            return true;
        }

        private static void LogManagerNotReady(string commandLabel, string managerName)
        {
            QuickLog.Warning<DirectCmdDefaultCommands>(
                ManagerNotReadyMessage,
                commandLabel,
                managerName);
        }
        #endregion
    }
}
