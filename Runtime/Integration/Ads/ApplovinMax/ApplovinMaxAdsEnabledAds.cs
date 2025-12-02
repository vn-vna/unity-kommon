#if APPLOVIN_MAX

using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Ads
{
    [Flags]
    public enum ApplovinMaxAdsEnabledAds
    {
        None = 0,
        Interstitial = 1 << 0,
        Banner = 1 << 1,
        OpenApp = 1 << 2,
        Rewarded = 1 << 3
    }
}

#endif