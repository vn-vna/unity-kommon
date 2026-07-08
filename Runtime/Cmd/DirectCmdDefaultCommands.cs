using System;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEditor;
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
            RegisterAdminCommands();
        }
        #endregion

        #region Private Methods — Ads
        private static void RegisterAdsCommands()
        {
            DirectCmdForwarding .RegisterCommand("ads")
                .WithSubcommand("show", ConfigureAdShowCommand)
                .DisablePositional();
        }

        private static void ConfigureAdShowCommand(
            DirectCmdCommandBuilder show
        ) => show.WithSubcommand("banner", ConfigureAdShowBannerCommand)
                .WithSubcommand("interstitial", ConfigureAdShowInterstitialCommand)
                .WithSubcommand("reward", ConfigureAdShowRewardCommand)
                .WithSubcommand("appopen", ConfigureAdShowAppOpenCommand)
                .DisablePositional();

        private static void ConfigureAdShowAppOpenCommand(
            DirectCmdCommandBuilder appopen
        ) 
            => appopen
                .WithParameter<string>(
                    "placement", "p",
                    defaultValue: DefaultPlacement)
                .OnExecute(HandleAdsAppOpen);

        private static void ConfigureAdShowRewardCommand(
            DirectCmdCommandBuilder reward
        ) => reward
                .WithParameter<string>(
                    "placement", "p",
                    defaultValue: DefaultPlacement
                )
                .OnExecute(HandleAdsReward);

        private static void ConfigureAdShowInterstitialCommand(
            DirectCmdCommandBuilder interstitial
        ) => interstitial
                .WithParameter<string>(
                    "placement", "p",
                    defaultValue: DefaultPlacement
                )
                .OnExecute(HandleAdsInterstitial);

        private static void ConfigureAdShowBannerCommand(
            DirectCmdCommandBuilder banner
        ) => banner.OnExecute(HandleAdsBanner);

        private static void HandleAdsBanner(DirectCmdContext ctx)
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
            IntegrationGlobal.AdsManager.ShowInterstitialAds(null, placement, true);
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
                .WithSubcommand("buy", ConfigureIapBuyCommand)
                .DisablePositional();
        }

        private static void ConfigureIapBuyCommand(
            DirectCmdCommandBuilder buy
        ) => buy
                .WithParameter<string>("product", required: true)
                .DisablePositional()
                .OnExecute(HandleIapBuy);

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
                .WithSubcommand("enabled", ConfigureTrackingEnabledCommand)
                .WithSubcommand("filtered", ConfigureTrackingFilteredCommand)
                .DisablePositional();
        }

        private static void ConfigureTrackingEnabledCommand(
            DirectCmdCommandBuilder enabled
        ) => enabled
                .WithSubcommand("on", ConfigureTrackingEnabledOnCommand)
                .WithSubcommand("off", ConfigureTrackingEnabledOffCommand)
                .DisablePositional();

        private static void ConfigureTrackingEnabledOnCommand(
            DirectCmdCommandBuilder on
        ) => on.OnExecute(HandleTrackingEnabledOn);

        private static void HandleTrackingEnabledOn(DirectCmdContext context)
            => SetTrackingEnabledStatus(true);

        private static void ConfigureTrackingEnabledOffCommand(
            DirectCmdCommandBuilder off
        ) => off.OnExecute(HandleTrackingEnabledOff);

        private static void HandleTrackingEnabledOff(DirectCmdContext context)
            => SetTrackingEnabledStatus(false);

        private static void ConfigureTrackingFilteredCommand(
            DirectCmdCommandBuilder filtered
        ) => filtered
                .WithSubcommand("on", ConfigureTrackingFilteredOnCommand)
                .WithSubcommand("off", ConfigureTrackingFilteredOffCommand)
                .DisablePositional();

        private static void ConfigureTrackingFilteredOnCommand(
            DirectCmdCommandBuilder on
        ) => on.OnExecute(HandleConfigureTrackingFilteredOn);

        private static void ConfigureTrackingFilteredOffCommand(
            DirectCmdCommandBuilder off
        ) => off.OnExecute(HandleConfigureTrackingFilteredOff);

        private static void HandleConfigureTrackingFilteredOn(DirectCmdContext context)
            => SetTrackingFilteredStatus(true);

        private static void HandleConfigureTrackingFilteredOff(DirectCmdContext context)
            => SetTrackingFilteredStatus(false);
        private static void SetTrackingEnabledStatus(bool enable)
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

        private static void SetTrackingFilteredStatus(bool enable)
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

        #region Private Methods — Admin
        private static void RegisterAdminCommands()
        {
            DirectCmdForwarding.RegisterCommand("admin")
                .WithSubcommand("execute", ConfigureAdminExecuteCommand)
                .DisablePositional();
        }

        private static void ConfigureAdminExecuteCommand(
            DirectCmdCommandBuilder execute
        ) => execute.OnExecute(HandleAdminExecute);

        private static void HandleAdminExecute(DirectCmdContext ctx)
        {
            string methodPath = ctx.GetPositional(0);
            if (string.IsNullOrWhiteSpace(methodPath))
            {
                QuickLog.Error<DirectCmdDefaultCommands>(
                    "Missing method path. Usage: admin execute <Namespace.Class.Method>");
                return;
            }

            int lastDot = methodPath.LastIndexOf('.');
            if (lastDot <= 0 || lastDot >= methodPath.Length - 1)
            {
                QuickLog.Error<DirectCmdDefaultCommands>(
                    "Invalid method path '{0}'. Expected: Namespace.Class.Method",
                    methodPath);
                return;
            }

            string typeName = methodPath.Substring(0, lastDot);
            string methodName = methodPath.Substring(lastDot + 1);

            try
            {
                System.Type type = ResolveType(typeName);
                if (type == null)
                {
                    QuickLog.Error<DirectCmdDefaultCommands>(
                        "Type '{0}' not found.", typeName);
                    return;
                }

                System.Reflection.MethodInfo method = type.GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static,
                    null,
                    System.Type.EmptyTypes,
                    null);

                if (method == null)
                {
                    QuickLog.Error<DirectCmdDefaultCommands>(
                        "Static parameterless method '{0}' not found on '{1}'.",
                        methodName,
                        typeName);
                    return;
                }

                method.Invoke(null, null);
                QuickLog.Info<DirectCmdDefaultCommands>(
                    "Executed '{0}.{1}'.",
                    typeName,
                    methodName);
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                QuickLog.Error<DirectCmdDefaultCommands>(
                    "Exception in '{0}.{1}': {2}",
                    typeName,
                    methodName,
                    ex.InnerException?.Message ?? ex.Message);
            }
            catch (System.Exception ex)
            {
                QuickLog.Error<DirectCmdDefaultCommands>(
                    "Failed to execute '{0}.{1}': {2}",
                    typeName,
                    methodName,
                    ex.Message);
            }
        }

        private static System.Type ResolveType(string typeName)
        {
            System.Type type = System.Type.GetType(typeName);
            if (type != null)
                return type;

            foreach (System.Reflection.Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null)
                    return type;
            }

            return null;
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
