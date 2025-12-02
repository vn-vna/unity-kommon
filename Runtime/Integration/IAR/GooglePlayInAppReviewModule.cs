#if (UNITY_ANDROID || !UNITY_EDITOR) && GOOGLEPLAY_REVIEW
using System.Collections;
using Google.Play.Review;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.IAR
{
    public class GooglePlayInAppReviewModule :
        IInAppReviewModule
    {
        public IInAppReviewManager Manager { get; set; }

        public bool IsInitialized { get; private set; }
        ReviewManager _reviewManager;

        public void Initialize()
        {
            _reviewManager = new ReviewManager();
            IsInitialized = true;
        }

        public void CleanUp()
        { }

        public IEnumerator PerformInAppReviewRequest()
        {
            var requestFlowOperation = _reviewManager.RequestReviewFlow();
            yield return requestFlowOperation;

            if (requestFlowOperation.Error != ReviewErrorCode.NoError)
            {
                Debug.LogWarning($"In-App Review request flow failed: {requestFlowOperation.Error}");
                DirectOpenReviewPage();
                yield break;
            }

            var reviewInfo = requestFlowOperation.GetResult();

            var launchFlowOperation = _reviewManager.LaunchReviewFlow(reviewInfo);
            yield return launchFlowOperation;

            if (launchFlowOperation.Error != ReviewErrorCode.NoError)
            {
                Debug.LogWarning($"In-App Review launch flow failed: {launchFlowOperation.Error}");
                DirectOpenReviewPage();
                yield break;
            }

            Debug.Log("In-App Review flow completed successfully.");
        }

        private void DirectOpenReviewPage()
        {
            Application.OpenURL("market://details?id=" + Application.identifier);
        }
    }
}
#endif