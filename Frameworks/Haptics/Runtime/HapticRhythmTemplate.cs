namespace Com.Hapiga.Scheherazade.Common.Haptics
{
    /// <summary>
    /// Built-in rhythm presets. Named after the game feel they produce.
    /// </summary>
    public enum HapticRhythmTemplate
    {
        Custom,              // blank timeline, designer builds it
        LightTap,            // UI buttons, subtle feedback
        MediumTap,           // tile moves, board interactions
        HeavyTap,            // matches, big events
        AlertTap,            // wrong move, warnings
        SelectionTick,       // list scroll, snapping
        NotificationSuccess,
        NotificationWarning,
        NotificationError,
        DoubleTap,           // confirmation
        TriplePulse,         // combo chain
        Burst                // explosion, danger (continuous)
    }
}
