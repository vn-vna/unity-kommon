using System.Collections;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.FallAway.AC
{
    /// <summary>
    /// Provides privacy screen functionality for anti-cheat purposes by displaying a cover when the application loses focus.
    /// </summary>
    /// <remarks>
    /// This component is a singleton that manages the display of a privacy cover on Android devices
    /// to prevent screen recording or capturing of sensitive game data when the app is in the background.
    /// Currently supports Android platform with iOS implementation pending.
    /// </remarks>
    /// <example>
    /// <code>
    /// // PrivacyScreen is automatically managed as a singleton
    /// // Add to a GameObject in your scene:
    /// GameObject privacyManager = new GameObject("PrivacyScreen");
    /// privacyManager.AddComponent&lt;PrivacyScreen&gt;();
    /// 
    /// // The screen cover will automatically show/hide based on app focus
    /// </code>
    /// </example>
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