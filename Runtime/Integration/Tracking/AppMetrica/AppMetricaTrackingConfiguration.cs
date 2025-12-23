using UnityEngine;
using UnityEngine.Serialization;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [CreateAssetMenu(
        fileName = "AppMetricaTrackingConfiguration",
        menuName = "Dev Menu/Integration/AppMetrica Tracking Configuration"
    )]
    public class AppMetricaTrackingConfiguration : ScriptableObject
    {
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
        [FormerlySerializedAs("apiKey")]
        private string androidApiKey;

        [SerializeField]
        private string iosApiKey;

        [SerializeField]
        private float iapMultiplier;
    }
}