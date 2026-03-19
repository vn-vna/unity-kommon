using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    [Serializable]
    public struct AdImpressionTrackingInfo
    {
        public int StageNumber { get; set; }
        public string AdPlatform { get; set; }
        public string AdSource { get; set; }
        public string AdFormat { get; set; }
        public string AdUnitName { get; set; }
        public string AdCreative { get; set; }
        public string Placement { get; set; }
        public double Value { get; set; }
        public string Currency { get; set; }
        public string Precision { get; set; }
        public int? CurrentStage { get; set; }

    }
}