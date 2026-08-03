using System;
using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    public interface IInAppPurchaseManager
    {
        event Action<IInAppPurchaseProduct> PurchaseInitiated;
        event Action<IInAppPurchaseProduct> PurchaseSucceeded;
        event Action<IInAppPurchaseProduct> PurchaseFailed;
        event Action<IInAppPurchaseProduct> PurchaseDeferred;
        event Action<IInAppPurchaseProduct> ProductRestored;
        event Action<bool> AllProductsRestored;

        IInAppPurchaseProvider Provider { get; }
        IInAppPurchaseDatabase ProductDatabase { get; }
        InAppPurchaseManagerStatus Status { get; }
        bool HasRestorableProducts { get; }

        void Initialize(float timeOut = float.MaxValue);
        IEnumerator InitializeCoroutine(float timeOut = float.MaxValue);
        void Shutdown();

        InAppPurchaseProductPrice? GetProductPrice(string productId);
        void BuyProduct(string productId);
        void RestorePurchases();
    }
}