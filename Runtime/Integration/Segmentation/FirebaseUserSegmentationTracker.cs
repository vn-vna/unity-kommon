#if FIREBASE_ANALYTICS

using Com.Hapiga.Scheherazade.Common.Logging;
using Firebase.Analytics;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    [CreateAssetMenu(
        fileName = "FirebaseUserSegmentationTracker",
        menuName = "Scheherazade/Segmentation Trackers/Firebase"
    )]
    public class FirebaseUserSegmentationTracker :
        ScriptableObject,
        IUserSegmentationTracker
    {
        public IUserSegmentation Manager { get; set; }

        public void SegmentationDataUpdated(
            SegmentationInformation info,
            SegmentationDeclaration declaration
        )
        {
            if (declaration == null)
            {
                QuickLog.Warning<FirebaseUserSegmentationTracker>(
                    "Segmentation declaration is null. Cannot update Firebase user property."
                );
                return;
            }

            QuickLog.Info<FirebaseUserSegmentationTracker>(
                "Updating Firebase user property for segmentation: [Segment = {0}]",
                declaration.SegmentName
            );

#pragma warning disable format
            FirebaseAnalytics.SetUserProperty("user_segment",           declaration.SegmentName);
            FirebaseAnalytics.SetUserProperty("attrib_tracker_id",      info.TrackerId);
            FirebaseAnalytics.SetUserProperty("attrib_tracker_name",    info.TrackerName);
            FirebaseAnalytics.SetUserProperty("attrib_ad_group",        info.AdGroup);
            FirebaseAnalytics.SetUserProperty("attrib_creative",        info.CreativeName);
            FirebaseAnalytics.SetUserProperty("attrib_campaign",        info.CampaignName);
            FirebaseAnalytics.SetUserProperty("attrib_network",         info.Network);
#pragma warning restore format
        }
    }
}

#endif