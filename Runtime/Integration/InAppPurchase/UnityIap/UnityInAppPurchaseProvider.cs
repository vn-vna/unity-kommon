#if UNITY_PURCHASING

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    public class UnityPurchaseResult
    {
        public string TransactionID { get; set; }
        public string ProductID { get; set; }
        public DateTime PurchaseDate { get; set; }
        public IInAppPurchaseProduct Product { get; set; }
    }


    public class UnityInAppPurchaseProvider :
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
        public byte[] GooglePlayTangleData { get; set; }
        public byte[] AppleTangleData { get; set; }
        public bool HasRestorableProducts => _pendingRestorations.Count > 0;

        private List<ProductDefinition> _productDefinitions;
        private StoreController _storeController;
        private int _fetchPurchasesTryCount;
        private int _fetchProductsTryCount;
        private bool? _storeConnected;
        private int _tryCount;
        private Queue<string> _pendingRestorations = new Queue<string>();
        private HashSet<string> _handledPurchase = new HashSet<string>();

        public void Initialize()
        {
            _storeController = UnityIAPServices.StoreController();

            _storeController.OnProductsFetched += HandleProductsFetched;
            _storeController.OnProductsFetchFailed += HandleProductsFetchFailed;

            _storeController.OnPurchasesFetched += HandlePurchasesFetched;
            _storeController.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;

            _storeController.OnPurchasePending += HandlePurchasePending;
            _storeController.OnPurchaseConfirmed += HandlePurchaseConfirmed;
            _storeController.OnPurchaseDeferred += HandlePurchaseDeferred;
            _storeController.OnPurchaseFailed += HandlePurchaseFailed;

            _storeController.OnStoreDisconnected += HandleStoreDisconnected;

            _tryCount = 0;

            InitializeInternal();
        }

        private void InitializeInternal()
        {
            if (_tryCount++ > 3)
            {
                QuickLog.Error<UnityInAppPurchaseProvider>(
                    "IAP Store initialization failed after multiple attempts."
                );
                return;
            }

            IsInitialized = false;
            CallInitializeStore();
        }

        private async void CallInitializeStore()
        {
            try
            {
                _storeConnected = null;
                await _storeController.Connect();

                lock (this)
                {
                    _storeConnected ??= true;
                }

                QuickLog.Info<UnityInAppPurchaseProvider>(
                    "IAP Store initialized successfully."
                );

                Dispatcher.DispatchOnMainThread(HandleConnectionCompleted);
            }
            catch (Exception ex)
            {
                QuickLog.Error<UnityInAppPurchaseProvider>(
                    "IAP Store initialization failed: {0}",
                    ex.Message
                );
            }
        }

        public void CleanUp()
        {
            IsInitialized = false;
            if (_storeController == null) return;

            _storeController.OnProductsFetched -= HandleProductsFetched;
            _storeController.OnProductsFetchFailed -= HandleProductsFetchFailed;

            _storeController.OnPurchasesFetched -= HandlePurchasesFetched;
            _storeController.OnPurchasesFetchFailed -= HandlePurchasesFetchFailed;

            _storeController.OnPurchasePending -= HandlePurchasePending;
            _storeController.OnPurchaseConfirmed -= HandlePurchaseConfirmed;
            _storeController.OnPurchaseDeferred -= HandlePurchaseDeferred;
            _storeController.OnPurchaseFailed -= HandlePurchaseFailed;

            _storeController.OnStoreDisconnected -= HandleStoreDisconnected;

            _storeController = null;
        }

        private bool ValidateReceipt(string receipt, out IPurchaseReceipt[] receipts)
        {
            receipts = null;
#if (UNITY_ANDROID || UNITY_IOS || UNITY_STANDALONE_OSX) && !PLATFORM_SKIP_IAP_VALIDATION
            try
            {
                CrossPlatformValidator validator = new CrossPlatformValidator(
                    GooglePlayTangleData,
                    AppleTangleData,
                    Application.identifier
                );

                receipts = validator.Validate(receipt);
            }
            catch (IAPSecurityException ex)
            {
                QuickLog.Error<UnityInAppPurchaseProvider>(
                    "Receipt validation failed: {0}",
                    ex.Message
                );
                return false;
            }
#endif
            return true;
        }

        private void HandleConnectionCompleted()
        {
            PerformFetchProducts();
        }

        private void PerformFetchProducts()
        {
            if (!_storeConnected.HasValue || !_storeConnected.Value)
            {
                QuickLog.Warning<UnityInAppPurchaseProvider>(
                    "Store is not connected. Cannot fetch products."
                );
                return;
            }

            if (_productDefinitions == null)
            {
                RefreshProductCatalog();
            }

            try
            {
                _storeController.FetchProducts(
                    _productDefinitions, 
                    new ExponentialBackOffRetryPolicy()
                );
            }
            catch (Exception ex)
            {
                QuickLog.Error<UnityInAppPurchaseProvider>(
                    "Failed to initiate product fetch: {0}",
                    ex.Message
                );
            }

        }

        private void RefreshProductCatalog()
        {
            _productDefinitions = new List<ProductDefinition>();

            foreach (IInAppPurchaseProduct product in Manager.ProductDatabase.Products)
            {
                _productDefinitions.Add(
                    new ProductDefinition(
                        product.ProductId,
                        product.AllowRecover
                            ? ProductType.NonConsumable
                            : ProductType.Consumable
                    )
                );
            }
        }

        private void HandleProductsFetched(List<Product> list)
        {
            if (IsInitialized) return;
            IsInitialized = true;
            Dispatcher.DispatchOnMainThread(_storeController.FetchPurchases);
            QuickLog.Info<UnityInAppPurchaseProvider>(
                "Products fetched successfully."
            );
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            foreach (ConfirmedOrder order in orders.ConfirmedOrders)
            {
                ProcessPurchasedOrderForRestoration(order);
            }

            QuickLog.Info<UnityInAppPurchaseProvider>("Purchases fetched successfully.");
        }

        private void ProcessPurchasedOrderForRestoration(ConfirmedOrder order)
        {
            QuickLog.Info<UnityInAppPurchaseProvider>(
                "Restoring purchase for transaction ID: {0} with receipt: {0}",
                order.Info.TransactionID, order.Info.Receipt
            );

            ProcessOrder(
                order,
                out bool valid,
                out List<UnityPurchaseResult> receipts
            );

            if (!valid) return;

            foreach (UnityPurchaseResult receipt in receipts)
            {
                if (!CheckProductRestorable(receipt.ProductID)) continue;
                _pendingRestorations.Enqueue(receipt.ProductID);
            }
        }

        private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription description)
        {
            if (_fetchPurchasesTryCount >= 10)
            {
                QuickLog.Error<UnityInAppPurchaseProvider>(
                    "Purchases fetch failed: {0}",
                    description.Message
                );
                return;
            }

            _storeController.FetchPurchases();
        }

        private void HandleProductsFetchFailed(ProductFetchFailed failed)
        {
            if (_fetchProductsTryCount >= 10)
            {
                QuickLog.Error<UnityInAppPurchaseProvider>(
                    $"Products fetch failed: {failed.FailureReason}"
                );
                return;
            }

            PerformFetchProducts();
        }

        private void HandlePurchasePending(PendingOrder order)
        {
            _storeController.ConfirmPurchase(order);
        }

        private void HandlePurchaseFailed(FailedOrder order)
        {
            QuickLog.Warning<UnityInAppPurchaseProvider>(
                $"Purchase failed for product {order.Info.TransactionID}: " +
                $"{order.FailureReason}"
            );

            foreach (var product in order.CartOrdered.Items())
            {
                var prod = Manager.ProductDatabase.Products
                    .FirstOrDefault(p => p.ProductId == product.Product.definition.id);
                if (prod != null)
                {
                    Dispatcher.DispatchOnMainThread(() => PurchaseFailed?.Invoke(prod));
                }
            }
        }

        private void HandlePurchaseDeferred(DeferredOrder order)
        {
            QuickLog.Info<UnityInAppPurchaseProvider>(
                "Purchase deferred for product {0}",
                order.Info.TransactionID
            );

            foreach (var product in order.CartOrdered.Items())
            {
                var prod = Manager.ProductDatabase.Products
                    .FirstOrDefault(p => p.ProductId == product.Product.definition.id);
                if (prod != null)
                {
                    Dispatcher.DispatchOnMainThread(() => PurchaseDeferred?.Invoke(prod));
                }
            }
        }

        private void HandlePurchaseConfirmed(Order order)
        {
            if (_handledPurchase.Contains(order.Info.TransactionID))
            {
                QuickLog.Warning<UnityInAppPurchaseProvider>(
                    "Purchase already handled for transaction ID: {0}",
                    order.Info.TransactionID
                );
                return;
            }

            ProcessOrder(
                order,
                out bool isValid,
                out List<UnityPurchaseResult> receipts
            );

            if (!isValid)
            {
                QuickLog.Warning<UnityInAppPurchaseProvider>(
                    "Verification failed for transaction >> Transaction ID: {0}",
                    order.Info.TransactionID
                );

                foreach (var product in receipts)
                {
                    Dispatcher.DispatchOnMainThread(() => PurchaseFailed?.Invoke(product.Product));
                }
                return;
            }

            QuickLog.Info<UnityInAppPurchaseProvider>(
                "Verification succeeded for transaction >> Transaction ID: {0}",
                order.Info.TransactionID
            );

            foreach (var receipt in receipts)
            {
                var product = Manager.ProductDatabase.Products
                    .FirstOrDefault(p => p.ProductId == receipt.ProductID);

                if (product == null)
                {
                    QuickLog.Warning<UnityInAppPurchaseProvider>(
                        "No handler found for product ID: {0}",
                        receipt.ProductID
                    );
                    continue;
                }

                SendPurchasingTrackingEvent(product, order);
                Dispatcher.DispatchOnMainThread(() => PurchaseSucceeded?.Invoke(product));
                if (product.AllowRecover)
                {
                    Dispatcher.DispatchOnMainThread(() => MarkProductAsRestored(product.ProductId));
                }

                QuickLog.Info<UnityInAppPurchaseProvider>(
                    "Purchase succeeded for product {0}, Transaction ID: {1}, Purchase Date: {2}",
                    receipt.ProductID, receipt.TransactionID, receipt.PurchaseDate
                );
            }

            _handledPurchase.Add(order.Info.TransactionID);
        }

        private void ProcessOrder(Order order, out bool isValid, out List<UnityPurchaseResult> receipts)
        {
            isValid = true;
            receipts = new List<UnityPurchaseResult>();

#if !UNITY_EDITOR && !PLATFORM_SKIP_IAP_VALIDATION
            isValid = ValidateReceipt(order.Info.Receipt, out _);
#endif
            foreach (var product in order.CartOrdered.Items())
            {
                receipts.Add(new UnityPurchaseResult
                {
                    TransactionID = order.Info.TransactionID,
                    ProductID = product.Product.definition.id,
                    PurchaseDate = DateTime.UtcNow,
                    Product = Manager.ProductDatabase.Products
                        .FirstOrDefault(p => p.ProductId == product.Product.definition.id)
                });
            }

            receipts ??= new List<UnityPurchaseResult>();
        }

        private void SendPurchasingTrackingEvent(IInAppPurchaseProduct product, Order order)
        {
            if (Integration.TrackingManager == null) return;

            var price = GetProductPrice(product.ProductId);

            Integration.TrackingManager.TrackPurchaseRevenue(new PurchaseTrackingInfo
            {
                Currency = price?.IsoCurrencyCode ?? "USD",
                Price = (double)(price?.Amount ?? 0),
                ProductId = product.ProductId,
                ReceiptRaw = order.Info.Receipt,
                TransactionId = order.Info.TransactionID
            });
        }

        private void HandleStoreDisconnected(StoreConnectionFailureDescription description)
        {
            QuickLog.Warning<UnityInAppPurchaseProvider>(
                "Store disconnected: {0}",
                description.Message
            );

            lock (this)
            {
                _storeConnected = false;
            }

            Dispatcher.DispatchDelayedOnMainThread(InitializeInternal, 1.0f);
        }


        public InAppPurchaseProductPrice? GetProductPrice(string productId)
        {
            Product product = _storeController.GetProductById(productId);
            if (product == null) return null;

            InAppPurchaseProductPrice price = new InAppPurchaseProductPrice
            {
                Amount = product.metadata.localizedPrice,
                LocalizedPrice = product.metadata.localizedPriceString,
                IsoCurrencyCode = product.metadata.isoCurrencyCode
            };
            return price;
        }

        public void BuyProduct(string productId)
        {
            PurchaseInitiated?.Invoke(
                Manager.ProductDatabase
                    .Products
                    .FirstOrDefault(p => p.ProductId == productId)
            );
            _storeController.PurchaseProduct(productId);
        }

        public void RestorePurchases()
        {
            bool hadRestorableProducts = HasRestorableProducts;

            while (_pendingRestorations.Count > 0)
            {
                string productId = _pendingRestorations.Dequeue();
                var product = Manager.ProductDatabase.Products
                    .FirstOrDefault(p => p.ProductId == productId);
                if (product == null) continue;

                Dispatcher.DispatchOnMainThread(() =>
                {
                    MarkProductAsRestored(product.ProductId);
                    ProductRestored?.Invoke(product);
                });

                QuickLog.Info<UnityInAppPurchaseProvider>(
                    $"Purchase restored for product {productId}"
                );
            }

            AllProductsRestored?.Invoke(hadRestorableProducts);
        }

        private bool CheckProductRestorable(string id)
        {
            var product = Manager.ProductDatabase.Products
                .FirstOrDefault(p => p.ProductId == id);
            if (product == null) return false;

            if (!product.AllowRecover) return false;
            if (PlayerPrefs.GetInt($"IAP_Restored_{id}", 0) == 1) return false;

            return true;
        }

        private void MarkProductAsRestored(string id)
        {
            if (!CheckProductRestorable(id)) return;
            PlayerPrefs.SetInt($"IAP_Restored_{id}", 1);
            PlayerPrefs.Save();
        }

    }
}

#endif