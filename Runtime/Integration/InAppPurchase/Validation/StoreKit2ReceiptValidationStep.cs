using Com.Scheherazade.Common.Integration.InAppPurchase.Processing;
using UnityEngine;

namespace Com.Scheherazade.Common.Integration.InAppPurchase.Validation
{
    [CreateAssetMenu(
        fileName = "StoreKit2ReceiptValidationStep",
        menuName = "Scheherazade/IAP Validation/Steps/StoreKit 2 Receipt"
    )]
    public sealed class StoreKit2ReceiptValidationStep : InAppPurchaseReceiptValidationStep
    {
        public override bool ProvidesAuthenticity => true;

        public override bool ValidateConfiguration(RuntimePlatform platform, out string reason)
        {
            bool valid = platform == RuntimePlatform.IPhonePlayer;
            reason = valid ? string.Empty : "StoreKit 2 validation is applicable only to iPhonePlayer.";
            return valid;
        }

        public override InAppPurchaseReceiptValidationResult Validate(
            InAppPurchaseReceiptValidationContext context
        )
        {
            InAppPurchaseOrderData order = context?.Order;
            if (order == null || order.Platform != RuntimePlatform.IPhonePlayer)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "StoreKit 2 receipt validation requires an iOS order."
                );
            if (order.Source == InAppPurchaseOrderSource.Direct)
                return InAppPurchaseReceiptValidationResult.WaitForStore(
                    "Wait for the App Store fetched transaction before validating StoreKit 2 data."
                );
            if (order.Source != InAppPurchaseOrderSource.Fetched)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "StoreKit 2 validation requires fetched store provenance."
                );
            if (string.IsNullOrWhiteSpace(order.NativeTransactionId) ||
                string.IsNullOrWhiteSpace(order.Jws))
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "The fetched StoreKit 2 transaction is missing identity or JWS data."
                );

            if (!context.TrySetStableTransactionId(
                    "AppleAppStore:" + order.NativeTransactionId, out string reason) ||
                !context.TrySetEffectiveReceipt(order.Jws, out reason))
                return InAppPurchaseReceiptValidationResult.Rejected(reason);

            return InAppPurchaseReceiptValidationResult.Passed();
        }
    }
}
