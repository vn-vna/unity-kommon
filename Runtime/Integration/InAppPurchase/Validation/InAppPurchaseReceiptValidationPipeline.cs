using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Processing;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Validation
{
    [Serializable]
    public sealed class InAppPurchaseReceiptValidationPlatformSteps
    {
        [SerializeField]
        private RuntimePlatform platform;

        [SerializeField]
        private List<InAppPurchaseReceiptValidationStep> steps = new();

        public RuntimePlatform Platform => platform;
        public IReadOnlyList<InAppPurchaseReceiptValidationStep> Steps => steps;
    }

    [CreateAssetMenu(
        fileName = "InAppPurchaseReceiptValidationPipeline",
        menuName = "Scheherazade/IAP Validation/Receipt Validation Pipeline"
    )]
    public sealed class InAppPurchaseReceiptValidationPipeline :
        ScriptableObject,
        IInAppPurchaseTransactionVerifier
    {
        [SerializeField]
        private List<InAppPurchaseReceiptValidationPlatformSteps> platformSteps = new();

        [SerializeField]
        private bool requireValidation = true;

        [NonSerialized]
        private Dictionary<RuntimePlatform, IReadOnlyList<InAppPurchaseReceiptValidationStep>> _stepsByPlatform;
        [NonSerialized]
        private IReadOnlyList<InAppPurchaseReceiptValidationStep> _flattenedSteps;
        [NonSerialized]
        private string _bindingError;

        public IReadOnlyDictionary<RuntimePlatform, IReadOnlyList<InAppPurchaseReceiptValidationStep>> StepsByPlatform
        {
            get { EnsureBindings(); return _stepsByPlatform; }
        }

        /// <summary>Flattened compatibility view in serialized platform-entry order.</summary>
        public IReadOnlyList<InAppPurchaseReceiptValidationStep> Steps
        {
            get { EnsureBindings(); return _flattenedSteps; }
        }

        public bool RequireValidation => requireValidation;

        public InAppPurchaseVerificationOutcome Verify(
            InAppPurchaseOrderData order,
            bool isRestoration,
            out VerifiedInAppPurchaseTransaction transaction,
            out string reason
        )
        {
            transaction = null;
            reason = string.Empty;
            if (order == null)
            {
                reason = "The purchase order is missing.";
                return InAppPurchaseVerificationOutcome.Rejected;
            }
            if (!TryGetPlatformSteps(order.Platform, out IReadOnlyList<InAppPurchaseReceiptValidationStep> ordered, out reason) ||
                !ValidateOrderedConfiguration(order.Platform, ordered, out reason))
                return InAppPurchaseVerificationOutcome.Rejected;

            var context = new InAppPurchaseReceiptValidationContext(order, isRestoration);
            bool authenticityPassed = false;
            foreach (InAppPurchaseReceiptValidationStep step in ordered)
            {
                InAppPurchaseReceiptValidationResult result;
                try { result = step.Validate(context); }
                catch (Exception)
                {
                    reason = "Receipt validation step " + step.GetType().Name + " failed unexpectedly.";
                    return InAppPurchaseVerificationOutcome.Rejected;
                }
                if (result == null)
                {
                    reason = "Receipt validation step " + step.GetType().Name + " returned no result.";
                    return InAppPurchaseVerificationOutcome.Rejected;
                }
                if (result.Outcome != InAppPurchaseReceiptValidationOutcome.Passed)
                {
                    reason = string.IsNullOrWhiteSpace(result.Reason)
                        ? "Receipt validation did not pass." : result.Reason;
                    return Map(result.Outcome);
                }
                authenticityPassed |= step.ProvidesAuthenticity;
            }

            if (requireValidation && !authenticityPassed)
            {
                reason = "No applicable authenticity validation step passed.";
                return InAppPurchaseVerificationOutcome.Rejected;
            }
            if (!context.HasStableTransactionId)
            {
                reason = "Receipt validation did not establish a stable transaction identity.";
                return InAppPurchaseVerificationOutcome.Rejected;
            }

            transaction = new VerifiedInAppPurchaseTransaction(
                context.StableTransactionId,
                order.Items,
                context.HasEffectiveReceipt ? context.EffectiveReceipt : order.Receipt,
                order.Jws,
                order.Platform,
                isRestoration
            );
            return InAppPurchaseVerificationOutcome.Verified;
        }

        public bool ValidateConfiguration(RuntimePlatform platform, out string reason)
        {
            if (!TryGetPlatformSteps(platform, out IReadOnlyList<InAppPurchaseReceiptValidationStep> ordered, out reason))
                return false;
            return ValidateOrderedConfiguration(platform, ordered, out reason);
        }

        private bool ValidateOrderedConfiguration(
            RuntimePlatform platform,
            IReadOnlyList<InAppPurchaseReceiptValidationStep> ordered,
            out string reason
        )
        {
            reason = string.Empty;
            bool authenticityConfigured = false;
            for (int i = 0; i < ordered.Count; i++)
            {
                InAppPurchaseReceiptValidationStep step = ordered[i];
                try
                {
                    if (!step.ValidateConfiguration(platform, out reason))
                    {
                        if (string.IsNullOrWhiteSpace(reason))
                            reason = "Receipt validation step " + step.GetType().Name + " is not configured.";
                        return false;
                    }
                }
                catch (Exception)
                {
                    reason = "Receipt validation step " + step.GetType().Name + " configuration failed.";
                    return false;
                }
                authenticityConfigured |= step.ProvidesAuthenticity;
            }
            if (requireValidation && !authenticityConfigured)
            {
                reason = "At least one authenticity validation step is required for " + platform + ".";
                return false;
            }
            return true;
        }

        private bool TryGetPlatformSteps(
            RuntimePlatform platform,
            out IReadOnlyList<InAppPurchaseReceiptValidationStep> ordered,
            out string reason
        )
        {
            EnsureBindings();
            ordered = null;
            if (!string.IsNullOrEmpty(_bindingError))
            {
                reason = _bindingError;
                return false;
            }
            if (!_stepsByPlatform.TryGetValue(platform, out ordered))
            {
                reason = "No receipt validation pipeline entry is bound for " + platform + ".";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private void EnsureBindings()
        {
            if (_stepsByPlatform != null) return;
            var dictionary = new Dictionary<RuntimePlatform, IReadOnlyList<InAppPurchaseReceiptValidationStep>>();
            var flattened = new List<InAppPurchaseReceiptValidationStep>();
            string error = string.Empty;
            if (platformSteps == null)
            {
                error = "The receipt validation platform entry list is missing.";
            }
            else
            {
                for (int entryIndex = 0; entryIndex < platformSteps.Count; entryIndex++)
                {
                    InAppPurchaseReceiptValidationPlatformSteps entry = platformSteps[entryIndex];
                    if (entry == null)
                    {
                        error = "Receipt validation platform entry " + entryIndex + " is missing.";
                        break;
                    }
                    if (dictionary.ContainsKey(entry.Platform))
                    {
                        error = "Duplicate receipt validation pipeline entry for " + entry.Platform + ".";
                        break;
                    }
                    if (entry.Steps == null)
                    {
                        error = "Receipt validation steps are missing for " + entry.Platform + ".";
                        break;
                    }
                    var snapshot = new List<InAppPurchaseReceiptValidationStep>(entry.Steps.Count);
                    for (int stepIndex = 0; stepIndex < entry.Steps.Count; stepIndex++)
                    {
                        InAppPurchaseReceiptValidationStep step = entry.Steps[stepIndex];
                        if (step == null)
                        {
                            error = "Receipt validation step " + stepIndex + " is missing for " + entry.Platform + ".";
                            break;
                        }
                        if (!step.IsApplicable(entry.Platform))
                        {
                            error = "Receipt validation step " + step.GetType().Name +
                                " does not accept platform " + entry.Platform + ".";
                            break;
                        }
                        snapshot.Add(step);
                        flattened.Add(step);
                    }
                    if (!string.IsNullOrEmpty(error)) break;
                    dictionary.Add(entry.Platform, new ReadOnlyCollection<InAppPurchaseReceiptValidationStep>(snapshot));
                }
            }
            _stepsByPlatform = dictionary;
            _flattenedSteps = new ReadOnlyCollection<InAppPurchaseReceiptValidationStep>(flattened);
            _bindingError = error;
        }

        private void OnEnable() => InvalidateBindings();
#if UNITY_EDITOR
        private void OnValidate() => InvalidateBindings();
#endif
        private void InvalidateBindings()
        {
            _stepsByPlatform = null;
            _flattenedSteps = null;
            _bindingError = null;
        }

        private static InAppPurchaseVerificationOutcome Map(
            InAppPurchaseReceiptValidationOutcome outcome
        ) => outcome switch
        {
            InAppPurchaseReceiptValidationOutcome.Retry => InAppPurchaseVerificationOutcome.Retry,
            InAppPurchaseReceiptValidationOutcome.WaitForStore => InAppPurchaseVerificationOutcome.WaitForStore,
            _ => InAppPurchaseVerificationOutcome.Rejected
        };
    }
}
