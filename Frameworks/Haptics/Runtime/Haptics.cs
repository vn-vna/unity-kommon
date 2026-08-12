using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Static facade over <see cref="HapticManager"/>. All methods are null-safe
    /// and log via QuickLog. This is the fire-and-forget surface game code calls.
    ///
    /// Deliberately NOT a static class: QuickLog.Warning&lt;T&gt; requires a
    /// non-static generic type argument (CS0718); a private ctor keeps the
    /// surface purely static.
    /// </summary>
    public class Haptics
    {
        private Haptics() { }

        public static bool IsReady => HapticManager.Instance != null;

        public static HapticHandle PlayRhythm(string rhythmId, float intensityScale = 1f)
        {
            if (!EnsureReady("PlayRhythm")) return HapticHandle.Invalid;
            return HapticManager.Instance.PlayRhythm(rhythmId, intensityScale);
        }

        public static HapticHandle PlayRhythm(HapticRhythm rhythm, float intensityScale = 1f)
        {
            if (!EnsureReady("PlayRhythm")) return HapticHandle.Invalid;
            return HapticManager.Instance.PlayRhythm(rhythm, intensityScale);
        }

        public static void Stop(HapticHandle handle)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.Stop(handle);
            }
        }

        public static void Pause(HapticHandle handle)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.Pause(handle);
            }
        }

        public static void Resume(HapticHandle handle)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.Resume(handle);
            }
        }

        public static void StopAll()
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.StopAll();
            }
        }

        public static void Cue(
            HapticWaveformType type,
            float intensity = 1f,
            float durationSeconds = 0.05f)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.Cue(type, intensity, durationSeconds);
            }
        }

        public static bool Supports(HapticWaveformType type)
        {
            return IsReady && HapticManager.Instance.Supports(type);
        }

        #region Private Methods

        private static bool EnsureReady(string operation)
        {
            if (HapticManager.Instance == null)
            {
                QuickLog.Warning<Haptics>(
                    "{0} ignored: HapticManager not ready.", operation);
                return false;
            }
            return true;
        }

        #endregion
    }
}
