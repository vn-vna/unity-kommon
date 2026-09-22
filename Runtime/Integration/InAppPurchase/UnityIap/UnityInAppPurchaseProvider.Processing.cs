#if UNITY_PURCHASING
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Processing;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase
{
    public partial class UnityInAppPurchaseProvider
    {
        #region Properties
        public InAppPurchaseProcessingOptions ProcessingOptions { get; private set; }
        public bool IsTransactionProcessingEnabled => ProcessingOptions != null;
        public bool IsInitializing => _processingSession != null && _processingSession.IsInitializing;
        public string UnavailableReason { get; private set; } = string.Empty;
        public PurchaseStatus LastPurchaseStatus { get; private set; }
        internal Func<IUnityIapStore> StoreFactory { get; set; }
        #endregion

        #region Private Fields
        private ProcessingSession _processingSession;
        private bool _processingStopping;
        #endregion

        #region Unity Callbacks
        private void OnDisable()
        {
            if (IsTransactionProcessingEnabled) EndProcessingSession();
        }
        #endregion

        #region Public Methods
        public void ConfigureTransactionProcessing(InAppPurchaseProcessingOptions options)
        {
            if (_processingStopping || _processingSession != null || _storeController != null || IsInitialized)
                throw new InvalidOperationException("Clean up the IAP provider before changing transaction processing.");
            ProcessingOptions = options;
        }

        public void Tick(float deltaTime) => _processingSession?.Tick(deltaTime);

        public void Dispose() => CleanUp();
        #endregion

        #region Private Methods
        private void InitializeProcessingSession()
        {
            if (_processingStopping) return;
            if (_processingSession == null)
                _processingSession = new ProcessingSession(this, ProcessingOptions);
            _processingSession.Initialize();
        }

        private void EndProcessingSession()
        {
            if (_processingStopping) return;
            _processingStopping = true;
            var session = _processingSession;
            _processingSession = null;
            IsInitialized = false;
            try { session?.Dispose(); }
            finally { _processingStopping = false; }
        }

        private void NotifyProcessingFailure(string productId, PurchaseStatus status, string reason)
        {
            LastPurchaseStatus = status;
            UnavailableReason = reason ?? string.Empty;
            var product = Manager?.ProductDatabase?.Products?.FirstOrDefault(p => p != null && p.ProductId == productId);
            NotifyProcessingListeners(PurchaseFailed, product);
        }

        private static void NotifyProcessingListeners<TValue>(Action<TValue> callback, TValue value)
        {
            if (callback == null) return;
            foreach (Action<TValue> listener in callback.GetInvocationList())
            {
                try { listener(value); }
                catch (Exception error) { QuickLog.Error<UnityInAppPurchaseProvider>("IAP observer failed: {0}", error.Message); }
            }
        }
        #endregion

        #region Nested Types
        // All mutations, policy calls and user notifications run on the owner's Tick.
        // Native callbacks (including synchronous callbacks) only enqueue work.
        private sealed class ProcessingSession : IDisposable
        {
            private const int MaxAttempts = 3;
            private const float OperationTimeout = 30;
            private const float FulfillmentRetryInterval = 10;
            private const float ConfirmationRetryInterval = 5;

            private readonly UnityInAppPurchaseProvider _owner;
            private readonly InAppPurchaseProcessingOptions _options;
            private readonly InAppPurchaseTransactionPipeline _pipeline;
            private readonly RuntimePlatform _platform;
            private readonly Dictionary<string, IInAppPurchaseProduct> _products = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _storeIds = new(StringComparer.Ordinal);
            private readonly Dictionary<string, PendingOrder> _pendingOrders = new(StringComparer.Ordinal);
            private readonly Dictionary<string, Acknowledgement> _acknowledgements = new(StringComparer.Ordinal);
            private readonly Dictionary<string, RetryOrder> _retries = new(StringComparer.Ordinal);
            private readonly HashSet<string> _awaitingStore = new(StringComparer.Ordinal);
            private readonly Queue<Action> _callbacks = new();
            private readonly object _callbackLock = new();
            private List<ProductDefinition> _definitions;
            private IUnityIapStore _store;
            private bool _disposed;
            private bool _connected;
            private bool _connecting;
            private bool _fetchingProducts;
            private bool _fetchingPurchases;
            private bool _catalogFetched;
            private bool _initialPurchasesFetched;
            private bool _recoveryRejected;
            private bool _restoring;
            private bool _restoreDraining;
            private int _restoreAttempt;
            private int _abandonedRestoreAttempt;
            private bool _fetchAgain;
            private bool _discardProductResult;
            private bool _discardPurchaseResult;
            private bool _restoreStarted;
            private bool _restoreStoreCompleted;
            private bool _restoreFetchCompleted;
            private bool _restoreSucceeded;
            private int _generation;
            private int _connectAttempts;
            private int _productAttempts;
            private int _purchaseAttempts;
            private float _time;
            private float _connectDue = float.PositiveInfinity;
            private float _productsDue = float.PositiveInfinity;
            private float _purchasesDue = float.PositiveInfinity;
            private float _connectDeadline = float.PositiveInfinity;
            private float _productDeadline = float.PositiveInfinity;
            private float _purchaseDeadline = float.PositiveInfinity;
            private float _restoreDeadline = float.PositiveInfinity;

            internal bool IsInitializing { get; private set; }
            internal bool HasRestorableProducts { get; private set; }

            internal ProcessingSession(UnityInAppPurchaseProvider owner, InAppPurchaseProcessingOptions options)
            {
                _owner = owner;
                _options = options;
                _platform = Application.platform;
                _pipeline = new InAppPurchaseTransactionPipeline(options, RequestAcknowledgement, Publish);
            }

            internal void Initialize()
            {
                if (_disposed || _connecting || _fetchingProducts || _fetchingPurchases || _restoreDraining || IsInitializing || _owner.IsInitialized) return;
                IsInitializing = true;
                _recoveryRejected = false;
                _connectAttempts = _productAttempts = _purchaseAttempts = 0;
                try
                {
                    if (_store == null)
                    {
                        BuildCatalog();
                        if (_disposed) return;
                        var store = _owner.StoreFactory != null ? _owner.StoreFactory() : new UnityIapStore();
                        if (_disposed) { store?.Dispose(); return; }
                        _store = store ?? throw new InvalidOperationException("The store factory returned null.");
                        Subscribe();
                    }
                    Connect();
                }
                catch (Exception error) { SetUnavailable(error.Message); }
            }

            private void BuildCatalog()
            {
                _products.Clear();
                _storeIds.Clear();
                _definitions = new List<ProductDefinition>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var catalog = _owner.Manager?.ProductDatabase?.Products;
                if (catalog == null) throw new InvalidOperationException("The IAP product database is missing.");
                foreach (var product in catalog)
                {
                    if (_disposed) return;
                    if (product == null || string.IsNullOrWhiteSpace(product.ProductId) || _products.ContainsKey(product.ProductId))
                        throw new InvalidOperationException("The IAP catalog has a missing or duplicate logical product ID.");
                    string storeId = _options.ResolveStoreProductId(product, _platform);
                    if (_disposed) return;
                    if (!ids.Add(storeId)) throw new InvalidOperationException("The IAP catalog has duplicate store product IDs.");
                    _products.Add(product.ProductId, product);
                    _storeIds.Add(product.ProductId, storeId);
                    _definitions.Add(new ProductDefinition(
                        product.ProductId, storeId, product.AllowRecover ? ProductType.NonConsumable : ProductType.Consumable
                    ));
                }
                if (_definitions.Count == 0) throw new InvalidOperationException("The IAP product database is empty.");
            }

            private void Subscribe()
            {
                _store.ProductsFetched += ProductsFetched;
                _store.ProductsFetchFailed += ProductsFetchFailed;
                _store.PurchasesFetched += PurchasesFetched;
                _store.PurchasesFetchFailed += PurchasesFetchFailed;
                _store.PurchasePending += PurchasePending;
                _store.PurchaseConfirmed += PurchaseConfirmed;
                _store.PurchaseFailed += PurchaseFailed;
                _store.PurchaseDeferred += PurchaseDeferred;
                _store.StoreDisconnected += StoreDisconnected;
            }

            private void Enqueue(Action callback)
            {
                lock (_callbackLock)
                {
                    if (!_disposed) _callbacks.Enqueue(callback);
                }
            }

            private void Connect()
            {
                if (_disposed || _connecting) return;
                _connecting = true;
                _connected = false;
                _catalogFetched = _initialPurchasesFetched = false;
                _connectDue = float.PositiveInfinity;
                _connectDeadline = _time + OperationTimeout;
                ++_connectAttempts;
                _ = ConnectAsync(_generation);
            }

            private async Task ConnectAsync(int generation)
            {
                try
                {
                    await _store.Connect();
                    Enqueue(() =>
                    {
                        if (generation != _generation) return;
                        _connecting = false;
                        _connected = true;
                        _connectAttempts = 0;
                        _productAttempts = 0;
                        IsInitializing = !_owner.IsInitialized;
                        ContinueConnectionAfterDrain();
                    });
                }
                catch (Exception error)
                {
                    Enqueue(() =>
                    {
                        if (generation != _generation) return;
                        _connecting = false;
                        ScheduleConnectionRetry(error.Message);
                    });
                }
            }

            private void ContinueConnectionAfterDrain()
            {
                if (_connected && !_disposed && !_discardProductResult && !_discardPurchaseResult) FetchProducts();
            }

            private void ScheduleConnectionRetry(string reason)
            {
                _owner.UnavailableReason = reason ?? "Store connection failed.";
                if (_connectAttempts < MaxAttempts) _connectDue = _time + 2 * Math.Max(1, _connectAttempts);
                else SetUnavailable(_owner.UnavailableReason);
            }

            private void FetchProducts()
            {
                if (_disposed || !_connected || _fetchingProducts) return;
                _productsDue = float.PositiveInfinity;
                _fetchingProducts = true;
                _productDeadline = _time + OperationTimeout;
                ++_productAttempts;
                try { _store.FetchProducts(_definitions); }
                catch (Exception error) { ProductFetchFailed(error.Message); }
            }

            private void ProductFetchFailed(string reason)
            {
                _fetchingProducts = false;
                _owner.UnavailableReason = reason ?? "Product lookup failed.";
                if (_productAttempts < MaxAttempts) _productsDue = _time + 2 * Math.Max(1, _productAttempts);
                else SetUnavailable(_owner.UnavailableReason);
            }

            private void ProductsFetched(List<Product> products) => Enqueue(() =>
            {
                if (_discardProductResult)
                {
                    _discardProductResult = _fetchingProducts = false;
                    ContinueConnectionAfterDrain();
                    return;
                }
                if (!_connected || !_fetchingProducts) return;
                _fetchingProducts = false;
                if (products == null || products.Count == 0)
                {
                    ProductFetchFailed("The store returned no products.");
                    return;
                }
                _catalogFetched = true;
                _productAttempts = _purchaseAttempts = 0;
                FetchPurchases();
            });

            private void ProductsFetchFailed(ProductFetchFailed failure) => Enqueue(() =>
            {
                if (_discardProductResult)
                {
                    _discardProductResult = _fetchingProducts = false;
                    ContinueConnectionAfterDrain();
                    return;
                }
                if (_fetchingProducts) ProductFetchFailed(failure?.FailureReason ?? "Product lookup failed.");
            });

            private void FetchPurchases()
            {
                if (_disposed || !_connected || _fetchingPurchases) return;
                _purchasesDue = float.PositiveInfinity;
                _fetchingPurchases = true;
                _purchaseDeadline = _time + OperationTimeout;
                ++_purchaseAttempts;
                try { _store.FetchPurchases(); }
                catch (Exception error) { PurchaseFetchFailed(error.Message); }
            }

            private void PurchaseFetchFailed(string reason)
            {
                _fetchingPurchases = false;
                _owner.UnavailableReason = reason ?? "Purchase lookup failed.";
                if ((_restoring || _restoreDraining) && _restoreStarted)
                {
                    _restoreFetchCompleted = true;
                    _restoreSucceeded = false;
                    TryCompleteRestore();
                    return;
                }
                if (_purchaseAttempts < MaxAttempts) _purchasesDue = _time + 2 * Math.Max(1, _purchaseAttempts);
                else
                {
                    if (!_owner.IsInitialized) SetUnavailable(_owner.UnavailableReason);
                    if (_restoring) CompleteRestore(false);
                }
            }

            private void PurchasesFetchFailed(PurchasesFetchFailureDescription failure) => Enqueue(() =>
            {
                if (_discardPurchaseResult)
                {
                    _discardPurchaseResult = _fetchingPurchases = false;
                    _abandonedRestoreAttempt = 0;
                    ContinueConnectionAfterDrain();
                    return;
                }
                if (_fetchingPurchases) PurchaseFetchFailed(failure?.Message ?? "Purchase lookup failed.");
            });

            private void PurchasesFetched(Orders orders) => Enqueue(() => HandleFetched(orders));

            private void HandleFetched(Orders orders)
            {
                if (_discardPurchaseResult)
                {
                    _discardPurchaseResult = _fetchingPurchases = false;
                    _abandonedRestoreAttempt = 0;
                    ContinueConnectionAfterDrain();
                    return;
                }
                if (!_connected || !_fetchingPurchases) return;
                _fetchingPurchases = false;
                _purchaseAttempts = 0;
                _purchasesDue = float.PositiveInfinity;
                if (orders == null) { PurchaseFetchFailed("The store returned no order snapshot."); return; }
                bool successful = true;
                _recoveryRejected = false;
                HasRestorableProducts = false;
                var observed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var order in orders.PendingOrders)
                {
                    if (_disposed) return;
                    if (order?.Info != null) observed.Add(order.Info.TransactionID);
                    successful &= ProcessOrder(order, InAppPurchaseOrderSource.Fetched);
                }
                foreach (var order in orders.ConfirmedOrders)
                {
                    if (_disposed) return;
                    if (order?.Info != null) observed.Add(order.Info.TransactionID);
                    bool restored = ProcessOrder(order, InAppPurchaseOrderSource.Fetched);
                    successful &= restored;
                    if (restored && order?.Info != null && _acknowledgements.TryGetValue(order.Info.TransactionID, out var known))
                    {
                        // A verified fetched confirmation also settles a lost ack callback.
                        _pipeline.ReportAcknowledgement(known.Transaction.TransactionId, known.Transaction.IsRestoration, true);
                        _acknowledgements.Remove(order.Info.TransactionID);
                        _pendingOrders.Remove(order.Info.TransactionID);
                    }
                }
                foreach (var order in orders.DeferredOrders)
                {
                    if (_disposed) return;
                    NotifyDeferred(order);
                }
                foreach (string id in _awaitingStore.ToArray())
                {
                    if (observed.Contains(id) || _fetchAgain) continue;
                    _awaitingStore.Remove(id);
                    _recoveryRejected = true;
                    successful = false;
                    if (_pendingOrders.TryGetValue(id, out var pending))
                        NotifyOrderFailure(pending, PurchaseStatus.Pending, "The store did not return the transaction for verification.");
                    if (!_acknowledgements.ContainsKey(id)) _pendingOrders.Remove(id);
                }
                if (_fetchAgain)
                {
                    _fetchAgain = false;
                    if (_awaitingStore.Count > 0) _purchasesDue = _time + 0.1f;
                }
                _initialPurchasesFetched = true;
                if ((_restoring || _restoreDraining) && _restoreStarted)
                {
                    _restoreFetchCompleted = true;
                    _restoreSucceeded &= successful;
                    TryCompleteRestore();
                }
                UpdateReadiness();
            }

            private void PurchasePending(PendingOrder order) => Enqueue(() => ProcessOrder(order, InAppPurchaseOrderSource.Direct));
            private void PurchaseDeferred(DeferredOrder order) => Enqueue(() => NotifyDeferred(order));
            private void PurchaseFailed(FailedOrder order) => Enqueue(() => NotifyOrderFailure(
                order, order != null && order.FailureReason == PurchaseFailureReason.UserCancelled ? PurchaseStatus.Canceled : PurchaseStatus.Failed,
                order?.Details ?? "Purchase failed."
            ));

            private InAppPurchaseOrderData Snapshot(Order order, InAppPurchaseOrderSource source)
            {
                if (order?.Info == null || order.CartOrdered == null || string.IsNullOrEmpty(order.Info.TransactionID))
                    throw new InvalidOperationException("The store returned an incomplete transaction.");
                var items = new List<InAppPurchaseLineItem>();
                foreach (var item in order.CartOrdered.Items())
                {
                    var definition = item?.Product?.definition;
                    if (definition == null || !_products.TryGetValue(definition.id, out var product) ||
                        definition.storeSpecificId != _storeIds[product.ProductId] || item.Quantity <= 0 ||
                        definition.type != (product.AllowRecover ? ProductType.NonConsumable : ProductType.Consumable))
                        throw new InvalidOperationException("The transaction does not match the configured catalog.");
                    items.Add(new InAppPurchaseLineItem(product.ProductId, definition.storeSpecificId, item.Quantity, product.AllowRecover));
                }
                return new InAppPurchaseOrderData(
                    order.Info.TransactionID, order.Info.Receipt, order.Info.Apple?.jwsRepresentation, _platform, source,
                    order is PendingOrder ? InAppPurchaseOrderKind.Pending : order is DeferredOrder ? InAppPurchaseOrderKind.Deferred : InAppPurchaseOrderKind.Confirmed,
                    items
                );
            }

            private bool ProcessOrder(Order order, InAppPurchaseOrderSource source, int attempt = 1)
            {
                if (_disposed) return false;
                InAppPurchaseOrderData raw;
                try { raw = Snapshot(order, source); }
                catch (Exception error)
                {
                    _recoveryRejected |= !_owner.IsInitialized;
                    NotifyOrderFailure(order, order is PendingOrder ? PurchaseStatus.Pending : PurchaseStatus.Failed, error.Message);
                    return false;
                }
                if (order is PendingOrder pending) _pendingOrders[raw.NativeTransactionId] = pending;
                if (order is ConfirmedOrder && raw.Items.Any(item => item.AllowRecover)) HasRestorableProducts = true;
                _retries.Remove(raw.NativeTransactionId);
                _awaitingStore.Remove(raw.NativeTransactionId);
                var result = _pipeline.Process(raw);
                if (_disposed) return false;
                switch (result.Outcome)
                {
                    case InAppPurchasePipelineOutcome.Completed:
                        if (order is PendingOrder && !result.AcknowledgementPending)
                        {
                            _pendingOrders.Remove(raw.NativeTransactionId);
                            _acknowledgements.Remove(raw.NativeTransactionId);
                        }
                        return true;
                    case InAppPurchasePipelineOutcome.Ignored:
                        _pendingOrders.Remove(raw.NativeTransactionId);
                        return true;
                    case InAppPurchasePipelineOutcome.WaitForStore:
                        if (source == InAppPurchaseOrderSource.Direct)
                        {
                            _awaitingStore.Add(raw.NativeTransactionId);
                            if (_fetchingPurchases) _fetchAgain = true;
                            else _purchasesDue = _time;
                            return false;
                        }
                        _recoveryRejected |= !_owner.IsInitialized;
                        NotifyOrderFailure(order, PurchaseStatus.Pending, "Fetched transaction verification is still unavailable.");
                        if (!_acknowledgements.ContainsKey(raw.NativeTransactionId)) _pendingOrders.Remove(raw.NativeTransactionId);
                        return false;
                    case InAppPurchasePipelineOutcome.Retry:
                    case InAppPurchasePipelineOutcome.Busy:
                        bool retryAllowed = _options.MaxProcessingAttempts == 0 || attempt < _options.MaxProcessingAttempts;
                        _retries[raw.NativeTransactionId] = new RetryOrder
                        {
                            Order = order, Source = source, Attempt = attempt,
                            Due = retryAllowed ? _time + FulfillmentRetryInterval : float.PositiveInfinity
                        };
                        if (!retryAllowed && !_owner.IsInitialized) IsInitializing = false;
                        NotifyOrderFailure(order, PurchaseStatus.Pending, result.Reason);
                        return false;
                    case InAppPurchasePipelineOutcome.Deferred:
                        NotifyDeferred(order);
                        return true;
                    default:
                        _recoveryRejected |= !_owner.IsInitialized;
                        NotifyOrderFailure(order, order is PendingOrder ? PurchaseStatus.Pending : PurchaseStatus.Failed, result.Reason);
                        if (!_acknowledgements.ContainsKey(raw.NativeTransactionId)) _pendingOrders.Remove(raw.NativeTransactionId);
                        return false;
                }
            }

            private void RequestAcknowledgement(InAppPurchaseOrderData raw, VerifiedInAppPurchaseTransaction transaction)
            {
                if (_disposed || !_pendingOrders.TryGetValue(raw.NativeTransactionId, out var pending))
                    throw new InvalidOperationException("The pending store order is no longer available.");
                var acknowledgement = new Acknowledgement
                {
                    Transaction = transaction, Due = _time + OperationTimeout
                };
                _acknowledgements[raw.NativeTransactionId] = acknowledgement;
                if (!_connected) throw new InvalidOperationException("The store is disconnected; acknowledgement remains pending.");
                try { _store.ConfirmPurchase(pending); }
                catch
                {
                    acknowledgement.Due = _time + ConfirmationRetryInterval;
                    throw;
                }
            }

            private void PurchaseConfirmed(Order order) => Enqueue(() =>
            {
                if (order?.Info == null || !_acknowledgements.TryGetValue(order.Info.TransactionID, out var acknowledgement)) return;
                bool succeeded = order is ConfirmedOrder;
                var transaction = acknowledgement.Transaction;
                _pipeline.ReportAcknowledgement(transaction.TransactionId, transaction.IsRestoration, succeeded);
                if (succeeded)
                {
                    _acknowledgements.Remove(order.Info.TransactionID);
                    _pendingOrders.Remove(order.Info.TransactionID);
                }
                else acknowledgement.Due = _time + ConfirmationRetryInterval;
            });

            private void Publish(VerifiedInAppPurchaseTransaction transaction)
            {
                if (_disposed) return;
                _owner.LastPurchaseStatus = PurchaseStatus.Confirmed;
                foreach (var item in transaction.Items)
                {
                    if (_disposed) return;
                    if (!_products.TryGetValue(item.ProductId, out var product)) continue;
                    if (!transaction.IsRestoration)
                    {
                        try
                        {
                            var price = GetProductPrice(product.ProductId);
                            Integration.TrackingManager?.TrackPurchaseRevenue(new PurchaseTrackingInfo
                            {
                                Currency = price?.IsoCurrencyCode ?? "USD", Price = (double)(price?.Amount ?? 0),
                                ProductId = product.ProductId, ReceiptRaw = transaction.Receipt, TransactionId = transaction.TransactionId
                            });
                        }
                        catch (Exception error) { QuickLog.Warning<UnityInAppPurchaseProvider>("IAP tracking failed: {0}", error.Message); }
                    }
                    if (_disposed) return;
                    NotifyProcessingListeners(transaction.IsRestoration ? _owner.ProductRestored : _owner.PurchaseSucceeded, product);
                }
            }

            private void NotifyOrderFailure(Order order, PurchaseStatus status, string reason)
            {
                if (_disposed) return;
                _owner.UnavailableReason = reason ?? string.Empty;
                _owner.LastPurchaseStatus = status;
                var items = order?.CartOrdered?.Items();
                bool notified = false;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (_disposed) return;
                        string id = item?.Product?.definition?.id;
                        if (id == null || !_products.TryGetValue(id, out var product)) continue;
                        notified = true;
                        NotifyProcessingListeners(_owner.PurchaseFailed, product);
                    }
                }
                if (!notified) NotifyProcessingListeners<IInAppPurchaseProduct>(_owner.PurchaseFailed, null);
            }

            private void NotifyDeferred(Order order)
            {
                if (_disposed) return;
                _owner.LastPurchaseStatus = PurchaseStatus.Deferred;
                if (order?.CartOrdered == null) return;
                foreach (var item in order.CartOrdered.Items())
                {
                    if (_disposed) return;
                    string id = item?.Product?.definition?.id;
                    if (id != null && _products.TryGetValue(id, out var product))
                        NotifyProcessingListeners(_owner.PurchaseDeferred, product);
                }
            }

            internal InAppPurchaseProductPrice? GetProductPrice(string productId)
            {
                if (_disposed || !_owner.IsInitialized || string.IsNullOrEmpty(productId) || !_products.ContainsKey(productId)) return null;
                var product = _store?.GetProductById(productId);
                if (product?.metadata == null) return null;
                return new InAppPurchaseProductPrice
                {
                    Amount = product.metadata.localizedPrice, LocalizedPrice = product.metadata.localizedPriceString,
                    IsoCurrencyCode = product.metadata.isoCurrencyCode
                };
            }

            internal void BuyProduct(string productId)
            {
                if (_disposed) return;
                if (string.IsNullOrEmpty(productId) || !_products.TryGetValue(productId, out var product))
                {
                    _owner.NotifyProcessingFailure(productId, PurchaseStatus.Failed, "Unknown product.");
                    return;
                }
                if (!_connected || !_owner.IsInitialized || _store.GetProductById(productId)?.availableToPurchase != true)
                {
                    _owner.NotifyProcessingFailure(productId, PurchaseStatus.Unavailable, "The product is not available from the store.");
                    return;
                }
                NotifyProcessingListeners(_owner.PurchaseInitiated, product);
                if (_disposed) return;
                try { _store.PurchaseProduct(productId); }
                catch (Exception error) { _owner.NotifyProcessingFailure(productId, PurchaseStatus.Failed, error.Message); }
            }

            internal void RestorePurchases()
            {
                if (_disposed || _restoring) return;
                if (_restoreDraining)
                {
                    _owner.UnavailableReason = "The previous native restore is still pending; wait for it to finish.";
                    NotifyProcessingListeners(_owner.AllProductsRestored, false);
                    return;
                }
                if (!_connected || !_owner.IsInitialized)
                {
                    NotifyProcessingListeners(_owner.AllProductsRestored, false);
                    return;
                }
                _restoring = true;
                ++_restoreAttempt;
                _restoreStarted = _restoreStoreCompleted = _restoreFetchCompleted = false;
                _restoreSucceeded = true;
                _restoreDeadline = _time + _options.RestoreTimeoutSeconds;
                TryStartRestore();
            }

            private void TryStartRestore()
            {
                if (!_restoring || _restoreStarted || _fetchingPurchases || !_connected) return;
                _restoreStarted = true;
                bool resynchronize;
                try
                {
                    resynchronize = _options.RestorationPolicy != null
                        ? _options.RestorationPolicy.RequiresStoreResynchronization(_platform)
                        : _platform == RuntimePlatform.IPhonePlayer;
                }
                catch (Exception error) { _owner.UnavailableReason = error.Message; CompleteRestore(false); return; }
                if (_disposed) return;
                _purchaseAttempts = 0;
                if (!resynchronize)
                {
                    _restoreStoreCompleted = true;
                    FetchPurchases();
                    return;
                }
                // Unity IAP 5.3 fetches purchases automatically on successful restore,
                // before invoking this callback. Reserve the one fetch slot first.
                _fetchingPurchases = true;
                _purchaseDeadline = _time + OperationTimeout;
                int generation = _generation;
                int attempt = _restoreAttempt;
                try
                {
                    _store.RestoreTransactions((success, error) => Enqueue(() =>
                    {
                        if (generation != _generation || attempt != _restoreAttempt || (!_restoring && !_restoreDraining))
                        {
                            // A failed abandoned resync guarantees the SDK will not
                            // auto-fetch. Release only the slot owned by that attempt.
                            if (!success && attempt == _abandonedRestoreAttempt && _discardPurchaseResult)
                            {
                                _discardPurchaseResult = _fetchingPurchases = false;
                                _abandonedRestoreAttempt = 0;
                                ContinueConnectionAfterDrain();
                            }
                            return;
                        }
                        if (!success)
                        {
                            _owner.UnavailableReason = error ?? "Store restoration failed.";
                            _fetchingPurchases = false;
                            _restoreFetchCompleted = _restoreStoreCompleted = true;
                            _restoreSucceeded = false;
                            TryCompleteRestore();
                            return;
                        }
                        _restoreStoreCompleted = true;
                        TryCompleteRestore();
                    }));
                }
                catch (Exception error)
                {
                    _owner.UnavailableReason = error.Message;
                    _fetchingPurchases = false;
                    CompleteRestore(false);
                }
            }

            private void TryCompleteRestore()
            {
                if (!_restoreStoreCompleted || !_restoreFetchCompleted) return;
                if (_restoreDraining) { _restoreDraining = false; return; }
                if (_restoring) CompleteRestore(_restoreSucceeded);
            }

            private void TimeoutRestore()
            {
                // There is no request token/cancellation API for the SDK fetch channel.
                // Do not attach a later restore to this operation's late result.
                _restoreDraining = _restoreStarted && (!_restoreStoreCompleted || !_restoreFetchCompleted);
                _purchaseDeadline = float.PositiveInfinity;
                CompleteRestore(false);
            }

            private void CompleteRestore(bool success)
            {
                if (!_restoring) return;
                _restoring = false;
                NotifyProcessingListeners(_owner.AllProductsRestored, success);
            }

            private void StoreDisconnected(StoreConnectionFailureDescription failure) => Enqueue(() =>
            {
                ++_generation;
                _connecting = _connected = false;
                // SDK fetches cannot be cancelled or identified by request ID. Drain
                // an outstanding pre-disconnect response before opening a new slot.
                _discardProductResult = _fetchingProducts;
                _discardPurchaseResult = _fetchingPurchases;
                if ((_restoring || _restoreDraining) && _restoreStarted && _fetchingPurchases)
                    _abandonedRestoreAttempt = _restoreAttempt;
                else if (!_discardPurchaseResult)
                    _abandonedRestoreAttempt = 0;
                _restoreDraining = false;
                _restoreStarted = false;
                ++_restoreAttempt;
                _owner.IsInitialized = false;
                IsInitializing = true;
                _productsDue = _purchasesDue = float.PositiveInfinity;
                if (_restoring) CompleteRestore(false);
                ScheduleConnectionRetry(failure?.Message ?? "Store disconnected.");
            });

            private void SetUnavailable(string reason)
            {
                if (_disposed) return;
                _owner.IsInitialized = false;
                IsInitializing = false;
                _owner.UnavailableReason = reason ?? string.Empty;
            }

            private void UpdateReadiness()
            {
                if (_disposed) return;
                if (_recoveryRejected && _initialPurchasesFetched && !_fetchingPurchases && _awaitingStore.Count == 0)
                {
                    SetUnavailable(string.IsNullOrEmpty(_owner.UnavailableReason)
                        ? "Initial purchase recovery was rejected; retry after resolving verification."
                        : _owner.UnavailableReason);
                    return;
                }
                if (!_connected || !_catalogFetched || !_initialPurchasesFetched || _recoveryRejected ||
                    _retries.Count != 0 || _awaitingStore.Count != 0) return;
                _owner.IsInitialized = true;
                IsInitializing = false;
                _owner.UnavailableReason = string.Empty;
            }

            internal void Tick(float deltaTime)
            {
                if (_disposed) return;
                int count;
                lock (_callbackLock) count = _callbacks.Count;
                for (int i = 0; i < count && !_disposed; i++)
                {
                    Action callback;
                    lock (_callbackLock) callback = _callbacks.Dequeue();
                    try { callback(); }
                    catch (Exception error) { SetUnavailable(error.Message); }
                }
                if (_disposed) return;
                if (!float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime)) _time += Mathf.Max(0, deltaTime);
                if (_connecting && _time >= _connectDeadline)
                {
                    // Do not start another native connection while the first Task is
                    // alive. Late completion is still reconciled through its generation.
                    _connectDeadline = float.PositiveInfinity;
                    SetUnavailable("Store connection is still pending.");
                }
                if (_fetchingProducts && _time >= _productDeadline)
                {
                    _productDeadline = float.PositiveInfinity;
                    SetUnavailable("Product lookup is still pending; waiting for its native response.");
                }
                if (_fetchingPurchases && _time >= _purchaseDeadline)
                {
                    if (_restoring && _restoreStarted) TimeoutRestore();
                    else if (!_restoreDraining)
                    {
                        _purchaseDeadline = float.PositiveInfinity;
                        SetUnavailable("Purchase lookup is still pending; waiting for its native response.");
                    }
                }
                if (_connectDue <= _time) Connect();
                if (_productsDue <= _time) FetchProducts();
                if (_purchasesDue <= _time) FetchPurchases();
                if (_disposed) return;
                if (_connected)
                {
                    foreach (var retry in _retries.Values.ToArray())
                    {
                        if (_disposed) return;
                        if (retry.Due <= _time) ProcessOrder(retry.Order, retry.Source, retry.Attempt + 1);
                    }
                    foreach (var acknowledgement in _acknowledgements.Values.ToArray())
                    {
                        if (_disposed) return;
                        if (acknowledgement.Due > _time) continue;
                        var transaction = acknowledgement.Transaction;
                        _pipeline.ReportAcknowledgement(transaction.TransactionId, transaction.IsRestoration, false);
                        _pipeline.RetryAcknowledgement(transaction.TransactionId, transaction.IsRestoration);
                    }
                }
                if (_disposed) return;
                if (_restoring && _time >= _restoreDeadline) TimeoutRestore();
                TryStartRestore();
                UpdateReadiness();
            }

            public void Dispose()
            {
                if (_disposed) return;
                lock (_callbackLock) { _disposed = true; _callbacks.Clear(); }
                ++_generation;
                IsInitializing = false;
                _pipeline.Dispose();
                var store = _store;
                _store = null;
                try
                {
                    if (store != null)
                    {
                        store.ProductsFetched -= ProductsFetched;
                        store.ProductsFetchFailed -= ProductsFetchFailed;
                        store.PurchasesFetched -= PurchasesFetched;
                        store.PurchasesFetchFailed -= PurchasesFetchFailed;
                        store.PurchasePending -= PurchasePending;
                        store.PurchaseConfirmed -= PurchaseConfirmed;
                        store.PurchaseFailed -= PurchaseFailed;
                        store.PurchaseDeferred -= PurchaseDeferred;
                        store.StoreDisconnected -= StoreDisconnected;
                        store.Dispose();
                    }
                }
                finally
                {
                    _pendingOrders.Clear();
                    _acknowledgements.Clear();
                    _retries.Clear();
                    _awaitingStore.Clear();
                    CompleteRestore(false);
                }
            }

            private sealed class RetryOrder
            {
                internal Order Order;
                internal InAppPurchaseOrderSource Source;
                internal int Attempt;
                internal float Due;
            }

            private sealed class Acknowledgement
            {
                internal VerifiedInAppPurchaseTransaction Transaction;
                internal float Due;
            }
        }
        #endregion
    }
}
#endif
