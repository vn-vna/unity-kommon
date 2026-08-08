namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Drift audio/haptics — replaces drop-car <c>AudioClipInfo</c>/<c>HapticPattern</c>.
    /// Null-safe: when a board has no provider assigned, no feedback plays.
    /// </summary>
    public interface IGridFeedbackProvider
    {
        void PlayDriftStart(IDrifter drifter);
        void PlayDriftRelease(IDrifter drifter);
        void PlayDriftFail(IDrifter drifter);
    }
}
