using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Android provider — native VibrationEffect API ladder with
    /// Handheld.Vibrate fallback. Calls
    /// <c>com.hapiga.scheherazade.android.HapticEngine</c> (mirrors the
    /// NativeDialogue pattern). Requires the VIBRATE permission in
    /// Assets/Plugins/Android/AndroidManifest.xml.
    /// </summary>
    [CreateAssetMenu(menuName = "Scheherazade/Haptics/Android Haptic Provider")]
    public class AndroidHapticProvider : ScriptableObject, IHapticProvider
    {
        #region Constants

        private const string HapticEngineClass =
            "com.hapiga.scheherazade.android.HapticEngine";

        private const int MaxAmplitude = 255;

        #endregion

        #region Serialized Fields

        [Tooltip("Fall back to Handheld.Vibrate when the native path fails.")]
        [SerializeField] private bool _useHandheldFallback = true;

        #endregion

        #region Private Fields

        private bool _isAvailable;

        #endregion

        #region Properties

        public string ProviderId => nameof(AndroidHapticProvider);

        public bool IsAvailable => _isAvailable;

        #endregion

        #region Public Methods

        public bool SupportsWaveform(HapticWaveformType type) => true;

        public void Initialize()
        {
            _isAvailable = false;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass hapticEngine =
                       new AndroidJavaClass(HapticEngineClass))
                {
                    using (AndroidJavaObject activity =
                           GetUnityActivity())
                    {
                        _isAvailable = activity != null
                            && hapticEngine.CallStatic<bool>(
                                "isAvailable", activity);
                    }
                }
            }
            catch (System.Exception ex)
            {
                QuickLog.Error<AndroidHapticProvider>(
                    "Native HapticEngine availability check failed: {0}", ex);
            }

            QuickLog.Info<AndroidHapticProvider>(
                "Android haptic provider initialized. Available: {0}", _isAvailable);
#else
            QuickLog.Info<AndroidHapticProvider>(
                "Android provider inactive (non-Android or editor).");
#endif
        }

        public void Cue(HapticKeyframe keyframe, float intensityScale = 1f)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_isAvailable) return;

            int durationMs = Mathf.Max(1, Mathf.RoundToInt(
                keyframe.DurationSeconds * 1000f));
            int amplitude = Mathf.RoundToInt(
                Mathf.Clamp01(keyframe.Intensity * intensityScale) * MaxAmplitude);

            try
            {
                using (AndroidJavaClass hapticEngine =
                       new AndroidJavaClass(HapticEngineClass))
                {
                    using (AndroidJavaObject activity = GetUnityActivity())
                    {
                        hapticEngine.CallStatic(
                            "cue", activity, durationMs, amplitude,
                            (int)keyframe.Waveform);
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (_useHandheldFallback)
                {
                    Handheld.Vibrate();
                }
                QuickLog.Warning<AndroidHapticProvider>(
                    "Native cue failed ({0}); fell back to Handheld.Vibrate.", ex.Message);
            }
#else
            QuickLog.Debug<AndroidHapticProvider>(
                "[Simulated] Cue {0} int={1:F2} dur={2:F2}s",
                keyframe.Waveform, keyframe.Intensity * intensityScale,
                keyframe.DurationSeconds);
#endif
        }

        public void BeginContinuous(HapticKeyframe keyframe, out int tokenId)
        {
            tokenId = -1;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_isAvailable) return;

            int durationMs = Mathf.Max(1, Mathf.RoundToInt(
                keyframe.DurationSeconds * 1000f));
            int amplitude = Mathf.RoundToInt(
                Mathf.Clamp01(keyframe.Intensity) * MaxAmplitude);

            try
            {
                tokenId = Random.Range(1, int.MaxValue);
                using (AndroidJavaClass hapticEngine =
                       new AndroidJavaClass(HapticEngineClass))
                {
                    using (AndroidJavaObject activity = GetUnityActivity())
                    {
                        hapticEngine.CallStatic(
                            "beginContinuous", activity, tokenId, durationMs, amplitude);
                    }
                }
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<AndroidHapticProvider>(
                    "Native beginContinuous failed: {0}", ex.Message);
                tokenId = -1;
            }
#else
            QuickLog.Debug<AndroidHapticProvider>(
                "[Simulated] BeginContinuous int={0:F2} dur={1:F2}s",
                keyframe.Intensity, keyframe.DurationSeconds);
#endif
        }

        public void UpdateContinuous(int tokenId, HapticKeyframe keyframe)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_isAvailable || tokenId < 0) return;

            int durationMs = Mathf.Max(1, Mathf.RoundToInt(
                keyframe.DurationSeconds * 1000f));
            int amplitude = Mathf.RoundToInt(
                Mathf.Clamp01(keyframe.Intensity) * MaxAmplitude);

            try
            {
                using (AndroidJavaClass hapticEngine =
                       new AndroidJavaClass(HapticEngineClass))
                {
                    using (AndroidJavaObject activity = GetUnityActivity())
                    {
                        hapticEngine.CallStatic(
                            "updateContinuous", activity, tokenId, durationMs, amplitude);
                    }
                }
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<AndroidHapticProvider>(
                    "Native updateContinuous failed: {0}", ex.Message);
            }
#else
            // no-op in editor / non-Android
#endif
        }

        public void EndContinuous(int tokenId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (tokenId < 0) return;

            try
            {
                using (AndroidJavaClass hapticEngine =
                       new AndroidJavaClass(HapticEngineClass))
                {
                    hapticEngine.CallStatic("endContinuous", tokenId);
                }
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<AndroidHapticProvider>(
                    "Native endContinuous failed: {0}", ex.Message);
            }
#else
            // no-op in editor / non-Android
#endif
        }

        public void CancelAll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass hapticEngine =
                       new AndroidJavaClass(HapticEngineClass))
                {
                    using (AndroidJavaObject activity = GetUnityActivity())
                    {
                        hapticEngine.CallStatic("cancelAll", activity);
                    }
                }
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<AndroidHapticProvider>(
                    "Native cancelAll failed: {0}", ex.Message);
            }
#else
            // no-op in editor / non-Android
#endif
        }

        #endregion

        #region Private Methods

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject GetUnityActivity()
        {
            using (AndroidJavaClass unityPlayer =
                   new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }
#endif

        #endregion
    }
}
