#if TRACKING_ADJUST

using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using AdjustSdk;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    [CreateAssetMenu(
        fileName = "AdjustSegmentationProvider",
        menuName = "Scheherazade/Segmentation Providers/Adjust"
    )]
    public class AdjustSegmentationProvider :
        ScriptableObject,
        IUserSegmentationProvider
    {
        const string UnknownAttributePlaceholder = "<unknown>";

        [SerializeField]
        private int attributionRetry = 5;

        [SerializeField]
        private float attributionTimeout = 10f;

        [SerializeField]
        private float providerWaitTimeout = 30f;

        public IUserSegmentation Manager { get; set; }
        public bool IsInitialized { get; private set; }

        public event Action<SegmentationInformation> SegmentationDataAcquired;

        public void Initialize()
        {
            Dispatcher.DispatchCoroutine(WaitForAttributionCoroutine());
        }

        public void CleanUp()
        {
            IsInitialized = false;
        }

        private IEnumerator WaitForAttributionCoroutine()
        {
            IsInitialized = false;

            yield return new WaitForSeconds(1f);

            AdjustTrackingProvider adjustTrackingProvider = null;

            if (Integration.TrackingManager == null)
            {
                QuickLog.Critical<AdjustSegmentationProvider>(
                    "Tracking Manager is NULL, cannot perform attribution acquisition"
                );
                yield break;
            }

            foreach (var p in Integration.TrackingManager.Providers)
            {
                if (p is AdjustTrackingProvider atp)
                {
                    adjustTrackingProvider = atp;
                    break;
                }
            }

            if (adjustTrackingProvider == null)
            {
                QuickLog.Critical<AdjustSegmentationProvider>(
                    "No Adjust Tracking Provider assigned, cannot acquire Adjust attribution"
                );
                yield break;
            }

            QuickLog.Info<AdjustSegmentationProvider>(
                "Waiting until Adjust Tracking Provider initialzation complete"
            );

            float elapsed = 0f;

            while (!adjustTrackingProvider.IsInitialized)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed > providerWaitTimeout)
                {
                    QuickLog.Critical<AdjustSegmentationProvider>(
                        "Adjuist Tracking Provider initialization timed out," +
                        "skipping attribution acquisition progress"
                    );
                    IsInitialized = false;
                    yield break;
                }
                yield return null;
            }

            QuickLog.Info<AdjustSegmentationProvider>(
                "Adjust Tracking Provider initialzation completed"
            );

            yield return RequestAdjustAttribution(attributionTimeout, attributionRetry);
        }

        private IEnumerator RequestAdjustAttribution(float timeout, int retry)
        {
            AdjustAttribution attribution = null;

            int tryCount = 0;

            while (tryCount < retry)
            {
                bool completed = false;

                QuickLog.Info<AdjustSegmentationProvider>(
                    "Starting acquiring attribution (attempt: {0})",
                    ++tryCount
                );

                Adjust.GetAttributionWithTimeout(
                    (int)(timeout * 1000),
                    (attrib) =>
                    {
                        attribution = attrib;
                        completed = true;
                    }
                );

                while (!completed) yield return null;

                if (attribution != null) break;
                else
                {
                    QuickLog.Warning<AdjustSegmentationProvider>(
                        "Adjust attribution data not available after {0}s timeout.",
                        attributionTimeout
                    );
                }
            }

            IsInitialized = true;

            if (attribution == null)
            {
                QuickLog.Critical<AdjustSegmentationProvider>(
                    "Adjust Attribution information is not available after " +
                    "{0} retry attempts.",
                    retry
                );
                yield break;
            }

            QuickLog.Info<AdjustSegmentationProvider>(
                "Adjust attribution data acquired."
            );

            SegmentationInformation info = BuildSegmentationInfo(attribution);
            SegmentationDataAcquired?.Invoke(info);

        }

        private static SegmentationInformation BuildSegmentationInfo(AdjustAttribution attribution)
            => new SegmentationInformation
            {
                CampaignName =      attribution.Campaign ?? UnknownAttributePlaceholder,
                CreativeName =      attribution.Creative ?? UnknownAttributePlaceholder,
                AdGroup =           attribution.Adgroup ?? UnknownAttributePlaceholder,
                Label =             attribution.ClickLabel ?? UnknownAttributePlaceholder,
                Network =           attribution.Network ?? UnknownAttributePlaceholder,
                TrackerId =         attribution.TrackerToken ?? UnknownAttributePlaceholder,
                TrackerName =       attribution.TrackerName ?? UnknownAttributePlaceholder
            };
    }
}

#endif
