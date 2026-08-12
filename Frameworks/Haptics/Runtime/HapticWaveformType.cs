namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Drives iOS generator selection and Android waveform choice.
    /// <see cref="Custom"/> falls through to raw intensity / frequency / duration.
    /// </summary>
    public enum HapticWaveformType
    {
        LightImpact,         // UIImpactFeedbackGenerator.Light / light click
        MediumImpact,        // UIImpactFeedbackGenerator.Medium
        HeavyImpact,         // UIImpactFeedbackGenerator.Heavy (may be unsupported)
        NotificationSuccess, // UINotificationFeedbackGenerator success
        NotificationWarning, // UINotificationFeedbackGenerator warning
        NotificationError,   // UINotificationFeedbackGenerator error
        SelectionTick,       // UISelectionFeedbackGenerator.SelectionChanged
        VibrationBurst,      // continuous long-buzz driver (Begin/Update/End)
        Custom               // raw intensity / frequency / duration
    }
}
