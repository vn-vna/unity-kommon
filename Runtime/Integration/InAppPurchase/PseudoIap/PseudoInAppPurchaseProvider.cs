using System;
using System.Linq;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Processing;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    [CreateAssetMenu(
        fileName = "PseudoInAppPurchaseProvider",
        menuName = "Scheherazade/In-App Purchase Providers/Pseudo Provider"
    )]
    public partial class PseudoInAppPurchaseProvider :
        ScriptableObject,
        IInAppPurchaseProvider,
        ITransactionProcessingIapProvider,
        IInAppPurchaseRetryPump,
        IInAppPurchaseProviderDiagnostics,
        IDisposable
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
            if (IsTransactionProcessingEnabled)
            {
                BuyProductWithProcessing(productId);
                return;
            }

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

            int processingGeneration = _processingGeneration;
            DelayedCall(() =>
            {
                // Legacy cleanup still permits this callback; opting into a new
                // processing lifetime must not publish an unverified old success.
                if (processingGeneration != _processingGeneration) return;
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
        {
            if (IsTransactionProcessingEnabled && (!IsInitialized || Manager?.ProductDatabase?.Products?.Any(
                    product => product != null && product.ProductId == productId
                ) != true)) return null;
            return new InAppPurchaseProductPrice
            {
                Amount = (decimal)0.69,
                IsoCurrencyCode = "USD",
                LocalizedPrice = "$0.69"
            };
        }

        public void Initialize()
        {
            if (IsTransactionProcessingEnabled)
            {
                InitializeProcessing();
                return;
            }

            IsInitialized = true;
            QuickLog.Warning<PseudoInAppPurchaseProvider>(
                "Pseudo In-App Purchase Provider is applied - All purchases will be simulated as successful."
            );
        }

        public void CleanUp()
        {
            if (IsTransactionProcessingEnabled) CleanUpProcessing();
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