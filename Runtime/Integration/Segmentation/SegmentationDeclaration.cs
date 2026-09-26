using System;

namespace Com.Scheherazade.Common.Integration.Segmentation
{
    [Serializable]
    public class SegmentationDeclaration
    {
        public string SegmentName;
        public SegmentationMatchingConfiguration[] MatchingConfigurations;
        public SegmentationSpecification[] Specifications;

        internal bool Matches(SegmentationInformation info)
        {
            foreach (SegmentationMatchingConfiguration config in MatchingConfigurations)
            {
                if (!config.Matches(info))
                {
                    return false;
                }
            }
            return true;
        }
    }
}