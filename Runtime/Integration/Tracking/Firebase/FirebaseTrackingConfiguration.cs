using System;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
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
        fileName = "FirebaseTrackingConfiguration",
        menuName = "Dev Menu/Integration/Firebase Tracking Configuration"
    )]
    public class FirebaseTrackingConfiguration  : 
        ScriptableObject
    {
        public FirebaseRevenueTrackingEvent[] RevenueTrackingConfig => adsRevenueTrackingConfig;
        public float IapMultiplier => iapMultiplier;
        public ActionSeverity MinimumActionSeverity => minimumActionSeverity;

        [SerializeField]
        private FirebaseRevenueTrackingEvent[] adsRevenueTrackingConfig;

        [SerializeField]
        private float iapMultiplier = 0.7f;

        [SerializeField]
        private ActionSeverity minimumActionSeverity = ActionSeverity.Debug;
    }
}