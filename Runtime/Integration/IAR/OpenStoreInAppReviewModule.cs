using System.Collections;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.IAR
{
    [CreateAssetMenu(fileName = "OpenStoreInAppReviewModule",
                     menuName = "Scheherazade/Providers/In-App Review/Open Store")]
    public class OpenStoreInAppReviewModule :
        ScriptableObject,
        IInAppReviewModule
    {
        public IInAppReviewManager Manager { get; set; }

        public bool IsInitialized { get; private set; }

        private string _storeUrl;

        public void Initialize()
        {
#if UNITY_ANDROID
            _storeUrl = "market://details?id=" + Application.identifier;
#elif UNITY_IOS
            _storeUrl = "https://apps.apple.com/app/id" + Application.identifier;
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
