namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    public interface IInAppPurchaseProduct
    {
        string ProductId { get; }
        bool AllowRecover { get; }
        IAPProductType ProductType { get; }
        IAPCategory Category { get; }
    }

    public enum IAPProductType
    {
        RemoveAds = 1,
        Gold = 2,
        HelperPack = 3,
        StarterPack = 4,
        BundlePack = 5,
        PiggyBank = 6,
    }

    public enum IAPCategory
    {
        None = 0,
        //RemoveAds
        RemoveAds = 101,
        RemoveAdsBundle = 102,

        //Gold
        FreeGold = 201,
        Gold300 = 202,
        Gold500 = 203,
        Gold900 = 204,
        Gold2000 = 205,
        Gold5000 = 206,
        Gold10000 = 207,

        //Pack bundle
        StarterPack = 301,
        HelperPack = 302,
        TinyPack = 303,
        SmallPack = 304,
        GreatPack = 305,
        EpicPack = 306,

        //PiggyBank
        PiggyBank = 401,
    }
}