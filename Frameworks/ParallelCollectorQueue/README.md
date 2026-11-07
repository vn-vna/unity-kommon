# ParallelCollectorQueue

Reusable, headless collector queues for Scheherazade / Unity **6000.0.72f1**.

- Assembly: `Com.Scheherazade.ParallelCollectorQueue`
- Namespace: `Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue`
- Dependencies: **standard managed C# only**. No UnityEngine, Common, game, ConveyorSystem, SandEngine, SDK, scene, render or update-loop reference.

## Included base

- Configurable parallel queues and target depth; one ordered shared reserve.
- Immutable typed/count-bearing definitions, instance-bound collector handles and value snapshots.
- Round-robin initial assignment for ordinary collectors; FIFO refill, last queue to first.
- Front-only, type-matched intake with explicit blocking and partial acceptance. An offer never spills into the next front.
- Two-member linked groups: filled members hold their queue; the final member releases both once.
- Buffered transition facts, bounded notification back-pressure and guarded explicit dispatch/polling.
- Cumulative quantity/box counters, independent consumer tests and deterministic model tests.

This implements the collector-queue base demonstrated by the HTML prototype, **not its presentation or debug undo**. It is not a complete reference-game port. No scene, build profile, package manifest or existing framework is changed by this plugin.

## Tap-to-collect consumer

```csharp
using Com.Scheherazade.Common.Frameworks.ParallelCollectorQueue;

var queue = new ParallelCollectorQueue(new[]
{
    new CollectorDefinition(id: 10, typeId: 0, requiredUnits: 120),
    new CollectorDefinition(id: 20, typeId: 1, requiredUnits: 80),
    new CollectorDefinition(id: 30, typeId: 2, requiredUnits: 160),
    new CollectorDefinition(id: 40, typeId: 3, requiredUnits: 200)
}, queueCount: 4, targetDepth: 3);

// A button captures this expected handle, not just its lane index.
if (queue.TryGetFront(0, out CollectorSnapshot front))
{
    CollectorCollectionStatus status = queue.TryCollect(
        queueIndex: 0,
        expectedFront: front.Handle,
        typeId: front.Definition.TypeId,
        offeredUnits: front.RemainingUnits,
        acceptedUnits: out int accepted
    );
    if (status == CollectorCollectionStatus.Accepted)
    {
        // Credit/reconcile `accepted` units in the caller's ownership ledger now.
        // Start a cosmetic flight only after this transaction is reconciled.
    }
}

while (queue.TryDequeueEvent(out CollectorQueueEvent fact))
{
    // Update the consumer's views/effects from immutable facts.
    // CollectorFilled and CollectorRemoved are different transitions.
}
```

For unit-by-unit delivery, offer `1`. For a bulk offer, the accepted count is clamped to the **named front's** remaining demand. The producer retains `offeredUnits - accepted`. Retry remaining cargo only after re-reading the new front; do not credit it implicitly to the successor.

### Queries

`TryGetFront`, `TryGetCollector`, `GetCollectorAt`, `GetQueuedCollector`, and `GetReserveCollector` return **value snapshots**, not mutable lists. `GetCollectorAt` uses original authored index; collector IDs need not be contiguous. Unknown IDs return false; out-of-range positional/queue queries throw `ArgumentOutOfRangeException`.

`CollectorSnapshot.Location` is Reserve, Queue, or Removed. `IsHeld` means a live queued box has zero remaining units. A default snapshot has an invalid handle and must not be treated as a real box.

`SetQueueBlocked(queueIndex, true)` is an explicit intake gate. It does not move boxes, advance time, or emit lifecycle events; the caller owns gate presentation and releasing it.

## Startup and reserve order

With ordinary definitions 0–23, default four queues/depth three give:

```text
queue 0: [0, 4, 8]      queue 1: [1, 5, 9]
queue 2: [2, 6, 10]     queue 3: [3, 7, 11]
reserve: [12, 13, ...]
```

Ten definitions instead yield `[0,4,8]`, `[1,5,9]`, `[2,6]`, `[3,7]`; no minimum level length is assumed. Completing an ordinary front removes it, fills queue vacancies from reserve and then records front changes. There is no transient empty-queue completion notification before refill.

## Linked groups

```csharp
var linked = new ParallelCollectorQueue(new[]
{
    new CollectorDefinition(10, 0, 120, linkedGroupId: 7),
    new CollectorDefinition(20, 1, 80, linkedGroupId: 7),
    new CollectorDefinition(30, 0, 100)
}, queueCount: 2, targetDepth: 1);
```

Each nonnegative group ID must identify **exactly two** definitions; at least two queues are required. IDs are stable throughout admission/removal.

**Deliberate base policy, not exact Sand Drop placement:** when the next reserve box belongs to a pair, both members are co-admitted at once. The first goes to the queue being filled; the partner goes to the shortest **other** queue, with ties resolved by lower queue index. Removing a partner early preserves the relative order of all other reserve entries. During startup, already-filled positions are skipped as assignment proceeds round-robin.

Pairs may therefore pull a member from later in reserve and exceed a destination's target depth. Target depth is **not a hard capacity**. This co-admission policy keeps both members reachable in distinct queues and preserves admission order of linked groups across queues, avoiding crossed hold cycles. Do not replace it with independently distributing all authored pair members round-robin.

Filling the first member credits its quantity and emits `CollectorFilled`, but the member stays queued and rejects further offers as `FilledHeld`. The final member causes both removals in original authored order, then regular queues refill last-to-first. `CollectorRemoved`, not `CollectorFilled`, is the corresponding hook for effects that require physical/logical queue departure. A held member still contributes to `RemainingCollectorCount`.

No arbitrary dependency graphs, variable-size groups, adaptive color-pressure policy, hidden/extra queues, shuffle or forced nonfront delivery are included in the base.

## Ownership, notifications and back-pressure

`TryCollect` commits demand and queue state synchronously. It never calls observers while a command is mutating state. The caller must reconcile source/transport ownership **before** polling or dispatching facts. Snapshots within facts reflect their individual transition; querying the model in an observer returns the final stable state after removal/refill.

Event order for ordinary completion:

```text
CargoCollected → CollectorFilled → CollectorRemoved
→ zero or more CollectorQueued → FrontChanged → Completed (only if all removed)
```

Partial intake emits only `CargoCollected`. A first linked fill emits `CargoCollected` and `CollectorFilled`. Group release emits two removal facts before any refill facts. Initialization creates no events: query the initial snapshot. Empty input is already completed and emits no completion event.

Choose **one consumption strategy**:

- Poll `TryDequeueEvent(out fact)` into a caller-owned adapter.
- Subscribe `EventPublished += HandleQueueEvent`, then explicitly call `DispatchEvents()` after reconciling cargo. No subscribers leaves events queued. Mutation, polling and nested dispatch from a callback throw `InvalidOperationException`; query methods are safe.

Dispatch is not a durable exactly-once delivery bus: if an observer throws, the current event remains queued. On retry, prior subscribers may see it again; use the instance-local `Sequence` to reconcile/idempotently consume events. Model credits are **not** reapplied by dispatch retries.

Events use preallocated storage. `eventCapacity: 0` automatically chooses `max(64, queueCount + 9)`; explicit capacities must be at least `queueCount + 9`. A completing command reserves a conservative upper bound (all possible front changes plus group/refill facts). It can return `EventBufferFull` before the array is literally full. This rejects the **whole offer** without changing demand, counters, ownership, events or sequence; drain facts and retry. Do not ignore this status or delete producer cargo on rejection.

Expected rejection statuses: InvalidQueue, InvalidQuantity, Empty, StaleCollector, Blocked, TypeMismatch, FilledHeld, EventBufferFull. All return zero accepted units. Invalid construction/query usage throws rather than silently repairing content.

## Quantity and lifecycle contracts

```text
TotalRequiredUnits = CollectedUnits + sum(all snapshots' RemainingUnits)
RemainingCollectorCount = CollectorCount - RemovedCount
```

Quantities use positive `int` per definition/offer, with `long` cumulative totals. Already-removed definitions/snapshots retain their credits for reconciliation. `FilledCount` includes held boxes; `RemovedCount` and completion wait for their removal. No cargo count or animation progress is inferred by this model.

Handles are bound to an instance, not just a numeric ID. `Handle.IsValid` is structural validity; a removed handle remains identifiable but cannot collect a successor. A fresh instance is a new run; old handles reject against it. Dispose/unsubscribe old consumer-side adapters when replacing a run. The model owns no Unity/native resources and needs no `Dispose` or per-frame pump.

This is a **single-owner, single-threaded** model. `TryCollect` is not a persistent cargo-ID/idempotent retry API: presenting the same front handle twice while demand remains accepts two offers. The producer/transport must validate its own generation-checked cargo identity and avoid duplicate dispatch. A conveyor bridge must preflight its withdrawal/notification capacity before collector mutation; this base does not provide a cross-framework transaction/reservation or roll back an arbitrary caller failure.

Animations are cosmetic. An arrival callback must never call `TryCollect` again to confirm a previously credited load. Game code owns win/fail policy; `IsCompleted` and the Completed fact mean all collector definitions have been removed, not that sand, conveyor visuals or other gameplay systems are empty.

## Performance boundaries

Records and intrusive list links live in fixed arrays; reserve removal, queue append/removal and front lookup are O(1). No per-unit reference objects, LINQ, coroutines or Unity objects are created by collection or event polling. Completion checks/notifications scan the small queue count; partner admission searches other queue lengths. Positional list queries walk to the requested position; use front lookup for delivery hot paths.

The warmed partial-collection allocation test excludes construction, NUnit assertions, observer code and presentation. It is not a device benchmark or proof of an allocation-free entire game. Create model data once per run, drain notifications, pool only consumer-owned views, and measure the actual integration.

## Tests

Run the consolidated **EditMode** assemblies:

- `Com.Scheherazade.ParallelCollectorQueue.EditorTests`
- `Com.Scheherazade.ParallelCollectorQueue.ConsumerTests`

Coverage: prefix/FIFO ordering, short/empty inputs, definition validation/copying, quantity clamping, front/type/gate/foreign-handle rejection, held-pair release, co-admission overflow, per-type accounting, stable notifications, capacity back-pressure, dispatch reentrancy/observer failure, large totals, seeded mixed-pair solvability and independent public-API consumption. No scene or PlayMode fixture is needed for these deterministic contracts.

Unity WebGL/IL2CPP player behavior, mobile performance, conveyor/sand integration and UI are separate acceptance work; Editor tests do not prove them.
