using System;
using UnityEngine;

[Serializable]
public enum AdjustEnvironment
{
    Sandbox,
    Production
}

[Serializable]
public enum AdjustLogLevel
{
    Verbose = 1,
    Debug,
    Info,
    Warn,
    Error,
    Assert,
    Suppress
}

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [CreateAssetMenu(
        fileName = "AdjustConfiguration", 
        menuName = "Dev Menu/Integration/Adjust Tracking Configuration"
    )]
    public class AdjustConfiguration : ScriptableObject
    {
        public string AppToken
        {
            get
            {
#if UNITY_ANDROID
                return androidAppToken;
#elif UNITY_IOS
                return iosAppToken;
#else
                return string.Empty;
#endif
            }
        }

        public string EventPrefix
        {
            get
            {
#if UNITY_ANDROID
                return androidEventPrefix;
#elif UNITY_IOS
                return iosEventPrefix;
#else
                return string.Empty;
#endif
            }
        }

        public AdjustEnvironment Environment => environment;
        public AdjustLogLevel LogLevel => logLevel;
        public string IapEventName
        {
            get
            {
#if UNITY_ANDROID
                return androidIapEventName;
#elif UNITY_IOS
                return iosIapEventName;
#else
                return string.Empty;
#endif
            }
        }
        public string AdsEventName => adsEventName;
        public float IapMultiplier => iapMultiplier;

        [SerializeField]
        private string androidAppToken;

        [SerializeField]
        private string iosAppToken;

        [SerializeField]
        private string androidEventPrefix;

        [SerializeField]
        private string iosEventPrefix;

        [SerializeField]
        private AdjustEnvironment environment;

        [SerializeField]
        private string androidIapEventName;

        [SerializeField]
        private string iosIapEventName;

        [SerializeField]
        private string adsEventName;

        [SerializeField]
        private float iapMultiplier;

        [SerializeField]
        private AdjustLogLevel logLevel = AdjustLogLevel.Verbose;
    }
}
