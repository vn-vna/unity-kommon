using System;
using System.Text.RegularExpressions;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    [Serializable]
    public class SegmentationMatchingConfiguration
    {
        public SegmentationField Field;
        public string MatchPattern;

        internal bool Matches(SegmentationInformation info)
        {
            switch (Field)
            {
                case SegmentationField.CampaignName:
                    return TryMatchCampaignName(info.CampaignName);
                case SegmentationField.CreativeName:
                    return TryMatchCreativeName(info.CreativeName);
            }
            return false;
        }

        private bool TryMatchCreativeName(string creativeName)
        {
            var match = Regex.Match(creativeName, MatchPattern);
            return match.Success;
        }

        private bool TryMatchCampaignName(string campaignName)
        {
            var match = Regex.Match(campaignName, MatchPattern);
            return match.Success;
        }
    }
}