using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Processing;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Validation
{
    [CreateAssetMenu(
        fileName = "SingleProductOrderValidationStep",
        menuName = "Scheherazade/IAP Validation/Steps/Single Product Order"
    )]
    public sealed class SingleProductOrderValidationStep : InAppPurchaseReceiptValidationStep
    {
        public override InAppPurchaseReceiptValidationResult Validate(
            InAppPurchaseReceiptValidationContext context
        )
        {
            InAppPurchaseOrderData order = context?.Order;
            if (order == null || order.Items.Count != 1)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "The order must contain exactly one product line."
                );

            InAppPurchaseLineItem item = order.Items[0];
            if (item == null || item.Quantity != 1)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "The order product quantity must equal one."
                );
            if (string.IsNullOrWhiteSpace(item.ProductId) ||
                string.IsNullOrWhiteSpace(item.StoreProductId))
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "The order product identity is incomplete."
                );
            if (context.IsRestoration && !item.AllowRecover)
                return InAppPurchaseReceiptValidationResult.Rejected(
                    "A non-recoverable product cannot be processed as a restoration."
                );

            return InAppPurchaseReceiptValidationResult.Passed();
        }
    }
}
