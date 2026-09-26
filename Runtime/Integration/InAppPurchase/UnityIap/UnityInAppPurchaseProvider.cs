#if UNITY_PURCHASING

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Com.Scheherazade.Common.Integration.Tracking;
using Com.Scheherazade.Common.Integration.InAppPurchase.Processing;
using Com.Scheherazade.Common.Logging;
using Com.Scheherazade.Common.Threading;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

namespace Com.Scheherazade.Common.Integration.InAppPurchase
{
    public class UnityPurchaseResult
    {
        public string TransactionID { get; set; }
        public string ProductID { get; set; }
        public DateTime PurchaseDate { get; set; }
        public IInAppPurchaseProduct Product { get; set; }
    }

    [CreateAssetMenu(
        fileName = "UnityInAppPurchaseProvider",
        menuName = "Scheherazade/In-App Purchase Providers/Unity IAP"
    )]
    public partial class UnityInAppPurchaseProvider :
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
        public byte[] GooglePlayTangleData { get; set; }
        public byte[] AppleTangleData { get; set; }
        public bool HasRestorableProducts => IsTransactionProcessingEnabled
            ? _processingSession != null && _processingSession.HasRestorableProducts
            : _pendingRestorations.Count > 0;

        [SerializeField]
        private int maxProductFetchRetries = 3;

        [SerializeField]
        private float productFetchRetryInterval = 1.0f;

        private List<ProductDefinition> _productDefinitions;
        private StoreController _storeController;
        private int _fetchPurchasesTryCount;
        private bool? _storeConnected;
        private int _tryCount;
        private Queue<string> _pendingRestorations = new Queue<string>();
        private HashSet<string> _handledPurchase = new HashSet<string>();

        private HashSet<string> _successfulProductIds = new HashSet<string>();
        private Dictionary<string, int> _productRetryAttempts = new Dictionary<string, int>();
        private PurchaseHandleSource _legacyPurchaseSource;

        public void Initialize()
        {
            if (IsTransactionProcessingEnabled)
            {
                InitializeProcessingSession();
                return;
            }
#if !PLATFORM_SKIP_IAP_VALIDATION && !UNITY_EDITOR && UNITY_ANDROID
            IEnumerable<byte[]> googlePlayTangleDataPresents = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.Name == "GooglePlayTangle")
                .Select(type => type.GetMethod("Data", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                .Where(prop => prop != null)
                .Select(prop => prop.Invoke(null, null) as byte[]);

            if (googlePlayTangleDataPresents.Count() == 0)
            {
                QuickLog.Warning<UnityInAppPurchaseProvider>(
                    "GooglePlayTangle data not found. Receipt validation will be skipped on Android."
                );
            }
            else
            {
                if (googlePlayTangleDataPresents.Count() > 1)
                {
                    QuickLog.Warning<UnityInAppPurchaseProvider>(
                        "Multiple GooglePlayTangle data found. Using the first one."
                    );
                }

                GooglePlayTangleData = googlePlayTangleDataPresents.First();
            }

            if (GooglePlayTangleData != null && GooglePlayTangleData.Length > 0)
            {
                QuickLog.Info<UnityInAppPurchaseProvider>(
                    "GooglePlayTangle data loaded successfully."
                );
            }
            else
            {
                QuickLog.Warning<UnityInAppPurchaseProvider>(
                    "GooglePlayTangle data is null. Receipt validation will be skipped on Android."
                );
            }
#endif

            CleanUp();

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
            if (IsInitialized)
            {
                return;
            }

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
            if (IsTransactionProcessingEnabled)
            {
                EndProcessingSession();
                return;
            }
            IsInitialized = false;
            _legacyPurchaseSource?.TryComplete(PurchaseStatus.Pending, "The purchase provider was cleaned up before completion.");
            _legacyPurchaseSource = null;
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
            QuickLog.Info<UnityInAppPurchaseProvider>("Store connected");
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

            if (_productDefinitions == null || _productDefinitions.Count == 0)
            {
                RefreshProductCatalog();
            }

            QuickLog.Info<UnityInAppPurchaseProvider>("Performing FETCH PRODUCT");

            try
            {
                if (_productDefinitions.Count == 0)
                {
                    QuickLog.Warning<UnityInAppPurchaseProvider>(
                        "No products defined in the product catalog."
                    );
                    return;
                }

                _storeController.FetchProductsWithNoRetries(
                    _productDefinitions
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
            foreach (Product product in list)
            {
                _successfulProductIds.Add(product.definition.id);
                _productRetryAttempts.Remove(product.definition.id);
            }

            if (IsInitialized) return;
            IsInitialized = true;
            Dispatcher.DispatchOnMainThread(PerformFetchPurchases);
            QuickLog.Info<UnityInAppPurchaseProvider>("Products fetched successfully.");
        }

        private void PerformFetchPurchases()
        {
            _storeController.FetchPurchases();
            QuickLog.Info<UnityInAppPurchaseProvider>("Fetching Purchases");
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            PerformProcessFetchedPurchases(orders).DispatchOnDispatcher();
        }

        private IEnumerator PerformProcessFetchedPurchases(Orders orders)
        {
            foreach (ConfirmedOrder order in orders.ConfirmedOrders)
            {
                ProcessPurchasedOrderForRestoration(order);
            }

            QuickLog.Info<UnityInAppPurchaseProvider>("Purchases fetched successfully.");
            yield break;
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

            if (!valid)
            {
                QuickLog.Error<UnityInAppPurchaseProvider>("Some purchases cannot be validated");
                return;
            }

            int count = 0;

            foreach (UnityPurchaseResult receipt in receipts)
            {
                if (!CheckProductRestorable(receipt.ProductID)) continue;
                _pendingRestorations.Enqueue(receipt.ProductID);
                QuickLog.Info<UnityInAppPurchaseProvider>(
                    "Queued product {0} for restoration",
                    receipt.ProductID
                );
                ++count;
            }

            QuickLog.Info<UnityInAppPurchaseProvider>(
                "Queued {0} items for restoration",
                count
            );

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
            List<ProductDefinition> productsToRetry = new List<ProductDefinition>();
            List<ProductDefinition> productNoRetry = new List<ProductDefinition>();

            foreach (ProductDefinition productDef in failed.FailedFetchProducts)
            {
                string productId = productDef.id;

                if (_successfulProductIds.Contains(productId))
                {
                    continue;
                }

                if (!_productRetryAttempts.ContainsKey(productId))
                {
                    _productRetryAttempts[productId] = 0;
                }

                _productRetryAttempts[productId]++;

                if (_productRetryAttempts[productId] <= maxProductFetchRetries)
                {
                    productsToRetry.Add(productDef);
                }
                else
                {
                    productNoRetry.Add(productDef);
                }
            }

            if (productsToRetry.Count > 0)
            {
                QuickLog.Warning<UnityInAppPurchaseProvider>(
                    "Retrying fetch for {0} product(s) [Reason: {1}] [Attempt: {2}/{3}]",
                    productsToRetry.Count, failed.FailureReason,
                    _productRetryAttempts[productsToRetry[0].id], maxProductFetchRetries
                );

                Dispatcher.DispatchDelayedOnMainThread(
                    () => RetryFailedProducts(productsToRetry),
                    productFetchRetryInterval
                );
            }
            else if (productNoRetry.Count > 0)
            {
                QuickLog.Critical<UnityInAppPurchaseProvider>(
                    "Several product(s) fetching will be permanently terminated" +
                    "due to multiple failure attempts including: [{0}]",
                    (Func<object>)(() => string.Join(",", productNoRetry.Select(p => p.id)))
                );
            }
        }

        private void RetryFailedProducts(List<ProductDefinition> failedProducts)
        {
            if (!_storeConnected.HasValue || !_storeConnected.Value)
            {
                QuickLog.Warning<UnityInAppPurchaseProvider>(
                    "Store disconnected. Aborting product fetch retry."
                );
                return;
            }

            try
            {
                _storeController.FetchProductsWithNoRetries(failedProducts);
            }
            catch (Exception ex)
            {
                QuickLog.Error<UnityInAppPurchaseProvider>(
                    "Failed to initiate product fetch retry: {0}",
                    ex.Message
                );
            }
        }

        private bool IsActiveLegacyOrder(Order order, bool allowBind)
        {
            if (_legacyPurchaseSource == null || order?.CartOrdered == null ||
                !order.CartOrdered.Items().Any(item =>
                    item?.Product?.definition?.id == _legacyPurchaseSource.Handle.ProductId)) return false;
            string nativeId = order.Info?.TransactionID;
            if (string.IsNullOrEmpty(_legacyPurchaseSource.Handle.TransactionId) && allowBind)
                _legacyPurchaseSource.TryBindTransaction(nativeId);
            return string.Equals(
                _legacyPurchaseSource.Handle.TransactionId,
                nativeId,
                StringComparison.Ordinal
            );
        }

        private void HandlePurchasePending(PendingOrder order)
        {
            IsActiveLegacyOrder(order, true);
            _storeController.ConfirmPurchase(order);
        }

        private void HandlePurchaseFailed(FailedOrder order)
        {
            PurchaseStatus status = order != null && order.FailureReason == PurchaseFailureReason.UserCancelled
                ? PurchaseStatus.Canceled : PurchaseStatus.Failed;
            if (IsActiveLegacyOrder(order, true))
            {
                _legacyPurchaseSource.TryComplete(status, order?.Details, order?.Info?.TransactionID);
                _legacyPurchaseSource = null;
            }
            QuickLog.Warning<UnityInAppPurchaseProvider>(
                $"Purchase failed for product {order?.Info?.TransactionID}: " +
                $"{order?.FailureReason}"
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
            if (IsActiveLegacyOrder(order, true))
            {
                _legacyPurchaseSource.TryComplete(
                    PurchaseStatus.Deferred,
                    "Store approval is deferred.",
                    order?.Info?.TransactionID
                );
            }
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
                if (IsActiveLegacyOrder(order, false))
                {
                    _legacyPurchaseSource.TryComplete(
                        PurchaseStatus.Failed,
                        "Purchase verification failed.",
                        order?.Info?.TransactionID
                    );
                    _legacyPurchaseSource = null;
                }
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
            if (IsActiveLegacyOrder(order, false))
            {
                _legacyPurchaseSource.TryComplete(
                    PurchaseStatus.Confirmed,
                    transactionId: order.Info.TransactionID
                );
                _legacyPurchaseSource = null;
            }

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
            if (IsTransactionProcessingEnabled) return _processingSession?.GetProductPrice(productId);
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

        public PurchaseHandle BuyProduct(string productId)
        {
            var source = new PurchaseHandleSource(productId);
            if (IsTransactionProcessingEnabled)
            {
                if (_processingSession != null) _processingSession.BuyProduct(productId, source);
                else
                {
                    const string reason = "The store has not initialized.";
                    source.TryComplete(PurchaseStatus.Unavailable, reason);
                    NotifyProcessingFailure(productId, PurchaseStatus.Unavailable, reason);
                }
                return source.Handle;
            }

            IInAppPurchaseProduct product = Manager?.ProductDatabase?.Products?
                .FirstOrDefault(candidate => candidate.ProductId == productId);
            if (_storeController == null || product == null)
            {
                const string reason = "The product or store is unavailable.";
                source.TryComplete(PurchaseStatus.Unavailable, reason);
                PurchaseFailed?.Invoke(product);
                return source.Handle;
            }
            if (_legacyPurchaseSource != null)
            {
                source.TryComplete(PurchaseStatus.Busy, "Another purchase is already pending.");
                PurchaseFailed?.Invoke(product);
                return source.Handle;
            }

            _legacyPurchaseSource = source;
            PurchaseInitiated?.Invoke(product);
            try { _storeController.PurchaseProduct(productId); }
            catch (Exception exception)
            {
                source.TryComplete(PurchaseStatus.Failed, exception.Message);
                _legacyPurchaseSource = null;
                PurchaseFailed?.Invoke(product);
            }
            return source.Handle;
        }

        public void RestorePurchases()
        {
            if (IsTransactionProcessingEnabled)
            {
                if (_processingSession != null) _processingSession.RestorePurchases();
                else NotifyProcessingListeners(AllProductsRestored, false);
                return;
            }
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