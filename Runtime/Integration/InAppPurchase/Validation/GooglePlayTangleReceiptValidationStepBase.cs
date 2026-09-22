using System;
using System.Security.Cryptography;
using System.Text;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Processing;
using UnityEngine;
#if UNITY_PURCHASING
using UnityEngine.Purchasing.Security;
#endif

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Validation
{
    /// <summary>
    /// Project subclasses bind generated obfuscation data directly by overriding
    /// TangleData, for example: GooglePlayTangle.Data().
    /// </summary>
    public abstract class GooglePlayTangleReceiptValidationStepBase :
        InAppPurchaseReceiptValidationStep
    {
        protected abstract byte[] TangleData { get; }
        public override bool ProvidesAuthenticity => true;

        public override bool ValidateConfiguration(RuntimePlatform platform, out string reason)
        {
            reason = string.Empty;
            if (platform != RuntimePlatform.Android)
            {
                reason = "Google Play validation is applicable only to Android.";
                return false;
            }
#if !UNITY_PURCHASING
            reason = "Unity IAP purchasing support is not compiled.";
            return false;
#else
            try
            {
                byte[] data = TangleData;
                if (data == null || data.Length == 0)
                {
                    reason = "Google Play tangle data is missing.";
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                reason = "Google Play tangle data could not be resolved.";
                return false;
            }
#endif
        }

        public override InAppPurchaseReceiptValidationResult Validate(
            InAppPurchaseReceiptValidationContext context
        )
        {
            InAppPurchaseOrderData order = context?.Order;
            if (order == null || order.Platform != RuntimePlatform.Android)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "Google Play receipt validation requires an Android order."
                );
#if !UNITY_PURCHASING
            return InAppPurchaseReceiptValidationResult.Rejected(
                "Unity IAP purchasing support is not compiled."
            );
#else
            if (order.Items.Count != 1 || order.Items[0] == null ||
                order.Items[0].Quantity != 1)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "Google Play validation requires one product with quantity one."
                );
            if (string.IsNullOrEmpty(order.Receipt) ||
                string.IsNullOrEmpty(order.NativeTransactionId))
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "The Google Play order is missing receipt identity data."
                );

            IPurchaseReceipt[] receipts;
            try
            {
                byte[] data = TangleData;
                if (data == null || data.Length == 0)
                    return InAppPurchaseReceiptValidationResult.Rejected(
                        "Google Play tangle data is missing."
                    );
                receipts = ValidateReceipt(order.Receipt, data);
            }
            catch (Exception)
            {
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "Google Play receipt authenticity validation failed."
                );
            }

            InAppPurchaseLineItem item = order.Items[0];
            if (receipts == null || receipts.Length != 1 ||
                receipts[0] is not GooglePlayReceipt google ||
                !string.Equals(google.packageName, Application.identifier, StringComparison.Ordinal) ||
                !string.Equals(google.productID, item.StoreProductId, StringComparison.Ordinal) ||
                google.purchaseState != GooglePurchaseState.Purchased ||
                string.IsNullOrEmpty(google.purchaseToken) ||
                !string.Equals(google.purchaseToken, order.NativeTransactionId, StringComparison.Ordinal))
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "Google Play receipt identity or purchase state is invalid."
                );

            if (!context.TrySetStableTransactionId(
                    "GooglePlay:" + Hash(google.purchaseToken), out string reason) ||
                !context.TrySetEffectiveReceipt(order.Receipt, out reason))
                return InAppPurchaseReceiptValidationResult.Rejected(reason);

            return InAppPurchaseReceiptValidationResult.Passed();
#endif
        }

#if UNITY_PURCHASING
        protected virtual IPurchaseReceipt[] ValidateReceipt(string receipt, byte[] data)
        {
            var validator = new CrossPlatformValidator(data, Application.identifier);
            return validator.Validate(receipt);
        }
#endif

        private static string Hash(string value)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(
                    algorithm.ComputeHash(Encoding.UTF8.GetBytes(value))
                )
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }
}
