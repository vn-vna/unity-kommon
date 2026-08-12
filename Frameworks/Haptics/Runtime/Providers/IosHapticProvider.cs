using System.Runtime.InteropServices;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// iOS provider — CHHapticEngine (iOS 13+) with
    /// UIImpact/Notification/Selection generators and legacy
    /// AudioServices impact ids (1519/1520/1521) as fallback.
    /// Simulator degrades gracefully: IsAvailable = false, no crash.
    /// </summary>
    [CreateAssetMenu(menuName = "Scheherazade/Haptics/iOS Haptic Provider")]
    public class IosHapticProvider : ScriptableObject, IHapticProvider
    {
        #region Private Fields

        private bool _isAvailable;

        #endregion

        #region Properties

        public string ProviderId => nameof(IosHapticProvider);

        public bool IsAvailable => _isAvailable;

        #endregion

        #region Public Methods

        public bool SupportsWaveform(HapticWaveformType type)
        {
            // Heavy may be unsupported on older devices; downgrade handled by the manager.
            return type != HapticWaveformType.HeavyImpact
                || NativeHapticBridge.haptic_supportsHeavy();
        }

        public void Initialize()
        {
            _isAvailable = false;

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                _isAvailable = NativeHapticBridge.haptic_isAvailable();
            }
            catch (System.Exception ex)
            {
                QuickLog.Error<IosHapticProvider>(
                    "Native haptic availability check failed: {0}", ex);
            }

            QuickLog.Info<IosHapticProvider>(
                "iOS haptic provider initialized. Available: {0}", _isAvailable);
#else
            QuickLog.Info<IosHapticProvider>(
                "iOS provider inactive (non-iOS or editor).");
#endif
        }

        public void Cue(HapticKeyframe keyframe, float intensityScale = 1f)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_isAvailable) return;

            float intensity = Mathf.Clamp01(keyframe.Intensity * intensityScale);
            try
            {
                NativeHapticBridge.haptic_cue((int)keyframe.Waveform, intensity);
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<IosHapticProvider>(
                    "Native cue failed: {0}", ex.Message);
            }
#else
            QuickLog.Debug<IosHapticProvider>(
                "[Simulated] Cue {0} int={1:F2} dur={2:F2}s",
                keyframe.Waveform, keyframe.Intensity * intensityScale,
                keyframe.DurationSeconds);
#endif
        }

        public void BeginContinuous(HapticKeyframe keyframe, out int tokenId)
        {
            tokenId = -1;

#if UNITY_IOS && !UNITY_EDITOR
            if (!_isAvailable) return;

            try
            {
                tokenId = Random.Range(1, int.MaxValue);
                NativeHapticBridge.haptic_beginContinuous(tokenId, keyframe.Intensity);
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<IosHapticProvider>(
                    "Native beginContinuous failed: {0}", ex.Message);
                tokenId = -1;
            }
#else
            QuickLog.Debug<IosHapticProvider>(
                "[Simulated] BeginContinuous int={0:F2} dur={1:F2}s",
                keyframe.Intensity, keyframe.DurationSeconds);
#endif
        }

        public void UpdateContinuous(int tokenId, HapticKeyframe keyframe)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_isAvailable || tokenId < 0) return;

            try
            {
                NativeHapticBridge.haptic_updateContinuous(tokenId, keyframe.Intensity);
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<IosHapticProvider>(
                    "Native updateContinuous failed: {0}", ex.Message);
            }
#else
            // no-op in editor / non-iOS
#endif
        }

        public void EndContinuous(int tokenId)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (tokenId < 0) return;

            try
            {
                NativeHapticBridge.haptic_endContinuous(tokenId);
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<IosHapticProvider>(
                    "Native endContinuous failed: {0}", ex.Message);
            }
#else
            // no-op in editor / non-iOS
#endif
        }

        public void CancelAll()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                NativeHapticBridge.haptic_cancelAll();
            }
            catch (System.Exception ex)
            {
                QuickLog.Warning<IosHapticProvider>(
                    "Native cancelAll failed: {0}", ex.Message);
            }
#else
            // no-op in editor / non-iOS
#endif
        }

        #endregion
    }

    /// <summary>
    /// P/Invoke surface for the iOS native bridge (HapticBridge.mm).
    /// </summary>
    internal static class NativeHapticBridge
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        public static extern bool haptic_isAvailable();

        [DllImport("__Internal")]
        public static extern bool haptic_supportsHeavy();

        [DllImport("__Internal")]
        public static extern void haptic_cue(int type, float intensity);

        [DllImport("__Internal")]
        public static extern void haptic_beginContinuous(int tokenId, float intensity);

        [DllImport("__Internal")]
        public static extern void haptic_updateContinuous(int tokenId, float intensity);

        [DllImport("__Internal")]
        public static extern void haptic_endContinuous(int tokenId);

        [DllImport("__Internal")]
        public static extern void haptic_cancelAll();
#else
        public static bool haptic_isAvailable() => false;
        public static bool haptic_supportsHeavy() => false;
        public static void haptic_cue(int type, float intensity) { }
        public static void haptic_beginContinuous(int tokenId, float intensity) { }
        public static void haptic_updateContinuous(int tokenId, float intensity) { }
        public static void haptic_endContinuous(int tokenId) { }
        public static void haptic_cancelAll() { }
#endif
    }
}
