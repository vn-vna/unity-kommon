using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    public static class ApplovinMaxAdsTrackingEventDefaults
    {
        #region Public Methods

        public static (string ActionId, ActionSeverity Severity) Get(
            ApplovinMaxAdsTrackingEventType type)
        {
            if (TryGet(type, out var defaults))
            {
                return defaults;
            }

            throw new ArgumentException(
                $"No default tracking event configured for '{type}'.",
                nameof(type)
            );
        }

        public static bool TryGet(
            ApplovinMaxAdsTrackingEventType type,
            out (string ActionId, ActionSeverity Severity) defaults)
            => _defaults.TryGetValue(type, out defaults);

        #endregion

        #region Private Fields

        private static readonly IReadOnlyDictionary<ApplovinMaxAdsTrackingEventType, (string ActionId, ActionSeverity Severity)> _defaults =
            new Dictionary<ApplovinMaxAdsTrackingEventType, (string ActionId, ActionSeverity Severity)>
            {
                { ApplovinMaxAdsTrackingEventType.AppOpenCallLoad, ("AdsAppOpen_CallLoad", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.AppOpenLoaded, ("AdsAppOpen_Loaded", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.AppOpenFailedToLoad, ("AdsAppOpen_FailedToLoad", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.AppOpenDisplayed, ("AdsAppOpen_Displayed", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.AppOpenDisplayFailed, ("AdsAppOpen_DisplayFailed", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.AppOpenHidden, ("AdsAppOpen_Hidden", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.AppOpenClicked, ("AdsAppOpen_Clicked", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.AppOpenRevenuePaid, ("AdsAppOpen_RevenuePaid", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.AppOpenCallShow, ("AdsAppOpen_CallShow", ActionSeverity.Debug) },

                { ApplovinMaxAdsTrackingEventType.InterCallLoad, ("AdsInter_CallLoad", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.InterLoaded, ("AdsInter_Loaded", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.InterFailedToLoad, ("AdsInter_FailedToLoad", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.InterDisplayed, ("AdsInter_Displayed", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.InterDisplayFailed, ("AdsInter_DisplayFailed", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.InterHidden, ("AdsInter_Hidden", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.InterClicked, ("AdsInter_Clicked", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.InterRevenuePaid, ("AdsInter_RevenuePaid", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.InterCallShow, ("AdsInter_CallShow", ActionSeverity.Debug) },

                { ApplovinMaxAdsTrackingEventType.RewardCallLoad, ("AdsReward_CallLoad", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardLoaded, ("AdsReward_Loaded", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardFailedToLoad, ("AdsReward_FailedToLoad", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardDisplayed, ("AdsReward_Displayed", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardDisplayFailed, ("AdsReward_DisplayFailed", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardHidden, ("AdsReward_Hidden", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardClicked, ("AdsReward_Clicked", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardRevenuePaid, ("AdsReward_RevenuePaid", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardRewardReceived, ("AdsReward_RewardReceived", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.RewardCallShow, ("AdsReward_CallShow", ActionSeverity.Debug) },

                { ApplovinMaxAdsTrackingEventType.BannerCallLoad, ("AdsBanner_CallLoad", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.BannerLoaded, ("AdsBanner_Loaded", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.BannerFailedToLoad, ("AdsBanner_FailedToLoad", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.BannerDisplayed, ("AdsBanner_Displayed", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.BannerDisplayFailed, ("AdsBanner_DisplayFailed", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.BannerHidden, ("AdsBanner_Hidden", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.BannerClicked, ("AdsBanner_Clicked", ActionSeverity.Debug) },
                { ApplovinMaxAdsTrackingEventType.BannerRevenuePaid, ("AdsBanner_RevenuePaid", ActionSeverity.Debug) },
            };

        #endregion
    }
}
