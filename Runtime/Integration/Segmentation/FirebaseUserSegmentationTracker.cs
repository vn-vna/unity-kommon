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

            FirebaseAnalytics.SetUserProperty(
                "user_segment",
                declaration.SegmentName
            );
        }
    }
}

#endif