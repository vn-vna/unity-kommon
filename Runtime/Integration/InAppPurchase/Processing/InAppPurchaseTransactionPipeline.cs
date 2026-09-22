using System;
using System.Collections.Generic;
using System.Threading;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Processing
{
    /// <summary>
    /// SDK-independent, synchronous transaction processing. Construct and call on the
    /// Unity main thread; callbacks run inline on that thread. The SDK bridge owns all
    /// timers, native-order references, store fetches and callback generation guards.
    /// In-memory fulfillment/publication/acknowledgement state is keyed by verified
    /// transaction identity plus purchase/restoration kind. Durable dedup belongs to
    /// Fulfillment and must survive this pipeline being disposed and recreated.
    /// </summary>
    public sealed class InAppPurchaseTransactionPipeline : IDisposable
    {
        private readonly InAppPurchaseProcessingOptions _options;
        private readonly int _ownerThreadId;
        private readonly Dictionary<TransactionKey, Entry> _entries = new Dictionary<TransactionKey, Entry>();
        private Action<InAppPurchaseOrderData, VerifiedInAppPurchaseTransaction> _acknowledge;
        private Action<VerifiedInAppPurchaseTransaction> _publish;
        private Action<InAppPurchaseOrderData, InAppPurchasePipelineOutcome, string> _report;
        private bool _processing;
        private bool _disposed;

        public bool IsDisposed => _disposed;

        public InAppPurchaseTransactionPipeline(
            InAppPurchaseProcessingOptions options,
            Action<InAppPurchaseOrderData, VerifiedInAppPurchaseTransaction> acknowledge,
            Action<VerifiedInAppPurchaseTransaction> publish,
            Action<InAppPurchaseOrderData, InAppPurchasePipelineOutcome, string> report = null
        )
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _acknowledge = acknowledge ?? throw new ArgumentNullException(nameof(acknowledge));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _report = report;
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public InAppPurchasePipelineResult Process(InAppPurchaseOrderData order)
        {
            EnsureOwnerThread();
            if (_disposed) return Stopped(InAppPurchasePipelineOutcome.Disposed);
            if (_processing) return Stopped(InAppPurchasePipelineOutcome.Busy);
            _processing = true;
            try
            {
                return ProcessCore(order);
            }
            finally
            {
                _processing = false;
            }
        }

        /// <summary>
        /// Retries only the store acknowledgement. To retry an in-flight attempt after
        /// a watchdog expires, report its failure first. No verification, fulfillment
        /// or publication callback is invoked here.
        /// </summary>
        public InAppPurchasePipelineResult RetryAcknowledgement(string transactionId, bool isRestoration)
        {
            EnsureOwnerThread();
            if (_disposed) return Stopped(InAppPurchasePipelineOutcome.Disposed);
            if (_processing) return Stopped(InAppPurchasePipelineOutcome.Busy);
            if (!_entries.TryGetValue(new TransactionKey(transactionId, isRestoration), out Entry entry) || !entry.Fulfilled)
            {
                return new InAppPurchasePipelineResult(
                    InAppPurchasePipelineOutcome.Rejected, null, "No fulfilled transaction is available for acknowledgement."
                );
            }
            _processing = true;
            try
            {
                TryAcknowledge(entry);
                return _disposed ? Stopped(InAppPurchasePipelineOutcome.Disposed) : Snapshot(entry);
            }
            finally
            {
                _processing = false;
            }
        }

        /// <summary>
        /// Reports a native response; may be called synchronously by the acknowledgement
        /// callback. Success after a watchdog failure is accepted. A failure after success
        /// is ignored. The bridge must reject responses from older provider generations.
        /// </summary>
        public bool ReportAcknowledgement(string transactionId, bool isRestoration, bool succeeded)
        {
            EnsureOwnerThread();
            if (_disposed || !_entries.TryGetValue(new TransactionKey(transactionId, isRestoration), out Entry entry)) return false;
            if (!entry.Fulfilled || !entry.AcknowledgementRequired || !entry.AcknowledgementRequested || entry.Acknowledged) return false;
            if (!succeeded && !entry.AcknowledgementInFlight) return false;
            entry.AcknowledgementInFlight = false;
            entry.Acknowledged = succeeded;
            entry.Reason = succeeded ? string.Empty : "Store acknowledgement failed; fulfillment remains committed.";
            return true;
        }

        public bool IsAcknowledged(string transactionId, bool isRestoration)
        {
            EnsureOwnerThread();
            return !_disposed && _entries.TryGetValue(new TransactionKey(transactionId, isRestoration), out Entry entry) && entry.Acknowledged;
        }

        public bool TryGetProgress(string transactionId, bool isRestoration, out InAppPurchasePipelineResult result)
        {
            EnsureOwnerThread();
            result = null;
            if (_disposed || !_entries.TryGetValue(new TransactionKey(transactionId, isRestoration), out Entry entry)) return false;
            result = Snapshot(entry);
            return true;
        }

        public void Dispose()
        {
            EnsureOwnerThread();
            if (_disposed) return;
            _disposed = true;
            _entries.Clear();
            _acknowledge = null;
            _publish = null;
            _report = null;
        }

        private InAppPurchasePipelineResult ProcessCore(InAppPurchaseOrderData order)
        {
            if (order == null) return Report(order, InAppPurchasePipelineOutcome.Rejected, "Order is missing.");
            if (order.Kind == InAppPurchaseOrderKind.Deferred)
            {
                return Report(order, InAppPurchasePipelineOutcome.Deferred, "Store approval is deferred.");
            }
            string error = ValidateOrder(order);
            if (error != null) return Report(order, InAppPurchasePipelineOutcome.Rejected, error);

            InAppPurchaseRestorationKind classification;
            try
            {
                classification = Classify(order);
            }
            catch (Exception exception)
            {
                return Report(order, InAppPurchasePipelineOutcome.Rejected, "Restoration policy failed: " + exception.Message);
            }
            if (_disposed) return Stopped(InAppPurchasePipelineOutcome.Disposed);
            if (classification == InAppPurchaseRestorationKind.Ignore)
            {
                return Report(order, InAppPurchasePipelineOutcome.Ignored, "Order was excluded by restoration policy.");
            }
            if (classification != InAppPurchaseRestorationKind.Purchase && classification != InAppPurchaseRestorationKind.Restoration)
            {
                return Report(order, InAppPurchasePipelineOutcome.Rejected, "Restoration policy returned an invalid classification.");
            }

            bool isRestoration = classification == InAppPurchaseRestorationKind.Restoration;
            VerifiedInAppPurchaseTransaction transaction;
            InAppPurchaseVerificationOutcome verification;
            try
            {
                verification = _options.Verifier.Verify(order, isRestoration, out transaction, out error);
            }
            catch (Exception exception)
            {
                return Report(order, InAppPurchasePipelineOutcome.Retry, "Verification could not complete: " + exception.Message);
            }
            if (_disposed) return Stopped(InAppPurchasePipelineOutcome.Disposed);
            if (verification != InAppPurchaseVerificationOutcome.Verified)
            {
                return Report(order, MapVerification(verification), error);
            }
            error = ValidateVerified(order, transaction, isRestoration);
            if (error != null) return Report(order, InAppPurchasePipelineOutcome.Rejected, error);

            var key = new TransactionKey(transaction.TransactionId, isRestoration);
            if (!_entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry(order, transaction);
                _entries.Add(key, entry);
            }
            else
            {
                if (entry.Transaction.Platform != transaction.Platform || !SameItems(entry.Transaction.Items, transaction.Items))
                {
                    return Report(order, InAppPurchasePipelineOutcome.Rejected, "Verified identity was reused for a different cart or platform.");
                }
                entry.Order = order;
                if (order.Kind == InAppPurchaseOrderKind.Confirmed)
                {
                    entry.Acknowledged = true;
                    entry.AcknowledgementInFlight = false;
                }
                else if (!entry.Acknowledged)
                {
                    entry.AcknowledgementRequired = true;
                }
            }

            if (!entry.Fulfilled && entry.Outcome != InAppPurchasePipelineOutcome.Rejected)
            {
                Fulfill(entry);
            }
            if (_disposed) return Stopped(InAppPurchasePipelineOutcome.Disposed);
            if (!entry.Fulfilled)
            {
                return Report(order, entry.Outcome, entry.Reason, entry);
            }
            TryAcknowledge(entry);
            if (_disposed) return Stopped(InAppPurchasePipelineOutcome.Disposed);
            Publish(entry);
            return _disposed ? Stopped(InAppPurchasePipelineOutcome.Disposed) : Snapshot(entry);
        }

        private void Fulfill(Entry entry)
        {
            InAppPurchaseProcessingOutcome outcome;
            string reason;
            try
            {
                outcome = _options.Fulfillment.Fulfill(entry.Transaction, out reason);
            }
            catch (Exception exception)
            {
                outcome = InAppPurchaseProcessingOutcome.Retry;
                reason = "Fulfillment could not complete: " + exception.Message;
            }
            if (_disposed) return;
            entry.Reason = reason ?? string.Empty;
            switch (outcome)
            {
                case InAppPurchaseProcessingOutcome.Completed:
                    entry.Fulfilled = true;
                    entry.Outcome = InAppPurchasePipelineOutcome.Completed;
                    break;
                case InAppPurchaseProcessingOutcome.Retry:
                    entry.Outcome = InAppPurchasePipelineOutcome.Retry;
                    break;
                default:
                    entry.Outcome = InAppPurchasePipelineOutcome.Rejected;
                    if (string.IsNullOrEmpty(entry.Reason)) entry.Reason = "Fulfillment rejected the transaction.";
                    break;
            }
        }

        private void TryAcknowledge(Entry entry)
        {
            if (!entry.Fulfilled || !entry.AcknowledgementRequired || entry.Acknowledged || entry.AcknowledgementInFlight) return;
            entry.AcknowledgementInFlight = true;
            entry.AcknowledgementRequested = true;
            try
            {
                _acknowledge(entry.Order, entry.Transaction);
            }
            catch (Exception exception)
            {
                if (_disposed || entry.Acknowledged) return;
                entry.AcknowledgementInFlight = false;
                entry.Reason = "Acknowledgement could not complete: " + exception.Message;
            }
        }

        private void Publish(Entry entry)
        {
            if (entry.Published) return;
            // Set before user code: a throwing listener may already have delivered to
            // another observer. Re-emitting must not be used as notification recovery.
            entry.Published = true;
            try
            {
                _publish(entry.Transaction);
            }
            catch (Exception exception)
            {
                if (!_disposed) entry.Reason = "Publication callback failed: " + exception.Message;
            }
        }

        private InAppPurchasePipelineResult Report(
            InAppPurchaseOrderData order,
            InAppPurchasePipelineOutcome outcome,
            string reason,
            Entry entry = null
        )
        {
            if (_disposed) return Stopped(InAppPurchasePipelineOutcome.Disposed);
            reason = reason ?? string.Empty;
            try
            {
                _report?.Invoke(order, outcome, reason);
            }
            catch (Exception exception)
            {
                reason += " Report callback failed: " + exception.Message;
            }
            if (_disposed) return Stopped(InAppPurchasePipelineOutcome.Disposed);
            if (entry == null) return new InAppPurchasePipelineResult(outcome, null, reason);
            entry.Reason = reason;
            return Snapshot(entry);
        }

        private InAppPurchaseRestorationKind Classify(InAppPurchaseOrderData order)
        {
            if (_options.RestorationPolicy != null) return _options.RestorationPolicy.Classify(order);
            if (order.Kind == InAppPurchaseOrderKind.Pending) return InAppPurchaseRestorationKind.Purchase;
            foreach (InAppPurchaseLineItem item in order.Items)
            {
                if (!item.AllowRecover) return InAppPurchaseRestorationKind.Ignore;
            }
            return InAppPurchaseRestorationKind.Restoration;
        }

        private static string ValidateOrder(InAppPurchaseOrderData order)
        {
            if (string.IsNullOrWhiteSpace(order.NativeTransactionId)) return "Native transaction identity is missing.";
            if (order.Kind != InAppPurchaseOrderKind.Pending && order.Kind != InAppPurchaseOrderKind.Confirmed) return "Order kind is invalid.";
            if (order.Source != InAppPurchaseOrderSource.Direct && order.Source != InAppPurchaseOrderSource.Fetched && order.Source != InAppPurchaseOrderSource.Simulated)
            {
                return "Order source is invalid.";
            }
            if (order.Items.Count == 0) return "Order cart is empty.";
            foreach (InAppPurchaseLineItem item in order.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ProductId) || string.IsNullOrWhiteSpace(item.StoreProductId) || item.Quantity <= 0)
                {
                    return "Order cart contains an invalid product identity or quantity.";
                }
            }
            return null;
        }

        private static string ValidateVerified(InAppPurchaseOrderData order, VerifiedInAppPurchaseTransaction transaction, bool isRestoration)
        {
            if (transaction == null || string.IsNullOrWhiteSpace(transaction.TransactionId)) return "Verifier did not return a stable transaction identity.";
            if (transaction.Platform != order.Platform || transaction.IsRestoration != isRestoration)
            {
                return "Verifier changed the order platform or restoration classification.";
            }
            return SameItems(order.Items, transaction.Items) ? null : "Verifier changed the order cart.";
        }

        private static bool SameItems(IReadOnlyList<InAppPurchaseLineItem> expected, IReadOnlyList<InAppPurchaseLineItem> actual)
        {
            if (expected.Count != actual.Count) return false;
            for (int i = 0; i < expected.Count; i++)
            {
                InAppPurchaseLineItem a = expected[i];
                InAppPurchaseLineItem b = actual[i];
                if (a == null || b == null || !string.Equals(a.ProductId, b.ProductId, StringComparison.Ordinal) ||
                    !string.Equals(a.StoreProductId, b.StoreProductId, StringComparison.Ordinal) ||
                    a.Quantity != b.Quantity || a.AllowRecover != b.AllowRecover) return false;
            }
            return true;
        }

        private static InAppPurchasePipelineOutcome MapVerification(InAppPurchaseVerificationOutcome outcome)
        {
            switch (outcome)
            {
                case InAppPurchaseVerificationOutcome.Retry: return InAppPurchasePipelineOutcome.Retry;
                case InAppPurchaseVerificationOutcome.WaitForStore: return InAppPurchasePipelineOutcome.WaitForStore;
                default: return InAppPurchasePipelineOutcome.Rejected;
            }
        }

        private static InAppPurchasePipelineResult Snapshot(Entry entry)
        {
            return new InAppPurchasePipelineResult(
                entry.Outcome, entry.Transaction, entry.Reason, entry.Published,
                entry.AcknowledgementRequested,
                entry.Fulfilled && entry.AcknowledgementRequired && !entry.Acknowledged,
                entry.AcknowledgementInFlight
            );
        }

        private static InAppPurchasePipelineResult Stopped(InAppPurchasePipelineOutcome outcome)
        {
            return new InAppPurchasePipelineResult(outcome, null, outcome == InAppPurchasePipelineOutcome.Disposed
                ? "Transaction pipeline is disposed." : "Transaction pipeline is processing another callback.");
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException("Transaction pipeline must be used on its creating Unity main thread.");
            }
        }

        private readonly struct TransactionKey : IEquatable<TransactionKey>
        {
            private readonly string _id;
            private readonly bool _restoration;
            public TransactionKey(string id, bool restoration) { _id = id ?? string.Empty; _restoration = restoration; }
            public bool Equals(TransactionKey other) => _restoration == other._restoration && string.Equals(_id, other._id, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is TransactionKey other && Equals(other);
            public override int GetHashCode() => unchecked((StringComparer.Ordinal.GetHashCode(_id) * 397) ^ _restoration.GetHashCode());
        }

        private sealed class Entry
        {
            public InAppPurchaseOrderData Order;
            public readonly VerifiedInAppPurchaseTransaction Transaction;
            public InAppPurchasePipelineOutcome Outcome = InAppPurchasePipelineOutcome.Retry;
            public string Reason = string.Empty;
            public bool Fulfilled;
            public bool Published;
            public bool AcknowledgementRequired;
            public bool AcknowledgementRequested;
            public bool AcknowledgementInFlight;
            public bool Acknowledged;

            public Entry(InAppPurchaseOrderData order, VerifiedInAppPurchaseTransaction transaction)
            {
                Order = order;
                Transaction = transaction;
                AcknowledgementRequired = order.Kind == InAppPurchaseOrderKind.Pending;
                Acknowledged = order.Kind == InAppPurchaseOrderKind.Confirmed;
            }
        }
    }
}
