using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    // Simple container for iAP purchase event data
    public struct IAPPurchaseEventInfo
    {
        public int StageNumber { get; set; }
        public string OrderId { get; set; }
        public string ProductId { get; set; }
        public double Price { get; set; }
        public double Value { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public string FailureReason { get; set; }
        // Optional current_stage for non-ingame contexts
        public int? CurrentStage { get; set; }
    }
}
