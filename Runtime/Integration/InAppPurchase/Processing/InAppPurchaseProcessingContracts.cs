using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.InAppPurchase.Processing
{
    public enum InAppPurchaseOrderSource { Direct, Fetched, Simulated }
    public enum InAppPurchaseOrderKind { Pending, Confirmed, Deferred }
    public enum InAppPurchaseRestorationKind { Ignore, Purchase, Restoration }
    public enum InAppPurchaseVerificationOutcome { Verified, Retry, Rejected, WaitForStore }
    public enum InAppPurchaseProcessingOutcome { Completed, Retry, Rejected }
    public enum InAppPurchasePipelineOutcome
    {
        Completed, Retry, Rejected, WaitForStore, Ignored, Deferred, Busy, Disposed
    }

    /// <summary>Immutable logical/store identity and quantity for one order line.</summary>
    public sealed class InAppPurchaseLineItem
    {
        public string ProductId { get; }
        public string StoreProductId { get; }
        public int Quantity { get; }
        public bool AllowRecover { get; }

        public InAppPurchaseLineItem(string productId, string storeProductId, int quantity, bool allowRecover)
        {
            ProductId = productId ?? string.Empty;
            StoreProductId = storeProductId ?? string.Empty;
            Quantity = quantity;
            AllowRecover = allowRecover;
        }
    }

    /// <summary>
    /// Snapshot supplied by the SDK bridge. Malformed input is retained for fail-closed
    /// pipeline diagnostics rather than granting or acknowledging an incomplete order.
    /// </summary>
    public sealed class InAppPurchaseOrderData
    {
        public string NativeTransactionId { get; }
        public string Receipt { get; }
        public string Jws { get; }
        public RuntimePlatform Platform { get; }
        public InAppPurchaseOrderSource Source { get; }
        public InAppPurchaseOrderKind Kind { get; }
        public IReadOnlyList<InAppPurchaseLineItem> Items { get; }

        public InAppPurchaseOrderData(
            string nativeTransactionId,
            string receipt,
            string jws,
            RuntimePlatform platform,
            InAppPurchaseOrderSource source,
            InAppPurchaseOrderKind kind,
            IEnumerable<InAppPurchaseLineItem> items
        )
        {
            NativeTransactionId = nativeTransactionId ?? string.Empty;
            Receipt = receipt ?? string.Empty;
            Jws = jws ?? string.Empty;
            Platform = platform;
            Source = source;
            Kind = kind;
            Items = Snapshot(items);
        }

        internal static IReadOnlyList<InAppPurchaseLineItem> Snapshot(IEnumerable<InAppPurchaseLineItem> items)
        {
            var copy = items == null ? new List<InAppPurchaseLineItem>() : new List<InAppPurchaseLineItem>(items);
            return new ReadOnlyCollection<InAppPurchaseLineItem>(copy);
        }
    }

    /// <summary>
    /// Verifier output. TransactionId is the stable durable idempotency identity, not
    /// necessarily the native identifier. The verified cart must match the raw cart.
    /// </summary>
    public sealed class VerifiedInAppPurchaseTransaction
    {
        public string TransactionId { get; }
        public IReadOnlyList<InAppPurchaseLineItem> Items { get; }
        public string Receipt { get; }
        public string Jws { get; }
        public RuntimePlatform Platform { get; }
        public bool IsRestoration { get; }

        public VerifiedInAppPurchaseTransaction(
            string transactionId,
            IEnumerable<InAppPurchaseLineItem> items,
            string receipt,
            string jws,
            RuntimePlatform platform,
            bool isRestoration
        )
        {
            TransactionId = transactionId ?? string.Empty;
            Items = InAppPurchaseOrderData.Snapshot(items);
            Receipt = receipt ?? string.Empty;
            Jws = jws ?? string.Empty;
            Platform = platform;
            IsRestoration = isRestoration;
        }
    }

    public interface IInAppPurchaseStoreIdResolver
    {
        string ResolveStoreProductId(IInAppPurchaseProduct product, RuntimePlatform platform);
    }

    public interface IInAppPurchaseRestorationPolicy
    {
        InAppPurchaseRestorationKind Classify(InAppPurchaseOrderData order);
        bool RequiresStoreResynchronization(RuntimePlatform platform);
    }

    public interface IInAppPurchaseTransactionVerifier
    {
        /// <summary>
        /// Pure verification on the Unity main thread. WaitForStore requests a later
        /// fetched-order handoff; it is not permission to grant or acknowledge.
        /// </summary>
        InAppPurchaseVerificationOutcome Verify(
            InAppPurchaseOrderData order,
            bool isRestoration,
            out VerifiedInAppPurchaseTransaction transaction,
            out string reason
        );
    }

    public interface IInAppPurchaseSimulationPolicy
    {
        PurchaseStatus SelectOutcome(string productId);
    }

    public interface IInAppPurchaseTransactionFulfillment
    {
        /// <summary>
        /// Completed means rewards and durable idempotency records are committed.
        /// Repeated calls after Retry or a new provider lifetime must be idempotent.
        /// Exceptions are treated as Retry; the store order remains unacknowledged.
        /// </summary>
        InAppPurchaseProcessingOutcome Fulfill(VerifiedInAppPurchaseTransaction transaction, out string reason);
    }

    public interface ITransactionProcessingIapProvider
    {
        InAppPurchaseProcessingOptions ProcessingOptions { get; }
        bool IsTransactionProcessingEnabled { get; }
        void ConfigureTransactionProcessing(InAppPurchaseProcessingOptions options);
    }

    /// <summary>Called once per frame by the owner with an unscaled delta, on the Unity main thread.</summary>
    public interface IInAppPurchaseRetryPump
    {
        void Tick(float deltaTime);
    }

    /// <summary>
    /// Non-null options explicitly opt in. Both verifier and fulfillment are required:
    /// partial configuration never falls back to unverified or event-only processing.
    /// Collaborators are owner-supplied and are not disposed by the transaction pipeline.
    /// </summary>
    public sealed class InAppPurchaseProcessingOptions
    {
        public IInAppPurchaseTransactionVerifier Verifier { get; }
        public IInAppPurchaseTransactionFulfillment Fulfillment { get; }
        public IInAppPurchaseStoreIdResolver StoreIdResolver { get; }
        public IInAppPurchaseRestorationPolicy RestorationPolicy { get; }
        public IInAppPurchaseSimulationPolicy SimulationPolicy { get; }
        /// <summary>Zero retries transaction processing until it succeeds or the provider is cleaned up.</summary>
        public int MaxProcessingAttempts { get; }
        public float RestoreTimeoutSeconds { get; }

        public InAppPurchaseProcessingOptions(
            IInAppPurchaseTransactionVerifier verifier,
            IInAppPurchaseTransactionFulfillment fulfillment,
            IInAppPurchaseStoreIdResolver storeIdResolver = null,
            IInAppPurchaseRestorationPolicy restorationPolicy = null,
            IInAppPurchaseSimulationPolicy simulationPolicy = null,
            int maxProcessingAttempts = 3,
            float restoreTimeoutSeconds = 30
        )
        {
            if (maxProcessingAttempts < 0) throw new ArgumentOutOfRangeException(nameof(maxProcessingAttempts));
            if (float.IsNaN(restoreTimeoutSeconds) || float.IsInfinity(restoreTimeoutSeconds) || restoreTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(restoreTimeoutSeconds));
            Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            Fulfillment = fulfillment ?? throw new ArgumentNullException(nameof(fulfillment));
            StoreIdResolver = storeIdResolver;
            RestorationPolicy = restorationPolicy;
            SimulationPolicy = simulationPolicy;
            MaxProcessingAttempts = maxProcessingAttempts;
            RestoreTimeoutSeconds = restoreTimeoutSeconds;
        }

        public string ResolveStoreProductId(IInAppPurchaseProduct product, RuntimePlatform platform)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            string id = StoreIdResolver == null ? product.ProductId : StoreIdResolver.ResolveStoreProductId(product, platform);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("The product resolver returned an empty store product ID.");
            }
            return id;
        }
    }

    /// <summary>
    /// Snapshot after one pipeline call. Completed means durable fulfillment, not
    /// store acknowledgement. Publication and acknowledgement are independent facts.
    /// </summary>
    public sealed class InAppPurchasePipelineResult
    {
        public InAppPurchasePipelineOutcome Outcome { get; }
        public VerifiedInAppPurchaseTransaction Transaction { get; }
        public string Reason { get; }
        public bool IsPublished { get; }
        /// <summary>At least one acknowledgement callback has been invoked for this transaction/kind.</summary>
        public bool AcknowledgementRequested { get; }
        public bool AcknowledgementPending { get; }
        public bool AcknowledgementInFlight { get; }

        internal InAppPurchasePipelineResult(
            InAppPurchasePipelineOutcome outcome,
            VerifiedInAppPurchaseTransaction transaction,
            string reason,
            bool isPublished = false,
            bool acknowledgementRequested = false,
            bool acknowledgementPending = false,
            bool acknowledgementInFlight = false
        )
        {
            Outcome = outcome;
            Transaction = transaction;
            Reason = reason ?? string.Empty;
            IsPublished = isPublished;
            AcknowledgementRequested = acknowledgementRequested;
            AcknowledgementPending = acknowledgementPending;
            AcknowledgementInFlight = acknowledgementInFlight;
        }
    }
}
