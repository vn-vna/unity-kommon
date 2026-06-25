using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public interface ITrackingProvider
    {
        ITrackingManager TrackingManager { get; set; }
        bool IsInitialized { get; }
        bool IsTrackingEnabled { get; }
        TrackingProviderFeatures Features { get; }
        TrackingProviderFeatures EnabledFeatures { get; }
        int Priority { get; }
        ActionSeverity MinimumActionSeverity { get; }
        ProviderIdentity ProviderIdentity { get; }

        void Initialize();
        void CleanUp();

        void TrackScreen(string screenId);
        void TrackAction(TrackingActionInfo info);
        void TrackPurchaseRevenue(PurchaseTrackingInfo info);
        void TrackAdRevenue(AdTrackingInfo info);
    }
}