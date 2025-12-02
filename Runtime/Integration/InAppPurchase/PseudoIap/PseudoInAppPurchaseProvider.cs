using System;
using System.Linq;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    public class PseudoInAppPurchaseProvider :
        IInAppPurchaseProvider
    {
        public Action<IInAppPurchaseProduct> PurchaseInitiated { get; set; }
        public Action<IInAppPurchaseProduct> PurchaseSucceeded { get; set; }
        public Action<IInAppPurchaseProduct> PurchaseFailed { get; set; }
        public Action<IInAppPurchaseProduct> PurchaseDeferred { get; set; }
        public Action<IInAppPurchaseProduct> ProductRestored { get; set; }
        public Action<bool> AllProductsRestored { get; set; }

        public IInAppPurchaseManager Manager { get; set; }
        public bool IsInitialized { get; private set; }
        public bool HasRestorableProducts => false;

        public void BuyProduct(string productId)
        {
            BuyProductInternal(productId);
        }

        private void BuyProductInternal(string productId)
        {
            if (!IsInitialized)
            {
                QuickLog.Error<PseudoInAppPurchaseProvider>(
                    "PseudoInAppPurchaseProvider is not initialized. Call Initialize() before making purchases."
                );
                return;
            }

            DelayedCall(() =>
            {
                QuickLog.Info<PseudoInAppPurchaseProvider>(
                    $"Product {productId} purchased successfully."
                );
                PurchaseSucceeded?.Invoke(
                    Manager.ProductDatabase.Products.First(p => p.ProductId == productId)
                );
            });

        }

        private async void DelayedCall(Action action)
        {
            await Task.Delay(1000);
            action?.Invoke();
        }

        public InAppPurchaseProductPrice? GetProductPrice(string productId)
            => new InAppPurchaseProductPrice
            {
                Amount = (decimal)0.69,
                IsoCurrencyCode = "USD",
                LocalizedPrice = "$0.69"
            };

        public void Initialize()
        {
            IsInitialized = true;
            QuickLog.Warning<PseudoInAppPurchaseProvider>(
                "Pseudo In-App Purchase Provider is applied - All purchases will be simulated as successful."
            );
        }

        public void CleanUp()
        {
            IsInitialized = false;
        }

        public void RestorePurchases()
        {
            QuickLog.Warning<PseudoInAppPurchaseProvider>(
                "RestorePurchases is not supported in PseudoInAppPurchaseProvider."
            );
            AllProductsRestored?.Invoke(false);
        }
    }
}