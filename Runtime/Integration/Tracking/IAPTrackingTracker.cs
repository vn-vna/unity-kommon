using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    // Central hub to log iap_purchase events
    public static class IAPTrackingTracker
    {
        public static void TrackIAPurchase(IAPPurchaseEventInfo info)
        {
            var parameters = new Dictionary<string, object>
            {
                { "stage_number", info.StageNumber },
                { "order_id", info.OrderId },
                { "product_id", info.ProductId },
                { "price", info.Price },
                { "value", info.Value },
                { "currency", info.Currency },
                { "status", info.Status },
                { "failure_reason", info.FailureReason ?? "" }
            };
            if (info.CurrentStage.HasValue)
            {
                parameters["current_stage"] = info.CurrentStage.Value;
            }

            Integration.TrackingManager?.TrackAction(new TrackingActionInfo
            {
                ActionId = "iap_purchase",
                Parameters = parameters
            });
        }
    }
}
