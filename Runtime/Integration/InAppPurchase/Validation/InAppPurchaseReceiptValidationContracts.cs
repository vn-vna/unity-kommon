using System;
using System.Collections.Generic;
using Com.Scheherazade.Common.Integration.InAppPurchase.Processing;
using UnityEngine;
using UnityEngine.Serialization;

namespace Com.Scheherazade.Common.Integration.InAppPurchase.Validation
{
    public enum InAppPurchaseReceiptValidationOutcome
    {
        Passed,
        Rejected,
        Retry,
        WaitForStore
    }

    public sealed class InAppPurchaseReceiptValidationResult
    {
        public InAppPurchaseReceiptValidationOutcome Outcome { get; }
        public string Reason { get; }

        private InAppPurchaseReceiptValidationResult(
            InAppPurchaseReceiptValidationOutcome outcome,
            string reason
        )
        {
            Outcome = outcome;
            Reason = reason ?? string.Empty;
        }

        public static InAppPurchaseReceiptValidationResult Passed(string reason = "") =>
            new InAppPurchaseReceiptValidationResult(InAppPurchaseReceiptValidationOutcome.Passed, reason);

        public static InAppPurchaseReceiptValidationResult Rejected(string reason) =>
            new InAppPurchaseReceiptValidationResult(InAppPurchaseReceiptValidationOutcome.Rejected, reason);

        public static InAppPurchaseReceiptValidationResult Retry(string reason) =>
            new InAppPurchaseReceiptValidationResult(InAppPurchaseReceiptValidationOutcome.Retry, reason);

        public static InAppPurchaseReceiptValidationResult WaitForStore(string reason) =>
            new InAppPurchaseReceiptValidationResult(InAppPurchaseReceiptValidationOutcome.WaitForStore, reason);
    }

    /// <summary>
    /// Per-verification mutable assignment context. Steps may assign one stable identity
    /// and one effective receipt. Equal reassignment is idempotent; conflicts fail closed.
    /// </summary>
    public sealed class InAppPurchaseReceiptValidationContext
    {
        public InAppPurchaseOrderData Order { get; }
        public bool IsRestoration { get; }
        public string StableTransactionId { get; private set; } = string.Empty;
        public string EffectiveReceipt { get; private set; } = string.Empty;
        public bool HasStableTransactionId => !string.IsNullOrWhiteSpace(StableTransactionId);
        public bool HasEffectiveReceipt => !string.IsNullOrEmpty(EffectiveReceipt);

        public InAppPurchaseReceiptValidationContext(
            InAppPurchaseOrderData order,
            bool isRestoration
        )
        {
            Order = order ?? throw new ArgumentNullException(nameof(order));
            IsRestoration = isRestoration;
        }

        public bool TrySetStableTransactionId(string value, out string reason)
        {
            string candidate = value?.Trim() ?? string.Empty;
            if (candidate.Length == 0)
            {
                reason = "A validation step produced an empty stable transaction identity.";
                return false;
            }
            if (HasStableTransactionId && !string.Equals(
                    StableTransactionId, candidate, StringComparison.Ordinal))
            {
                reason = "Validation steps produced conflicting transaction identities.";
                return false;
            }
            StableTransactionId = candidate;
            reason = string.Empty;
            return true;
        }

        public bool TrySetEffectiveReceipt(string value, out string reason)
        {
            string candidate = value ?? string.Empty;
            if (candidate.Length == 0)
            {
                reason = "A validation step produced an empty effective receipt.";
                return false;
            }
            if (HasEffectiveReceipt && !string.Equals(
                    EffectiveReceipt, candidate, StringComparison.Ordinal))
            {
                reason = "Validation steps produced conflicting effective receipts.";
                return false;
            }
            EffectiveReceipt = candidate;
            reason = string.Empty;
            return true;
        }
    }

    public abstract class InAppPurchaseReceiptValidationStep : ScriptableObject
    {
        [FormerlySerializedAs("platforms")]
        [SerializeField]
        private RuntimePlatform[] acceptedPlatforms = Array.Empty<RuntimePlatform>();

        public IReadOnlyList<RuntimePlatform> AcceptedPlatforms => acceptedPlatforms;
        public virtual bool ProvidesAuthenticity => false;

        public bool IsApplicable(RuntimePlatform platform) =>
            acceptedPlatforms != null && Array.IndexOf(acceptedPlatforms, platform) >= 0;

        public abstract InAppPurchaseReceiptValidationResult Validate(
            InAppPurchaseReceiptValidationContext context
        );

        public virtual bool ValidateConfiguration(RuntimePlatform platform, out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }
}
