namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    /// <summary>
    /// Marker interface for event data payloads that can be queued
    /// in the tracking event pipeline.
    /// </summary>
    public interface ITrackingData { }

    public struct ScreenTrackingData : ITrackingData
    {
        public string ScreenId;
    }
}
