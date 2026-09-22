#if UNITY_PURCHASING
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.Purchasing;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    // Narrow native boundary: tests drive store events without opening a real store.
    internal interface IUnityIapStore : IDisposable
    {
        event Action<List<Product>> ProductsFetched;
        event Action<ProductFetchFailed> ProductsFetchFailed;
        event Action<Orders> PurchasesFetched;
        event Action<PurchasesFetchFailureDescription> PurchasesFetchFailed;
        event Action<PendingOrder> PurchasePending;
        event Action<Order> PurchaseConfirmed;
        event Action<FailedOrder> PurchaseFailed;
        event Action<DeferredOrder> PurchaseDeferred;
        event Action<StoreConnectionFailureDescription> StoreDisconnected;

        Task Connect();
        void FetchProducts(List<ProductDefinition> products);
        void FetchPurchases();
        void PurchaseProduct(string productId);
        void ConfirmPurchase(PendingOrder order);
        void RestoreTransactions(Action<bool, string> callback);
        Product GetProductById(string productId);
    }

    internal sealed class UnityIapStore : IUnityIapStore
    {
        private readonly StoreController _controller;
        private bool _disposed;

        public UnityIapStore()
        {
            _controller = UnityIAPServices.StoreController();
            // The active provider is the sole owner of these shared SDK services.
            // Fetched pending orders must retain their Fetched provenance.
            _controller.ProcessPendingOrdersOnPurchasesFetched(false);
        }

        public event Action<List<Product>> ProductsFetched
        {
            add => _controller.OnProductsFetched += value;
            remove => _controller.OnProductsFetched -= value;
        }
        public event Action<ProductFetchFailed> ProductsFetchFailed
        {
            add => _controller.OnProductsFetchFailed += value;
            remove => _controller.OnProductsFetchFailed -= value;
        }
        public event Action<Orders> PurchasesFetched
        {
            add => _controller.OnPurchasesFetched += value;
            remove => _controller.OnPurchasesFetched -= value;
        }
        public event Action<PurchasesFetchFailureDescription> PurchasesFetchFailed
        {
            add => _controller.OnPurchasesFetchFailed += value;
            remove => _controller.OnPurchasesFetchFailed -= value;
        }
        public event Action<PendingOrder> PurchasePending
        {
            add => _controller.OnPurchasePending += value;
            remove => _controller.OnPurchasePending -= value;
        }
        public event Action<Order> PurchaseConfirmed
        {
            add => _controller.OnPurchaseConfirmed += value;
            remove => _controller.OnPurchaseConfirmed -= value;
        }
        public event Action<FailedOrder> PurchaseFailed
        {
            add => _controller.OnPurchaseFailed += value;
            remove => _controller.OnPurchaseFailed -= value;
        }
        public event Action<DeferredOrder> PurchaseDeferred
        {
            add => _controller.OnPurchaseDeferred += value;
            remove => _controller.OnPurchaseDeferred -= value;
        }
        public event Action<StoreConnectionFailureDescription> StoreDisconnected
        {
            add => _controller.OnStoreDisconnected += value;
            remove => _controller.OnStoreDisconnected -= value;
        }

        public Task Connect() => _controller.Connect();
        public void FetchProducts(List<ProductDefinition> products) => _controller.FetchProductsWithNoRetries(products);
        public void FetchPurchases() => _controller.FetchPurchases();
        public void PurchaseProduct(string productId) => _controller.PurchaseProduct(productId);
        public void ConfirmPurchase(PendingOrder order) => _controller.ConfirmPurchase(order);
        public void RestoreTransactions(Action<bool, string> callback) => _controller.RestoreTransactions(callback);
        public Product GetProductById(string productId) => _controller.GetProductById(productId);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Unity IAP exposes no getter for this setting. Restore the SDK default,
            // not a claimed snapshot; concurrently active providers are unsupported.
            _controller.ProcessPendingOrdersOnPurchasesFetched(true);
        }
    }
}
#endif
