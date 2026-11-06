using System;
using Com.Scheherazade.Common.Integration.InAppPurchase.Processing;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.InAppPurchase.Validation
{
    [CreateAssetMenu(
        fileName = "EditorSimulatedReceiptValidationStep",
        menuName = "Scheherazade/IAP Validation/Steps/Editor Simulated Receipt"
    )]
    public sealed class EditorSimulatedReceiptValidationStep : InAppPurchaseReceiptValidationStep
    {
        public override bool ProvidesAuthenticity => true;

        public override bool ValidateConfiguration(RuntimePlatform platform, out string reason)
        {
            bool valid = IsEditorPlatform(platform);
            reason = valid ? string.Empty : "Editor simulation validation applies only to Editor platforms.";
            return valid;
        }

        public override InAppPurchaseReceiptValidationResult Validate(
            InAppPurchaseReceiptValidationContext context
        )
        {
            InAppPurchaseOrderData order = context?.Order;
            if (order == null || !IsEditorPlatform(order.Platform))
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "Simulated receipt validation requires an Editor platform."
                );
            if (order.Source != InAppPurchaseOrderSource.Simulated)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "Editor receipt validation accepts only simulated orders."
                );
            if (context.IsRestoration)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "Editor simulated transactions cannot be restorations."
                );
            if (string.IsNullOrWhiteSpace(order.NativeTransactionId) ||
                !order.NativeTransactionId.StartsWith("EditorPseudo:", StringComparison.Ordinal))
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "The simulated transaction identity is invalid."
                );
            if (!context.TrySetStableTransactionId(order.NativeTransactionId, out string reason))
                return InAppPurchaseReceiptValidationResult.Rejected(reason);
            return InAppPurchaseReceiptValidationResult.Passed();
        }

        private static bool IsEditorPlatform(RuntimePlatform platform) =>
            platform == RuntimePlatform.WindowsEditor ||
            platform == RuntimePlatform.OSXEditor ||
            platform == RuntimePlatform.LinuxEditor;
    }
}
