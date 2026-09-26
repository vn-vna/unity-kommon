using System;

using Com.Scheherazade.Common.DataSync;

namespace Com.Scheherazade.Common.Integration.Segmentation
{
    [Serializable]
    [CurrentDataVersion("0.0.1")]
    public class SegmentationInformation
    {
        public string CampaignName;
        public string CreativeName;
        public string AdGroup;
        public string Network;
        public string Label;
        public string TrackerName;
        public string TrackerId;
    }
}