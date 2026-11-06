using System;
using System.Threading.Tasks;
using Com.Scheherazade.Common.Logging;

namespace Com.Scheherazade.Common.Integration.InAppPurchase
{
    /// <summary>Immutable result of one caller-correlated purchase action.</summary>
    public sealed class PurchaseResult
    {
        public PurchaseStatus Status { get; }
        public string ProductId { get; }
        public string TransactionId { get; }
        public string Reason { get; }
        public bool WasAlreadyProcessed { get; }

        internal PurchaseResult(
            PurchaseStatus status,
            string productId,
            string transactionId,
            string reason,
            bool wasAlreadyProcessed
        )
        {
            Status = status;
            ProductId = productId ?? string.Empty;
            TransactionId = transactionId ?? string.Empty;
            Reason = reason ?? string.Empty;
            WasAlreadyProcessed = wasAlreadyProcessed;
        }
    }

    /// <summary>Provider-owned mutation source for a caller-visible purchase handle.</summary>
    public sealed class PurchaseHandleSource
    {
        public PurchaseHandle Handle { get; }

        public PurchaseHandleSource(string productId)
        {
            Handle = new PurchaseHandle(productId);
        }

        public bool TryBindTransaction(string transactionId) => Handle.TryBindTransaction(transactionId);

        public bool TryComplete(
            PurchaseStatus status,
            string reason = null,
            string transactionId = null,
            bool wasAlreadyProcessed = false
        ) => Handle.TryComplete(status, reason, transactionId, wasAlreadyProcessed);
    }

    /// <summary>
    /// Read-only representation of one call to BuyProduct. Consumers observe its
    /// correlated result instead of matching global product events. The handle is not
    /// the durable purchase record.
    /// </summary>
    public sealed class PurchaseHandle
    {
        #region Events & Delegates
        public event Action<PurchaseHandle> StatusChanged;
        public event Action<PurchaseHandle> Completed;
        #endregion

        #region Properties
        public string RequestId { get; }
        public string ProductId { get; }
        public string TransactionId { get; private set; } = string.Empty;
        public PurchaseStatus Status { get; private set; } = PurchaseStatus.Pending;
        public string Reason { get; private set; } = string.Empty;
        public bool WasAlreadyProcessed { get; private set; }
        public bool IsCompleted => _completion.Task.IsCompleted;
        public Task<PurchaseResult> Completion => _completion.Task;
        #endregion

        #region Private Fields
        private readonly TaskCompletionSource<PurchaseResult> _completion =
            new TaskCompletionSource<PurchaseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _sync = new object();
        #endregion

        #region Public Methods
        internal PurchaseHandle(string productId)
        {
            RequestId = Guid.NewGuid().ToString("N");
            ProductId = productId ?? string.Empty;
        }

        /// <summary>Provider implementation hook. Binds the verified transaction identity once available.</summary>
        internal bool TryBindTransaction(string transactionId)
        {
            if (string.IsNullOrWhiteSpace(transactionId)) return false;
            bool changed;
            lock (_sync)
            {
                if (IsCompleted) return false;
                if (!string.IsNullOrEmpty(TransactionId))
                    return string.Equals(TransactionId, transactionId, StringComparison.Ordinal);
                TransactionId = transactionId;
                changed = true;
            }
            if (changed) InvokeSafely(StatusChanged);
            return true;
        }

        /// <summary>Provider implementation hook. Completes the caller-facing action exactly once.</summary>
        internal bool TryComplete(
            PurchaseStatus status,
            string reason = null,
            string transactionId = null,
            bool wasAlreadyProcessed = false
        )
        {
            PurchaseResult result;
            lock (_sync)
            {
                if (IsCompleted) return false;
                if (!string.IsNullOrWhiteSpace(transactionId))
                {
                    if (!string.IsNullOrEmpty(TransactionId) &&
                        !string.Equals(TransactionId, transactionId, StringComparison.Ordinal)) return false;
                    TransactionId = transactionId;
                }
                Status = status;
                Reason = reason ?? string.Empty;
                WasAlreadyProcessed = wasAlreadyProcessed;
                result = new PurchaseResult(Status, ProductId, TransactionId, Reason, WasAlreadyProcessed);
                if (!_completion.TrySetResult(result)) return false;
            }
            InvokeSafely(StatusChanged);
            InvokeSafely(Completed);
            return true;
        }
        #endregion

        #region Private Methods
        private void InvokeSafely(Action<PurchaseHandle> listeners)
        {
            if (listeners == null) return;
            foreach (Action<PurchaseHandle> listener in listeners.GetInvocationList())
            {
                try { listener(this); }
                catch (Exception exception)
                {
                    QuickLog.Error<PurchaseHandle>("Purchase handle observer failed: {0}", exception.Message);
                }
            }
        }
        #endregion
    }
}
