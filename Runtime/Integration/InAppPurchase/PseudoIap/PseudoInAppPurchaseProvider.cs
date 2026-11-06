using System;
using System.Linq;
using System.Threading.Tasks;
using Com.Scheherazade.Common.Logging;
using Com.Scheherazade.Common.Integration.InAppPurchase.Processing;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.InAppPurchase
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

        private PurchaseHandleSource _legacyPurchaseSource;

        public PurchaseHandle BuyProduct(string productId)
        {
            var source = new PurchaseHandleSource(productId);
            if (IsTransactionProcessingEnabled)
            {
                BuyProductWithProcessing(productId, source);
                return source.Handle;
            }

            BuyProductInternal(productId, source);
            return source.Handle;
        }

        private void BuyProductInternal(string productId, PurchaseHandleSource source)
        {
            if (!IsInitialized)
            {
                const string reason = "PseudoInAppPurchaseProvider is not initialized. Call Initialize() before making purchases.";
                QuickLog.Error<PseudoInAppPurchaseProvider>(reason);
                source.TryComplete(PurchaseStatus.Unavailable, reason);
                return;
            }

            if (_legacyPurchaseSource != null && !_legacyPurchaseSource.Handle.IsCompleted)
            {
                source.TryComplete(PurchaseStatus.Busy, "Another purchase is already pending.");
                return;
            }
            _legacyPurchaseSource = source;
            int processingGeneration = _processingGeneration;
            DelayedCall(() =>
            {
                // Legacy cleanup still permits this callback; opting into a new
                // processing lifetime must not publish an unverified old success.
                if (processingGeneration != _processingGeneration ||
                    !ReferenceEquals(_legacyPurchaseSource, source))
                {
                    source.TryComplete(PurchaseStatus.Pending, "The purchase provider was restarted before completion.");
                    return;
                }
                _legacyPurchaseSource = null;
                QuickLog.Info<PseudoInAppPurchaseProvider>(
                    $"Product {productId} purchased successfully."
                );
                source.TryComplete(PurchaseStatus.Confirmed);
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
            else ++_processingGeneration;
            _legacyPurchaseSource?.TryComplete(
                PurchaseStatus.Pending,
                "The purchase provider was cleaned up before completion."
            );
            _legacyPurchaseSource = null;
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