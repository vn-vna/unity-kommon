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
        InterstitialAdsDisplayRevenue = 1 << 4,
        RewardedAdsDisplayRevenue = 1 << 5,
        AppOpenAdsDisplayRevenue = 1 << 6,
        IapRevenue = 1 << 7,
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

        [SerializeField]
        private FirebaseRevenueTrackingEvent[] adsRevenueTrackingConfig;

        [SerializeField]
        private float iapMultiplier = 0.7f;
    }
}