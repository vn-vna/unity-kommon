using System.Collections;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public interface ITrackingManager
    {
        List<ITrackingProvider> Providers { get; }
        string SessionPlayId { get; }
        int CurrentStage { get; }
        bool AllowTracking { get; set; }
        TrackingManagerStatus Status { get; }
        void Initialize(int currentStage, float timeOut);
        IEnumerator InitializeCoroutine(int currentStage, float timeOut);
        void Shutdown();

        void TrackScreen(string screenId);
        void TrackAction(TrackingActionInfo info);
        void TrackAdRevenue(AdTrackingInfo info);
        void TrackPurchaseRevenue(PurchaseTrackingInfo info);

        void TrackAction(string action, params (string key, object value)[] parameters)
        {
            TrackAction(new TrackingActionInfo
            {
                ActionId = action,
                Parameters = TrackingActionInfo.CreateParametersDictionary(parameters)
            });
        }
    }
}