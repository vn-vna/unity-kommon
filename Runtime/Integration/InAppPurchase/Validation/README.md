# Ordered receipt validation

InAppPurchaseReceiptValidationPipeline is a ScriptableObject implementation of IInAppPurchaseTransactionVerifier. It resolves one exact platform entry, evaluates that entry's serialized steps in list order, and stops on the first Rejected, Retry, or WaitForStore result. There is no cross-platform fallback.

## Setup

1. Create a pipeline asset from **Scheherazade / IAP Validation / Receipt Validation Pipeline**.
2. Create one serialized `platformSteps` entry per supported OS and order its `steps` list. Duplicate or missing platform entries fail closed.
3. Set each step's `acceptedPlatforms` explicitly. A step must accept the key of every platform entry that references it; an empty list accepts no platform.
4. Keep **Require Validation** enabled for production. The pipeline then requires at least one applicable, passed step whose ProvidesAuthenticity is true.
5. Call ValidateConfiguration(targetPlatform, out reason) in build validation before shipping.
6. Assign the pipeline in the inherited **Receipt Validation** section on the IAP manager asset. The derived manager explicitly supplies `ReceiptValidationPipeline` as the verifier in `InAppPurchaseProcessingOptions`.

Serialized runtime names are `platformSteps` on the pipeline, `platform` and `steps` on each `InAppPurchaseReceiptValidationPlatformSteps` entry, and `acceptedPlatforms` on every step (`FormerlySerializedAs("platforms")` migrates the old step field only). `StepsByPlatform` exposes the exact runtime dictionary; `Steps` remains a flattened compatibility view.

A typical per-platform ordering is:

- SingleProductOrderValidationStep for cart shape and recoverability;
- one OS-specific authenticity step:
  - project subclass of GooglePlayTangleReceiptValidationStepBase on Android;
  - StoreKit2ReceiptValidationStep on IPhonePlayer;
  - EditorSimulatedReceiptValidationStep on Editor platforms only.

Even with RequireValidation disabled, a step must establish a nonempty stable transaction ID or verification fails. The output transaction preserves the exact input line items, platform, and restoration flag. A step can assign the stable transaction identity and effective receipt once; equal reassignment is idempotent, while conflicting assignments reject the order.

## Google Play generated tangle binding

The plugin intentionally performs no reflection or generated-type lookup. Generated tangle code may live outside the plugin assembly. Bind it directly in a project class:

~~~csharp
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Validation;
using UnityEngine;
using UnityEngine.Purchasing.Security;

[CreateAssetMenu(menuName = "Project/IAP/Google Play Receipt Validation")]
public sealed class ProjectGooglePlayReceiptValidationStep
    : GooglePlayTangleReceiptValidationStepBase
{
    protected override byte[] TangleData => GooglePlayTangle.Data();
}
~~~

The base step constructs CrossPlatformValidator, validates the local receipt, requires exactly one GooglePlayReceipt, and checks package name, store product ID, purchased state, and purchaseToken == NativeTransactionId. Its stable ID is GooglePlay:<lowercase SHA256 purchase token>. Tangle bytes, receipts, and purchase tokens are never placed into diagnostic reasons or logs by this module.

Use Unity's Receipt Validation Obfuscator to generate tangle data. Missing or unresolvable data makes ValidateConfiguration fail closed.

## StoreKit 2

StoreKit2ReceiptValidationStep returns WaitForStore for direct callbacks. A fetched iOS order must contain a nonempty JWS and native transaction ID. It assigns AppleAppStore:<native transaction ID> and uses the JWS as the effective receipt.

This step validates **source/provenance and required payload presence only**. It does not cryptographically verify Apple's JWS chain or server state. Perform server-side App Store verification for authoritative or high-value entitlements.

## Editor simulation

EditorSimulatedReceiptValidationStep accepts only Simulated orders on Editor platforms, rejects restoration, and requires the EditorPseudo: identity prefix. It is an explicit test authenticity boundary, not proof of a real store purchase. Never include it in a player-platform pipeline.

## Security limitations

Local receipt validation executes in the client and can be bypassed by a sufficiently capable attacker. Tangle data obfuscates embedded keys; it is not a secret vault or server authority. The processing pipeline must still perform durable, idempotent fulfillment before store acknowledgement. Do not grant content from purchase-success observers.

For authoritative validation and revocation or refund handling, verify transactions on a trusted server and use store server APIs and notifications.

Official references:

- Unity receipt validation: https://docs.unity.com/en-us/iap/receipt-validation
- Unity IAP manual: https://docs.unity3d.com/Packages/com.unity.purchasing@5.3/manual/index.html
- Unity local receipt validation: https://docs.unity3d.com/Manual/UnityIAPValidatingReceipts.html
- Google Play Billing security: https://developer.android.com/google/play/billing/security
- Apple StoreKit transaction and JWS documentation: https://developer.apple.com/documentation/storekit/transaction

## Safe diagnostics

Validation reasons are deliberately generic. Do not append raw receipts, JWS payloads, generated tangle bytes, public keys, purchase tokens, or account identifiers to them. Retry should represent transient inability to decide; malformed or unauthentic input should be Rejected; WaitForStore requests a fetched-store handoff and is not permission to grant or acknowledge.
