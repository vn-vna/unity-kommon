using System.Collections;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.IAR
{
    [CreateAssetMenu(fileName = "OpenStoreInAppReviewModule",
                     menuName = "Scheherazade/Providers/In-App Review/Open Store")]
    public class OpenStoreInAppReviewModule :
        ScriptableObject,
        IInAppReviewModule
    {
        public IInAppReviewManager Manager { get; set; }

        public bool IsInitialized { get; private set; }

        [SerializeField]
        private string iosAppStoreId;

        private string _storeUrl;

        public void Initialize()
        {
#if UNITY_ANDROID
            _storeUrl = "market://details?id=" + Application.identifier;
#elif UNITY_IOS
            if (ulong.TryParse(iosAppStoreId, out _))
            {
                _storeUrl = $"itms-apps://itunes.apple.com/app/id{iosAppStoreId}?action=write-review";
            }
            else
            {
                _storeUrl = null;
                Debug.LogWarning("[OpenStoreIAR] Configure the numeric iOS App Store ID before requesting a review.");
            }
#endif
            IsInitialized = true;
        }

        public void CleanUp()
        {
            _storeUrl = null;
            IsInitialized = false;
        }

        public IEnumerator PerformInAppReviewRequest()
        {
            if (string.IsNullOrEmpty(_storeUrl))
            {
                Debug.LogWarning("[OpenStoreIAR] No store URL available for this platform.");
                yield break;
            }

            Application.OpenURL(_storeUrl);
            yield return null;
        }
    }
}
