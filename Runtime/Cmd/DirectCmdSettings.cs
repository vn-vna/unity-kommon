using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common
{
    public sealed class DirectCmdSettings : SingletonScriptableObject<DirectCmdSettings>
    {
        #region Interfaces & Properties
        public static bool Exists
        {
            get
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
                    return Instance != null;

                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:DirectCmdSettings");
                return guids.Length > 0;
#else
                return Instance != null;
#endif
            }
        }
        #endregion

        #region Serialized Fields — Ticker Configuration
#if UNITY_EDITOR
        [Tooltip("When enabled, the command file is polled every frame. " +
                 "Disable to use interval-based polling for better performance.")]
#endif
        [SerializeField]
        private bool pollEveryFrame = true;

#if UNITY_EDITOR
        [Tooltip("Polling interval in seconds when not polling every frame.")]
#endif
        [SerializeField]
        [Min(0.05f)]
        private float pollInterval = 0.5f;
        #endregion

        #region Serialized Fields — Default Command Toggles
#if UNITY_EDITOR
        [Tooltip("Enable the 'ads show <type>' commands for showing ads via DCF.")]
#endif
        [SerializeField]
        private bool enableAdsCommands = true;

#if UNITY_EDITOR
        [Tooltip("Enable the 'iap buy --product <id>' command for IAP via DCF.")]
#endif
        [SerializeField]
        private bool enableIapCommands = true;

#if UNITY_EDITOR
        [Tooltip("Enable the 'tracking enabled on/off' command for toggling tracking.")]
#endif
        [SerializeField]
        private bool enableTrackingCommands = true;

#if UNITY_EDITOR
        [Tooltip("Enable the 'tracking filtered on/off' command for toggling tracking filters.")]
#endif
        [SerializeField]
        private bool enableTrackingFilterCommands = true;
        #endregion

        #region Public Accessors
        public bool PollEveryFrame => pollEveryFrame;
        public float PollInterval => pollInterval;
        public bool EnableAdsCommands => enableAdsCommands;
        public bool EnableIapCommands => enableIapCommands;
        public bool EnableTrackingCommands => enableTrackingCommands;
        public bool EnableTrackingFilterCommands => enableTrackingFilterCommands;
        #endregion

        #region Unity Callbacks
#if UNITY_EDITOR
        private void OnValidate()
        {
            pollInterval = Mathf.Max(0.05f, pollInterval);
        }
#endif
        #endregion
    }
}
