using System;

using Com.Hapiga.Scheherazade.Common.LocalSave;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    [Serializable]
    [CurrentDataVersion("0.0.1")]
    public class SegmentationInformation
        : IVersionedData
    {
        public string CampaignName;
        public string CreativeName;
        public string CreativeHash;
        public string CampaignHash;
    }
}