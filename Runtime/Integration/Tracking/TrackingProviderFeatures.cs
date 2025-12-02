using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [Flags]
    public enum TrackingProviderFeatures
    {
        None = 0,
        IngameAction = 1 << 0,
        PurchaseRevenue = 1 << 2,
        AdRevenue = 1 << 3,
        ScreenView = 1 << 4,
        AllFeatures = IngameAction | PurchaseRevenue | AdRevenue | ScreenView,
        Revenue = PurchaseRevenue | AdRevenue,
        Activity = IngameAction | ScreenView
    }
}