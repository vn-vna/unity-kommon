namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    internal enum TrackingEventType : byte
    {
        Screen,
        Action,
        AdRevenue,
        PurchaseRevenue
    }

    internal readonly struct TrackingEventData
    {
        public readonly TrackingEventType Type;
        public readonly ITrackingData Data;

        private TrackingEventData(TrackingEventType type, ITrackingData data)
        {
            Type = type;
            Data = data;
        }

        public static TrackingEventData Screen(string screenId)
            => new(TrackingEventType.Screen, new ScreenTrackingData { ScreenId = screenId });

        public static TrackingEventData Action(TrackingActionInfo info)
            => new(TrackingEventType.Action, info);

        public static TrackingEventData AdRevenue(AdTrackingInfo info)
            => new(TrackingEventType.AdRevenue, info);

        public static TrackingEventData PurchaseRevenue(PurchaseTrackingInfo info)
            => new(TrackingEventType.PurchaseRevenue, info);
    }
}
