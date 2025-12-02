using System.Collections;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.FallAway.AC
{
    [AddComponentMenu("Scheherazade/Anti Cheat/Privacy Screen")]
    public sealed class PrivacyScreen
        : SingletonBehavior<PrivacyScreen>
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string UnityPlayerClass = "com.unity3d.player.UnityPlayer";
        const string UnityPlayerCurrentActivityField = "currentActivity";
        const string PrivacyCoverManagerClass = "com.hapiga.scheherazade.android.PrivacyCoverManager";
        const string ShowCoverMethod = "showCover";
        const string RemoveCoverMethod = "removeCover";
#endif

        protected override void Awake()
        {
            base.Awake();
        }

        private IEnumerator OnApplicationFocus(bool focus)
        {
#if UNITY_ANDROID && !UNITY_EDITOR && !DISABLE_ANTICHEAT
            try
            {
                using AndroidJavaClass unityPlayer = new AndroidJavaClass(UnityPlayerClass);
                using AndroidJavaClass cls = new AndroidJavaClass(PrivacyCoverManagerClass);
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>(UnityPlayerCurrentActivityField);
                cls.CallStatic(focus ? RemoveCoverMethod : ShowCoverMethod, activity);
            }
            catch (System.Exception e)
            {
                QuickLog.Critical<PrivacyScreen>(
                    $"Error while trying to set privacy screen: {e}"
                );
            }
#endif

#if UNITY_IOS && !UNITY_EDITOR
            QuickLog.Critical<PrivacyScreen>(
                "iOS privacy screen is not implemented yet."
            );
#endif
            yield return null; // Wait a frame to
        }
    }
}