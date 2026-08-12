using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Baseline provider: everything no-ops. Used for unsupported devices and
    /// editor-only runs so the system never crashes.
    /// </summary>
    [CreateAssetMenu(menuName = "Scheherazade/Haptics/Null Haptic Provider")]
    public class NullHapticProvider : ScriptableObject, IHapticProvider
    {
        #region Properties

        public string ProviderId => nameof(NullHapticProvider);
        public bool IsAvailable => false;

        #endregion

        #region Public Methods

        public bool SupportsWaveform(HapticWaveformType type) => false;

        public void Initialize() { }

        public void Cue(HapticKeyframe keyframe, float intensityScale = 1f) { }

        public void BeginContinuous(HapticKeyframe keyframe, out int tokenId)
        {
            tokenId = -1;
        }

        public void UpdateContinuous(int tokenId, HapticKeyframe keyframe) { }

        public void EndContinuous(int tokenId) { }

        public void CancelAll() { }

        #endregion
    }
}
