using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    public interface IInAppPurchaseProvider
    {
        Action<IInAppPurchaseProduct> PurchaseInitiated { get; set; }
        Action<IInAppPurchaseProduct> PurchaseSucceeded { get; set; }
        Action<IInAppPurchaseProduct> PurchaseFailed { get; set; }
        Action<IInAppPurchaseProduct> PurchaseDeferred { get; set; }
        Action<IInAppPurchaseProduct> ProductRestored { get; set; }
        Action<bool> AllProductsRestored { get; set; }

        IInAppPurchaseManager Manager { get; set; }
        bool IsInitialized { get; }
        bool HasRestorableProducts { get; }

        void Initialize();
        void CleanUp();

        InAppPurchaseProductPrice? GetProductPrice(string productId);
        void BuyProduct(string productId);
        void RestorePurchases();
    }
}