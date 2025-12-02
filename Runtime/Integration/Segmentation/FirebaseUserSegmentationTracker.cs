#if FIREBASE_ANALYTICS

using Com.Hapiga.Scheherazade.Common.Logging;
using Firebase.Analytics;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    public class FirebaseUserSegmentationTracker :
        IUserSegmentationTracker
    {
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
            else
            {
                QuickLog.Info<FirebaseUserSegmentationTracker>(
                    "Updating Firebase user property for segmentation: [Segment = {0}]",
                    declaration.SegmentName
                );
            }

            FirebaseAnalytics.SetUserProperty(
                "user_segment",
                declaration?.SegmentName ?? "Unknown"
            );
        }
    }
}

#endif