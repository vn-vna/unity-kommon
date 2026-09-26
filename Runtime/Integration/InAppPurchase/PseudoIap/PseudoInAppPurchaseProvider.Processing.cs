using System;
using System.Linq;
using Com.Scheherazade.Common.Integration.InAppPurchase.Processing;
using Com.Scheherazade.Common.Logging;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.InAppPurchase
{
    public partial class PseudoInAppPurchaseProvider
    {
        #region Constants
        private const float ProcessingRetryInterval = 1f;
        #endregion

        #region Properties
        public InAppPurchaseProcessingOptions ProcessingOptions { get; private set; }
        public bool IsTransactionProcessingEnabled => ProcessingOptions != null;
        public InAppPurchasePipelineResult LastProcessingResult { get; private set; }
        public string UnavailableReason => LastProcessingResult?.Reason ?? string.Empty;
        public bool IsInitializing => false;
        public PurchaseStatus LastPurchaseStatus { get; private set; }

        // Only the opt-in path uses this seam; legacy delayed callbacks are unchanged.
        internal Action<Action> ScheduleCompletion { get; set; }
        #endregion

        #region Private Fields
        private InAppPurchaseTransactionPipeline _processingPipeline;
        private PendingSimulation _pendingSimulation;
        private int _processingGeneration;
        #endregion

        #region Unity Callbacks
        private void OnDisable()
        {
            if (IsTransactionProcessingEnabled) CleanUp();
        }
        #endregion

        #region Public Methods
        public void ConfigureTransactionProcessing(InAppPurchaseProcessingOptions options)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("Stop the pseudo provider before changing transaction processing options.");
            }

            CleanUpProcessing();
            ProcessingOptions = options;
            LastProcessingResult = null;
        }

        public void Tick(float deltaTime)
        {
            var pending = _pendingSimulation;
            if (!IsTransactionProcessingEnabled || !IsInitialized || pending == null || !pending.Ready) return;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f) return;

            pending.RetryRemaining -= deltaTime;
            if (pending.RetryRemaining <= 0f) ProcessSimulation(pending, _processingGeneration);
        }

        public void Dispose() => CleanUp();
        #endregion

        #region Private Methods
        private void InitializeProcessing()
        {
            if (IsInitialized) return;
            CleanUpProcessing();
            int generation = _processingGeneration;
            InAppPurchaseTransactionPipeline pipeline = null;
            pipeline = new InAppPurchaseTransactionPipeline(
                ProcessingOptions,
                (order, transaction) =>
                {
                    if (generation != _processingGeneration || !IsInitialized) return;
                    pipeline.ReportAcknowledgement(transaction.TransactionId, transaction.IsRestoration, true);
                },
                transaction =>
                {
                    if (generation != _processingGeneration || !IsInitialized || _pendingSimulation == null) return;
                    _pendingSimulation.Publication = transaction;
                }
            );
            _processingPipeline = pipeline;
            LastProcessingResult = null;
            IsInitialized = true;
            QuickLog.Warning<PseudoInAppPurchaseProvider>(
                "Pseudo purchases use the configured verifier and durable fulfillment before success."
            );
        }

        private void BuyProductWithProcessing(string productId, PurchaseHandleSource source)
        {
            IInAppPurchaseProduct product;
            try
            {
                product = Manager?.ProductDatabase?.Products?.FirstOrDefault(
                    item => item != null && string.Equals(item.ProductId, productId, StringComparison.Ordinal)
                );
            }
            catch (Exception exception)
            {
                RejectSimulation(source, null, InAppPurchasePipelineOutcome.Rejected, PurchaseStatus.Failed, "Cannot read the product catalog: " + exception.Message);
                return;
            }

            if (!IsInitialized || _processingPipeline == null)
            {
                RejectSimulation(source, product, InAppPurchasePipelineOutcome.Rejected, PurchaseStatus.Unavailable, "The pseudo provider is not initialized.");
                return;
            }
            if (product == null)
            {
                RejectSimulation(source, null, InAppPurchasePipelineOutcome.Rejected, PurchaseStatus.Failed, "The product is not in the configured catalog.");
                return;
            }
            if (_pendingSimulation != null)
            {
                RejectSimulation(source, product, InAppPurchasePipelineOutcome.Busy, PurchaseStatus.Busy, "A simulated purchase is already pending.");
                return;
            }

            InAppPurchaseOrderData order;
            try
            {
                string storeId = ProcessingOptions.ResolveStoreProductId(product, Application.platform);
                order = new InAppPurchaseOrderData(
                    "EditorPseudo:" + Guid.NewGuid().ToString("N"),
                    string.Empty,
                    string.Empty,
                    Application.platform,
                    InAppPurchaseOrderSource.Simulated,
                    InAppPurchaseOrderKind.Pending,
                    new[] { new InAppPurchaseLineItem(product.ProductId, storeId, 1, product.AllowRecover) }
                );
            }
            catch (Exception exception)
            {
                RejectSimulation(source, product, InAppPurchasePipelineOutcome.Rejected, PurchaseStatus.Failed, "Cannot create the simulated order: " + exception.Message);
                return;
            }

            var pending = new PendingSimulation(product, order, source);
            int generation = _processingGeneration;
            _pendingSimulation = pending;
            LastProcessingResult = null;
            LastPurchaseStatus = PurchaseStatus.Pending;
            NotifyProcessing(PurchaseInitiated, product);
            if (!IsCurrentSimulation(pending, generation)) return;

            PurchaseStatus simulatedOutcome;
            try
            {
                simulatedOutcome = ProcessingOptions.SimulationPolicy?.SelectOutcome(productId)
                    ?? PurchaseStatus.Confirmed;
            }
            catch (Exception exception)
            {
                _pendingSimulation = null;
                RejectSimulation(source, product, InAppPurchasePipelineOutcome.Rejected, PurchaseStatus.Failed,
                    "The simulation policy failed: " + exception.Message);
                return;
            }
            if (simulatedOutcome != PurchaseStatus.Confirmed)
            {
                _pendingSimulation = null;
                LastPurchaseStatus = simulatedOutcome;
                LastProcessingResult = new InAppPurchasePipelineResult(
                    simulatedOutcome == PurchaseStatus.Deferred ? InAppPurchasePipelineOutcome.Deferred
                        : simulatedOutcome == PurchaseStatus.Busy ? InAppPurchasePipelineOutcome.Busy
                        : InAppPurchasePipelineOutcome.Rejected, null, "The simulated purchase returned " + simulatedOutcome + "."
                );
                source.TryComplete(simulatedOutcome, LastProcessingResult.Reason);
                NotifyProcessing(simulatedOutcome == PurchaseStatus.Deferred ? PurchaseDeferred : PurchaseFailed, product);
                return;
            }

            Action complete = () =>
            {
                if (!IsCurrentSimulation(pending, generation) || pending.Ready) return;
                pending.Ready = true;
                ProcessSimulation(pending, generation);
            };
            try
            {
                if (ScheduleCompletion == null) DelayedCall(complete);
                else ScheduleCompletion(complete);
            }
            catch (Exception exception)
            {
                if (!IsCurrentSimulation(pending, generation)) return;
                _pendingSimulation = null;
                RejectSimulation(source, product, InAppPurchasePipelineOutcome.Rejected, PurchaseStatus.Failed, "Cannot schedule the simulated purchase: " + exception.Message);
            }
        }

        private void ProcessSimulation(PendingSimulation pending, int generation)
        {
            if (!IsCurrentSimulation(pending, generation)) return;
            pending.Publication = null;
            InAppPurchasePipelineResult result;
            try
            {
                result = _processingPipeline.Process(pending.Order);
            }
            catch (Exception exception)
            {
                result = new InAppPurchasePipelineResult(
                    InAppPurchasePipelineOutcome.Rejected, null, "Cannot process the simulated purchase: " + exception.Message
                );
            }
            if (!IsCurrentSimulation(pending, generation)) return;
            LastProcessingResult = result;

            if (result.Outcome == InAppPurchasePipelineOutcome.Retry || result.Outcome == InAppPurchasePipelineOutcome.Busy)
            {
                pending.RetryRemaining = ProcessingRetryInterval;
                pending.Attempts++;
                LastPurchaseStatus = PurchaseStatus.Pending;
                bool retryAllowed = ProcessingOptions.MaxProcessingAttempts == 0 ||
                    pending.Attempts < ProcessingOptions.MaxProcessingAttempts;
                if (!retryAllowed) pending.RetryRemaining = float.PositiveInfinity;
                NotifyProcessing(PurchaseFailed, pending.Product);
                return;
            }

            _pendingSimulation = null;
            if (result.Outcome == InAppPurchasePipelineOutcome.Completed)
            {
                VerifiedInAppPurchaseTransaction transaction = pending.Publication ?? result.Transaction;
                LastPurchaseStatus = PurchaseStatus.Confirmed;
                pending.Source.TryComplete(
                    PurchaseStatus.Confirmed,
                    transactionId: transaction?.TransactionId
                );
                if (pending.Publication != null)
                    NotifyProcessing(pending.Publication.IsRestoration ? ProductRestored : PurchaseSucceeded, pending.Product);
                return;
            }
            if (result.Outcome == InAppPurchasePipelineOutcome.Deferred)
            {
                LastPurchaseStatus = PurchaseStatus.Deferred;
                pending.Source.TryComplete(PurchaseStatus.Deferred, result.Reason, result.Transaction?.TransactionId);
                NotifyProcessing(PurchaseDeferred, pending.Product);
                return;
            }

            // Simulated purchases cannot fetch a real store transaction. A verifier
            // requiring that handoff must reject simulation rather than grant it.
            LastPurchaseStatus = result.Outcome == InAppPurchasePipelineOutcome.WaitForStore
                ? PurchaseStatus.Pending : PurchaseStatus.Failed;
            pending.Source.TryComplete(LastPurchaseStatus, result.Reason, result.Transaction?.TransactionId);
            NotifyProcessing(PurchaseFailed, pending.Product);
        }

        private bool IsCurrentSimulation(PendingSimulation pending, int generation) =>
            IsInitialized && generation == _processingGeneration &&
            ReferenceEquals(_pendingSimulation, pending) && _processingPipeline != null;

        private void RejectSimulation(
            PurchaseHandleSource source,
            IInAppPurchaseProduct product,
            InAppPurchasePipelineOutcome outcome,
            PurchaseStatus status,
            string reason
        )
        {
            LastPurchaseStatus = status;
            LastProcessingResult = new InAppPurchasePipelineResult(outcome, null, reason);
            source?.TryComplete(status, reason);
            NotifyProcessing(PurchaseFailed, product);
        }

        private void CleanUpProcessing()
        {
            ++_processingGeneration;
            if (_pendingSimulation != null)
            {
                const string reason = "The simulated purchase was cancelled by provider cleanup.";
                LastProcessingResult = new InAppPurchasePipelineResult(
                    InAppPurchasePipelineOutcome.Disposed, null, reason
                );
                _pendingSimulation.Source.TryComplete(PurchaseStatus.Pending, reason);
            }
            _pendingSimulation = null;
            var pipeline = _processingPipeline;
            _processingPipeline = null;
            pipeline?.Dispose();
        }

        private static void NotifyProcessing(Action<IInAppPurchaseProduct> listeners, IInAppPurchaseProduct product)
        {
            if (listeners == null) return;
            foreach (Action<IInAppPurchaseProduct> listener in listeners.GetInvocationList())
            {
                try { listener(product); }
                catch (Exception exception)
                {
                    QuickLog.Error<PseudoInAppPurchaseProvider>("A simulated purchase listener failed: " + exception.Message);
                }
            }
        }
        #endregion

        #region Nested Types
        private sealed class PendingSimulation
        {
            internal readonly IInAppPurchaseProduct Product;
            internal readonly InAppPurchaseOrderData Order;
            internal readonly PurchaseHandleSource Source;
            internal bool Ready;
            internal float RetryRemaining;
            internal int Attempts = 1;
            internal VerifiedInAppPurchaseTransaction Publication;

            internal PendingSimulation(
                IInAppPurchaseProduct product,
                InAppPurchaseOrderData order,
                PurchaseHandleSource source
            )
            {
                Product = product;
                Order = order;
                Source = source;
            }
        }
        #endregion
    }
}
