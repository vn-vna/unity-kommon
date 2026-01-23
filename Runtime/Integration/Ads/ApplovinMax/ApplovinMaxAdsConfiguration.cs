using System;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.MappedList;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    [CreateAssetMenu(fileName = "ApplovinMaxAdsConfiguration", menuName = "Dev Menu/Integration/Applovin Max Ads Configuration")]
    public class ApplovinMaxAdsConfiguration : ScriptableObject
    {
        public bool IsTestAds => isTestAds;
        public ApplovinMaxAdsEnabledAds EnabledAds => enabledAds;
        public BannerAdsPosition BannerAdPosition => bannerAdsDisplayPosition;
        public bool IsBannerAutoSized => bannerAutoSized;
        public Color BannerBackgroundColor => bannerBackgroundColor;

        public MappedList<AdsType, ApplovinMaxAdsUnitId> UnitIdsMapping
        {
            get
            {
                if (_unitIdsMapping == null)
                {
                    _unitIdsMapping = new Lazy<MappedList<AdsType, ApplovinMaxAdsUnitId>>(ConstructMappedList);
                }
                return _unitIdsMapping.Value;
            }
        }

        private Lazy<MappedList<AdsType, ApplovinMaxAdsUnitId>> _unitIdsMapping;

        [SerializeField]
        private bool isTestAds;

        [SerializeField]
        private ApplovinMaxAdsEnabledAds enabledAds;

        [SerializeField]
        private ApplovinMaxAdsUnitId[] unitIds;

        [SerializeField]
        private BannerAdsPosition bannerAdsDisplayPosition;

        [SerializeField]
        private bool bannerAutoSized = true;

        [SerializeField]
        private Color bannerBackgroundColor = Color.black;

        private MappedList<AdsType, ApplovinMaxAdsUnitId> ConstructMappedList()
            => new MappedList<AdsType, ApplovinMaxAdsUnitId>(
                unitIds, (uid) => uid.Type
            );

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EditorRefreshOnLoad()
        {
            var configurations = UnityEditor.AssetDatabase.FindAssets("t: ApplovinMaxAdsConfiguration")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<ApplovinMaxAdsConfiguration>)
                .Where(asset => asset != null);

            foreach (var config in configurations)
            {
                config._unitIdsMapping = null;
            }
        }
#endif
    }
}