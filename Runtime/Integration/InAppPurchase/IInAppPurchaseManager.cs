using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    public interface IInAppPurchaseManager
    {
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