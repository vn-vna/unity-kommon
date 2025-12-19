using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    [CreateAssetMenu(fileName = "AdsConfiguration", menuName = "Dev Menu/Integration/Ads/Ads Configuration")]
    public class AdsConfiguration : ScriptableObject
    {
        public float ShowInterstitialAdsInterval => showInterstitialAdsInterval;

        public InterResetType InterAdsIntervalResetType => interAdsIntervalResetType;

        [SerializeField]
        private float showInterstitialAdsInterval = 120.0f;

        [SerializeField]
        private InterResetType interAdsIntervalResetType = InterResetType.OnRewardedAdsComplete;

    }

    [Serializable]
    public enum InterResetType
    {
        OnRewardedAdsShow,
        OnRewardedAdsComplete
    }
}