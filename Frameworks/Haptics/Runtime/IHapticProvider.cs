namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Contract for a haptic provider. Implemented by ScriptableObjects;
    /// the manager stays provider-agnostic and only talks through this interface.
    /// </summary>
    public interface IHapticProvider
    {
        string ProviderId { get; }
        bool IsAvailable { get; }

        /// <summary>
        /// Capability query — lets the manager downgrade
        /// (e.g. Heavy → Medium) automatically.
        /// </summary>
        bool SupportsWaveform(HapticWaveformType type);

        /// <summary>Called once at manager Awake; warm the native bridge.</summary>
        void Initialize();

        /// <summary>One-shot fire of a single keyframe (e.g. button taps).</summary>
        void Cue(HapticKeyframe keyframe, float intensityScale = 1f);

        /// <summary>Start a long-buzz driver; returns a token to address it.</summary>
        void BeginContinuous(HapticKeyframe keyframe, out int tokenId);

        /// <summary>Retune an active continuous driver (intensity/frequency).</summary>
        void UpdateContinuous(int tokenId, HapticKeyframe keyframe);

        /// <summary>Stop the addressed continuous driver.</summary>
        void EndContinuous(int tokenId);

        /// <summary>Hard-stop everything the provider knows about (used by StopAll).</summary>
        void CancelAll();
    }
}
