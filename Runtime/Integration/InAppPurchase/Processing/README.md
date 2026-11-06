# Opt-in IAP transaction processing

`UnityInAppPurchaseProvider` and `PseudoInAppPurchaseProvider` keep their existing behavior until explicitly configured. Existing `IInAppPurchaseProvider`, product databases, serialized provider assets and public purchase events remain compatible.

## Enable before initialization

```csharp
using Com.Scheherazade.Common.Integration.InAppPurchase.Processing;

var options = new InAppPurchaseProcessingOptions(
    verifier,
    fulfillment,
    storeIdResolver,      // optional: logical ProductId is the fallback
    restorationPolicy,   // optional: pending = purchase; recoverable confirmed = restoration
    simulationPolicy,    // optional Editor outcome selection
    maxProcessingAttempts: 3, // 0 = retry until cleanup/store redelivery
    restoreTimeoutSeconds: 30
);

((ITransactionProcessingIapProvider)provider).ConfigureTransactionProcessing(options);
manager.RegisterProvider(provider);
manager.Initialize();
```

Both verifier and fulfillment are mandatory. Missing collaborators throw before a store connection is started; this mode never falls back to automatic, unverified confirmation. Pass null options only after `CleanUp()` to return to the legacy behavior. Configure once while stopped, not during a live purchase.

The example uses the existing base manager registration API. `InAppPurchaseManagerBase<T>` forwards one unscaled tick to optional `IInAppPurchaseRetryPump` providers, observes late readiness, and cleans up partially initialized advanced sessions. If a provider is used without that manager, its owner must tick it exactly once per frame on the Unity main thread. Do not separately register the provider with IntegrationCentre as another ticker.

Receipt verification can be supplied by `InAppPurchaseReceiptValidationPipeline`, an ordered OS-dependent ScriptableObject pipeline documented at `../Validation/README.md`. Sand binds that pipeline as its verifier; no project receipt heuristic runs inside the manager.


## Manager-owned receipt validation binding

`InAppPurchaseManagerBase<T>` owns the hidden serialized field `receiptValidationPipeline`. Configure it through **Project Settings > Integration > In-App Purchase > Receipt Validation**, which provides platform tabs, ordered step lists, and default asset creation without duplicating the raw field in the Manager inspector. The public `ReceiptValidationPipeline` property is read-only to consumers and has a protected setter; derived managers and fixture subclasses can also call `SetReceiptValidationPipeline(...)`.

This binding is deliberately passive. The base manager does **not** call `ConfigureTransactionProcessing`, construct processing options, or replace a provider's verifier automatically. A derived manager remains responsible for deciding when and how to pass its bound pipeline into `InAppPurchaseProcessingOptions`, after any project recovery/preflight work and before provider initialization.

```csharp
var options = new InAppPurchaseProcessingOptions(
    ReceiptValidationPipeline,
    fulfillment,
    storeIdResolver,
    restorationPolicy
);
((ITransactionProcessingIapProvider)provider).ConfigureTransactionProcessing(options);
```

Projects can keep another configuration asset as the source of truth, but should then copy that reference into the inherited manager binding explicitly and validate that the two do not drift. Assigning a pipeline in the Receipt Validation settings tab alone never enables validation unless the derived manager supplies it to the provider.

## Responsibilities

- **Store-ID resolver:** map logical product IDs to store IDs for the current platform. Missing or duplicate logical/store IDs fail before opening the store. Current product types remain consumable/non-consumable through `AllowRecover`; this is not subscription support.
- **Verifier:** inspect immutable raw line items, native identity, receipt/JWS, platform, and `Direct`/`Fetched`/`Simulated` provenance. Return `Verified` with a stable transaction ID and matching cart, `Retry`, `Rejected`, or `WaitForStore`. A JWS string alone is not proof of Apple verification. The SDK bridge does not implement a receipt-validation policy on the collaborator’s behalf.
- **Fulfillment:** return `Completed` only after content and durable idempotency/ownership records have been committed atomically or recoverably. `Retry`/exceptions leave the native purchase pending; `Rejected` never confirms it. The plugin does not write wallet or entitlement data.
- **Restoration policy:** optionally classify an order as purchase, restoration or ignore; optionally request platform resynchronization. The advanced default restores confirmed recoverable items, skips confirmed consumables, and requests explicit resync on iOS. No `IAP_Restored_*` keys are used in this mode.

Advanced configuration selects one coherent processing flow; only the entirely unconfigured provider keeps the old immediate-confirm and legacy restore-queue behavior. Optional resolver/policy defaults within advanced mode are not a mixture of the two persistence schemes.

## Ordering and idempotency

```text
raw order -> classify -> verify -> durable fulfillment -> confirm attempt -> publish
                                      |                       |
                                      +-> retry unconfirmed   +-> retry acknowledgement only
```

`Completed`/`PurchaseSucceeded` means durable fulfillment, not necessarily completed store acknowledgement. Failure to acknowledge does not revoke rewards or emit a new purchase failure. Pending and confirmed callbacks are kept separate.

- Per-session notification/fulfillment entries use verified transaction ID plus purchase/restoration kind. Duplicate callbacks cannot fulfill or publish again within the same pipeline.
- Across restarts, durable idempotency is the fulfillment implementation’s responsibility. Events may recur; do not grant rewards in success/restoration observers.
- A purchase and a later restoration may each publish for one transaction. Their durable processing must not replay consumable bundle extras.
- All policy and notification code runs on the main thread. Native SDK callbacks are queued until the next tick. The pure pipeline rejects reentry and use from a different thread.
- Pipeline cleanup invalidates future callbacks but cannot undo a durable commit that already happened. An unfinished native purchase can redeliver and must be safely recognized.

## Purchase handles

`BuyProduct` returns a read-only `PurchaseHandle` before opening the native store. Providers retain a separate `PurchaseHandleSource`, so consumers cannot spoof completion or transaction identity. The handle correlates one caller request without requiring consumers to match global product events. `Completion` resolves exactly once with a `PurchaseResult`; `Confirmed` means durable fulfillment completed, while store acknowledgement may still be retrying internally.

The provider registers the handle before `PurchaseProduct`, so synchronous SDK or simulation callbacks cannot be lost. Unity IAP exposes no caller request token, therefore the designated providers permit only one unbound native purchase at a time. Verification/fulfillment retries keep the handle pending. Cancellation, deferral, unavailability, rejection and busy outcomes complete it directly; cleanup completes an unresolved handle as `Pending` without cancelling the underlying store transaction.

A facade timeout stops waiting only. It does not cancel or invalidate the handle, reward recovery, or acknowledgement. Fetched recovery and restoration orders can run without a caller handle and continue to use durable transaction identity.

## SDK ownership and restoration

Use one live provider per store. Unity IAP 5.3 controller wrappers share underlying services. The advanced adapter disables automatic processing of fetched pending orders so their fetched provenance is preserved; cleanup restores the SDK default `true`. There is no public getter, so this is not a snapshot/restore of another provider’s custom setting. Concurrent providers are unsupported.

A direct order whose verifier returns `WaitForStore` is handed off to a fresh fetched snapshot. If a fetch is already active, the provider drains it and requests a follow-up snapshot rather than trusting that older request to contain the new purchase.

Apple resync automatically fetches purchases in IAP 5.3, before invoking the restore callback. Completion waits for both store resync and the corresponding processed snapshot; it does not issue a competing fetch. A successful empty snapshot is a successful restore.

Native fetches have no cancellation/request-token API. A timeout stops the readiness/restore wait but does not open a competing native request slot. Late results can still complete recovery. A timed-out restore drains its old callback and snapshot before another restore may start. Cleanup invalidates captured callbacks, but it cannot cancel native work delivered through the shared SDK service; exclusive ownership and transaction idempotency remain necessary.

## Retries and cleanup

Connection and explicit fetch failures are retried at most three times per recovery attempt. Missing native callbacks keep their slot reserved rather than spawning overlapping requests. Fulfillment/verification retries are bounded per received order; a future store redelivery/reinitialization can recover an order still pending. Acknowledgement has a watchdog and continues retrying after durable fulfillment without rerunning the journal.

`CleanUp()` disposes the current advanced session, unsubscribes native events, invalidates queued continuations and settles an active restore with false. Options stay attached to the reusable ScriptableObject; initialize creates a fresh session. `Dispose()` delegates to cleanup: do not call both. The caller owns the collaborator objects and any persistence they use.

## Simulation and diagnostics

The opt-in pseudo provider creates one `EditorPseudo:` identity per invocation, uses `Simulated` provenance, and runs the same verifier/fulfillment pipeline before success. A processor retry keeps the identity. Cleanup suppresses delayed callbacks from the disposed generation. Your verifier must explicitly allow simulated transactions in the intended environment; simulation is not receipt verification. The unconfigured pseudo provider retains its legacy one-second auto-success path.

Both designated providers expose `IInAppPurchaseProviderDiagnostics` (`IsInitializing`, `UnavailableReason`, `LastPurchaseStatus`); the pseudo provider additionally exposes `LastProcessingResult`. `Pending`, `Unavailable`, `Busy`, `Canceled` and `Deferred` remain distinct manager-readable outcomes. Rejected unacknowledged native pending orders report `Pending` so an application cannot accidentally initiate another charge. Retry results remain unacknowledged. `maxProcessingAttempts: 0` preserves an application policy that retries durable fulfillment until cleanup, while the default remains bounded. `restoreTimeoutSeconds` lets applications align the provider restore deadline with their UI contract. Do not log full receipts/JWS or raw purchase tokens.

## Sand rollout

Sand now inherits the shared manager base and selects the designated Scheherazade Pseudo provider in Editor or Unity provider on players through its runtime factory. It binds project-owned verifier, SKU resolver, journal fulfillment, restoration and simulation policies after journal recovery and before provider initialization. The inherited serialized provider field remains empty intentionally, avoiding an unconfigured second provider.

Product assets, logical/store IDs, journal prefixes/schema, PlayerPrefs keys, build settings and Unity IAP 5.3 remain unchanged. Legacy Sand provider source remains available only as a rollback/reference until Android and iOS sandbox tests pass. The project config currently fails device readiness because its Google public key is blank and iOS minimum is below the verified flow requirement; this migration does not fabricate or weaken those settings.

Editor regression tests: `Com.Scheherazade.InAppPurchase.EditorTests`. They use a fake store transport and do not open a real store or spend money. Device receipt verification and store sandbox tests remain required before project rollout.
