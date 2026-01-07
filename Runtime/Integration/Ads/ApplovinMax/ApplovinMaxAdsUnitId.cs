using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    [Serializable]
    public class ApplovinMaxAdsUnitId
    {
        public AdsType Type => type;
        public string UnitId
            => Application.platform switch
            {
#if UNITY_EDITOR
                _ => androidUnitId,
#else
                RuntimePlatform.IPhonePlayer => iosUnitId,
                RuntimePlatform.Android => androidUnitId,
                _ => null
#endif
            };

        [SerializeField]
        private AdsType type;

        [SerializeField]
        private string androidUnitId;

        [SerializeField]
        private string iosUnitId;
    }
}