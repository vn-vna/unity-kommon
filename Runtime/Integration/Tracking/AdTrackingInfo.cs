using Com.Hapiga.Scheherazade.Common.Integration.Ads;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public struct AdTrackingInfo
    {
        public IAdsServiceProvider Provider { get; set; }
        public AdsType AdType { get; set; }
        public double Revenue { get; set; }
        public string NetworkName { get; set; }
        public string RevenueUnit { get; set; }
        public string Placement { get; set; }
        public string AdFormat { get; set; }
        public string CreativeIdentifier { get; set; }
        public string Country { get; set; }
    }
}