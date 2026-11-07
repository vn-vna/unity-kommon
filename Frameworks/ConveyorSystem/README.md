# ConveyorSystem

Modular closed-loop conveyors for Unity **6000.0.72f1**. Namespace:
`Com.Scheherazade.Common.Frameworks.ConveyorSystem`.

## Included v1

- Headless `ConveyorWorld` / `ConveyorRuntime`, stable generation-checked payload handles, bounded ingress reservations, command/result buffers, and versioned read-only frames.
- Provider-selected transport. `LogicalLaneBackend` supplies independent lane motion, slot-sized circular footprints, swept entrance detection and lane-wide holding. External prepare/complete barriers are available for future adapters; no physics solver is included.
- Editor and runtime manufacture from rounded control-point paths or explicitly placed/snapped prefab segments. Both compile to the same immutable arc-length path contract.
- Attached teleport modules with same-loop/cross-loop pairs, explicit lane mappings, Blocking and permanent Vaporizing policies.
- Optional pooled and GPU-instanced payload views, plus a public view-provider seam for game-specific batch rendering.
- Scene owner and optional fixed-step manager, Scene View handles, validated temporary previews, prefab/mesh baking, starter-asset creation, and public-consumer/editor/PlayMode tests.

The plugin has no game, SandEngine, GridSystem, UnityEditor runtime, Unity Splines, Jobs/Burst or third-party SDK dependency. It follows Scheherazade assembly/namespace/configuration conventions. Unlike frameworks which use Common APIs, this implementation does not add an unused Common reference and its SDK dependency closure.

## Boundaries

```text
ConveyorConfiguration
  +-> path + geometry providers -> ConveyorManufacturer -> owned build result
  +-> transport provider       -> one owned backend per ConveyorRuntime
  +-> module factories         -> private per-instance teleport/extension state
  +-> payload-view provider    -> pooled / instanced / custom renderer

ConveyorWorld: identity, conservation, guarded commands and transfer commits
  -> backend prepare / complete -> completed frame -> read-only payload view
```

Configurations and source assets are borrowed, not mutated or destroyed by runtime instances. The world owns payload identity; backend placement records and visual objects are not additional cargo owners. Sand extraction, colors/palettes, targets, failure conditions, boosters and economy remain game-side policy.

## Scene setup

1. Run **Tools → Scheherazade → Conveyor System → Create Starter Assets…**. Select a **new** configuration path in the game's asset folder. It creates sibling definition, path/transport/geometry/view settings, materials and a cargo mesh. Existing assets are never overwritten.
2. Add a **Scheherazade → Conveyor System → Conveyor** component. Assign its definition/configuration and a unique ID. Add the owner to a **Conveyor Manager** component's `conveyors` list.
3. Select the conveyor. Move its numbered Scene View handles, insert/remove control points, validate, and build a temporary geometry preview. Definition assets are shared: edit a duplicate if another conveyor should retain its original route.
4. Use **Bake geometry prefab…** to save a new prefab and persistent copies of any generated meshes. This bakes presentation, not a hidden runtime manager or cargo state.
5. Enter Play Mode. The manager initializes the configured owners and drives their world. Consumers admit payloads through `manager.World` and the owner's `Runtime`, and subscribe to `ResultPublished` before producing cargo.

There is no automatic global manager, scene search, game bootstrap, or per-payload MonoBehaviour requirement. The plugin does not alter build scenes or create game-specific level assets.

### Dockable editor window

Open **Tools → Scheherazade → Conveyor System → Conveyor Editor**, or use the component inspector's **Open Conveyor Editor window** button.

- **Authoring** reuses the component's validated point/segment tools, geometry preview and prefab baking. **Select / frame** selects the owner for its existing Scene View handles. Toggle **Follow selection** off to keep editing one conveyor while inspecting other objects.
- **Assets** edits the actual definition, configuration, providers, modules and overflow policy inline with Unity's normal Undo support. Shared assets affect all their consumers; duplicate an asset first for independent settings. Save assets, then explicitly rebuild the preview to reflect changes.
- **Live test** displays payload/transfer/lane-block diagnostics and inserts a payload by profile, lane and normalized position. Assign the owning **Clock manager** to pause/run or single-step. Asset/geometry editing is disabled in Play Mode; test insertions do not persist.

Window-owned editors and previews are released on target changes, Undo/Redo, play-mode transitions and window close. A prefab asset cannot be used as a live scene target.

### Workspace test scene

The game-side demo is **`Assets/__Project/Labs/Conveyor/ConveyorTest/Scenes/ConveyorTest.unity`**. Open it and enter Play Mode. It has three two-lane pairs: circulating transfers, a full Blocking destination lane, and a full Vaporizing destination. The on-screen controls pause/step, replenish sources, free Blocking lane 0 and reset cargo. The scene includes a camera, main light, persistent edit-mode belt previews and GPU-instanced payloads. Preview geometry hides in Play Mode so it is not rendered twice.

**Create Conveyor Test Scene** in the same Tools menu creates a fresh, uniquely named scene/asset folder under `Assets/__Project/Labs/Conveyor/`; it refuses to replace unsaved open scenes and does not change build settings. This builder and its consumer/HUD live in `Assets/__Project/Labs/Conveyor/Scripts/`, outside the reusable plugin. The demo's destination-fill helper intentionally assumes its supplied 0.5-unit logical transport spacing; after changing that setting, adapt the helper or use the Live test insertion controls instead.

### Dense sand swarm

**`Assets/__Project/Labs/Conveyor/ConveyorSwarm/Scenes/ConveyorSwarm.unity`** demonstrates **250 cells × 16 lanes = 4,000 individually tracked grains**. Create another independent copy with **Tools → Scheherazade → Conveyor System → Create Sand Swarm Scene**. This is a standalone conveyor demo, not a connected falling-sand simulator or a complete reference-game reconstruction.

- `LogicalLaneTransportConfiguration.indexedAdmission` is opt-in. Plain factories append `indexedAdmission: true`; the asset's `Configure` method appends `useIndexedAdmission: true`. Existing calls/defaults retain continuous admission and multi-slot support.
- Indexed admission snaps the requested distance to the nearest moving logical cell using that lane's current phase. Occupancy covers reservations and committed grains; reserve/cancel/remove use direct cell lookup rather than scanning all payload records. Only one-slot profiles are supported; other footprints reject as Unsupported. Maximum allocation is 1,048,576 indexed cells per conveyor.
- Indexed mode bakes evenly spaced path poses once (at least 64 samples, otherwise one per slot). Published poses interpolate those cached samples, including across the seam. This is a sampled-path approximation, not an exact evaluation of the authoring curve. Logical identity, gates, lane holds and transfer accounting remain world/backend responsibilities.
- `PalettePayloadViewConfiguration` maps profile IDs to per-instance colors using one shared grain mesh and one runtime material copy. It reuses 511-element matrix/color arrays and a property block. Use an instancing shader with per-instance `_BaseColor` support, such as the included shader; merely having a scalar color property is insufficient. Seeded pose/scale variation is visual-only and stable across transfers, never new simulation state.
- The game-side `ConveyorSwarmSandbox` holds a bounded **8,192-descriptor producer queue** and attempts at most **16 admissions per fixed tick** through a cached world command. Full admissions leave the head queued; no grain is silently discarded. `QueueGrains` returns how many requests fit. Pending descriptors are not yet world-owned cargo. No per-grain GameObject, Rigidbody, material or coroutine is created.
- The sample speed moves about one cell per fixed tick, so its initial queue fills a 4,000-cell belt over roughly one lap. The HUD can pause the clock or inlet, single-step, queue another 1,000 grains, clear lane 0 and reset. At full occupancy, new requests wait until space is available; the standalone scene has no target collector draining them.
- Admission's **cell lookup** is O(1), not the entire frame. Profile resolution, completed-frame scans, gate work, CPU matrices/rotation and GPU rendering remain real costs. Zero-allocation core tests exclude rendering and HUD strings. Device performance requires a separate profile.

### Reference-style belt presentation

Open **`Assets/__Project/Labs/Conveyor/ConveyorReferenceStyle/Scenes/ConveyorReferenceStyle.unity`**, or create a fresh variant with **Tools → Scheherazade → Conveyor System → Create Reference Style Scene**.

This visual sample uses a flattened capsule, recessed dark deck, rounded white inner/outer rims, lavender surroundings and a soft procedural contact-shadow card. Its orange/yellow/lime/white/dark grains use scattered occupancy and stable size, tilt, height and positional variation rather than uniform colored rows. The live **70%** label starts from **2,800 actual grains / 4,000 cells**, not a hardcoded percentage. **Lab controls** exposes pause/run and reset-to-70%; the standard editor window remains available for detailed editing.

The **V-shaped upstream feed is deliberately excluded**: it belongs to another project system. This sample neither creates a decorative inlet stream nor connects SandEngine or target collectors. Existing lab/swarm scenes and build settings are left unchanged.

`RimmedConveyorGeometryProvider` is a reusable geometry-provider choice, independent of transport. It combines deck and two rounded rims into one owned mesh with three material slots. `ConveyorRimMeshBuilder` supports flat/up-oriented closed paths without reversing cusps; choose path curvature and belt width that keep the inner rim from folding over itself.

Palette views append optional size/height variation settings. Defaults preserve previous rendering; larger variation is presentation-only and does not change occupancy, cargo identity, or collision behavior. Keep grain size/jitter compatible with lane pitch and rim clearance. The sample's scattered-fill seeding, percentage overlay, backdrop and soft-shadow shader stay game-side under `Assets/__Project/Labs/Conveyor/Scripts/`.

### Teleport configuration

Create a **Modules → Teleport** asset and attach it to the **source configuration's** module list. Set `destinationId` to the target conveyor's exact ID, and set normalized entrance/exit positions in `[0,1)`. An empty lane map preserves channel index; otherwise provide one nonnegative destination channel for every source lane. Multiple module assets can define multiple entrances, up to 64 active entrances per logical lane.

- Empty overflow policy means Blocking. Assign a **Policies → Vaporizing** asset for permanent loss on a **valid but full** exit.
- Missing/disabled destination, absent lane, incompatible backend/profile, or overlapping same-loop source/destination footprint holds with a typed diagnostic instead of invoking overflow policy.
- Blocking holds only the affected source lane, preserving spacing. It does not compact cargo into a traffic queue. `GetBlockReason(channel)` and `GetBlockedTicks(channel)` expose diagnostics.
- Arrival is serviced at the beginning of the following fixed tick. All destinations are reserved before any source is freed. Source/port/module order is stable; slots only promised to be freed later in that batch are not reused. Full cyclic pairs can therefore deadlock intentionally.
- Teleportation preserves the same handle and consumer key. Vaporization emits one terminal result. It never means an invisible waiting buffer or an automatic level failure.

## Headless use

```csharp
using Com.Scheherazade.Common.Frameworks.ConveyorSystem;
using UnityEngine;

var definition = new ConveyorDefinitionSnapshot(new[]
{
    new Vector3(-3, 0, -2), new Vector3(3, 0, -2),
    new Vector3(3, 0, 2), new Vector3(-3, 0, 2)
});
ConveyorPath path = new RoundedPolylinePathProvider().Bake(definition, Matrix4x4.identity);
var world = new ConveyorWorld(payloadCapacity: 1024, resultCapacity: 2048);
ConveyorRuntime a = world.CreateConveyor("a", path,
    new LogicalLaneTransportProvider(speed: 1.5, slotSpacing: 0.5), channels: 2,
    modules: new IConveyorModuleFactory[]
    {
        new TeleportModuleFactory("b", 0.2, 0.65, BlockingTransferPolicy.Instance)
    });
ConveyorRuntime b = world.CreateConveyor("b", path,
    new LogicalLaneTransportProvider(), channels: 2);

ConveyorAdmissionStatus status = world.TryInsertAt(a,
    new ConveyorPayloadDescriptor(consumerKey: 123, profileId: 0),
    channel: 0, distance: 1, out ConveyorPayloadHandle handle);

world.Tick(1.0 / 60.0);
ConveyorTransportFrame frame = a.Frame;
// Read frame[index].Pose / Payload here. CopyTo a caller-owned array to retain it.
// Drain/reconcile result records, or DispatchResults to a game-owned observer.
while (world.TryDequeueResult(out ConveyorResult result))
{
    // Pass each identity-rich result to the game's accounting/event adapter.
}
world.Shutdown(ConveyorShutdownDisposition.Discard);
// Terminal shutdown results remain available for reconciliation after disposal.
```

`TryInsertAt` is a path/channel convenience. Neutral backend admission receives a desired `Pose`, channel hint and optional route-distance hint through `ConveyorAdmissionRequest`; slot counts are not part of the universal payload identity. Logical profile IDs resolve to positive footprint lengths using `LogicalPayloadProfile`. Configure every payload profile used by the consumer; unknown or oversized profiles reject as Unsupported.

### Producer-safe ingress

For a producer whose removal is destructive, use `TryReserveIngress` first. The reservation holds world-store capacity, backend spatial capacity, and its insertion-result slot without yet taking payload ownership. Validate/commit the upstream removal, then call `CommitIngress`. If upstream removal fails, call `CancelIngress`.

- Complete or cancel before stepping, structural changes, suspension, or shutdown. Outstanding ingress reservations deliberately block those operations.
- Tokens belong to one world/backend epoch, are single-use, and cannot be committed after cancellation.
- Producers retain ownership on rejection/cancellation. Do not mutate/reenter between the upstream operation and ingress commit.
- Do not hand a mutable backend instance to multiple conveyors; factories must create independent products.

### Results, callbacks and back-pressure

The bounded result queue is part of correctness, not optional logging. Insertion/removal/transfer defers when the operation cannot reserve its required notification space. Game adapters must drain results regularly. `ConveyorManager` drains through `ResultPublished`; manual worlds use `TryDequeueResult` or `DispatchResults`.

Notifications include terminal consumer identity even after a handle becomes stale. `LaneBlocked`/`LaneReleased` are transitions, not retry spam. Callbacks run outside commits and cannot reenter mutations; use bounded `TryEnqueue` for the following tick. Caller-created action closures can allocate; warmed core ticking itself does not create them. If an observer throws, the current result stays queued for explicit retry/reconciliation; previously successful observers may need idempotent handling.

Conservation uses mutually exclusive terminal categories:

```text
Count = AcceptedCount - RemovedCount - DeliveredCount - VaporizedCount
```

Transfers do not enter this equation. `RemovedCount` includes explicit Shutdown removals. Do not count presentation flights as live transported cargo.

## Clock and lifecycle

- A bare world supports manual `Tick`. `ClaimClock(owner)` makes additional drivers fail; the manager claims its world. Pause `manager.Running` before `StepOnce`. Its catch-up budget retains elapsed-time debt rather than silently dropping it.
- A future external backend uses `BeginStep(deltaTime, driver)` → one shared scene simulation → `CompleteStep(driver)`. It must advertise its real capabilities, stage body/contact changes and respect admission guarantees. The synchronous manager rejects external backends. No physics integration or automatic logical↔physics transfer is implied by the interfaces.
- World phases are queued commands → prior gate arrivals → destination reservations → guarded transfer commits → prepare/complete all backends → frame publication. Renderer calls do not advance simulation.
- Borrowed frames expire at the next publication or disposal. `CopyTo` creates a snapshot in caller-owned storage. Explicit ingress/removal does not publish immediately; call `Publish` or complete a tick before rendering those changes.
- Disabling a scene conveyor suspends it without deleting cargo. Runtime structural replacement requires an empty conveyor; provider/build failure preserves the old working instance.
- `Shutdown()` / `Dispose()` default to RequireEmpty. To dispose occupied state, explicitly select Discard and reconcile Shutdown results. Drain existing notifications first if capacity is needed for terminal records.
- Engine destruction cannot be vetoed: scene-owner and manager `OnDestroy` use declared Discard semantics and produce terminal records. Call `Shutdown(RequireEmpty)` **before** destroying an owner if the game needs to enforce draining instead. Lifecycle operations requested inside world callbacks use the bounded next-tick queue; do not destroy owners while retaining uncommitted ingress reservations.
- Trusted provider/module failures stop further world mutation (`IsFaulted`) rather than pretending partial work succeeded. Fix the extension and reconcile/reconstruct the world; this is not a general rollback guarantee for arbitrary engine/body operations. Read queries and policies must not mutate or reenter the world.

## Manufacturing and authoring limits

The default point provider authors a finite, non-self-intersecting local XZ loop with 3–256 points, rounded corners and a supplied up direction. A custom path provider may replace it. Arc length is baked once and reused; transport spacing is separate from geometry depth/mesh representation. Width-related self-overlap between distant portions of a path is an authoring constraint, not collision simulation.

Prefab construction requires a segment-mode definition with explicit segment assets and placement poses. The segment centerline's first/last edges define connector direction. **Snap segment chain and validate closure** positions successive pieces through those connectors. Joins must close within **5 mm / 2°**, declared widths must match, and prefab manufacture requires unit world scale. A finite prefab library cannot represent arbitrary authored curves; unsupported combinations fail instead of silently approximating. Source prefabs should be geometry-only; their scripts are not a supported simulation bootstrap.

The default mesh builds an owned closed ribbon/deck with UVs and independent generated resources. Body material assets and prefab/mesh inputs remain borrowed. Temporary editor previews clean up on inspector disable; persistent bake operations create new assets only and clean up their own partial outputs on failure.

## Rendering customization

Implement `IConveyorPayloadViewProvider.Create(in ConveyorViewContext)` and return an instance-owned `IConveyorPayloadView`. `Render(in ConveyorTransportFrame)` receives completed handle/profile/pose data; consume it during the call or copy explicitly. No separate rendering module is needed.

- `InstancedPayloadViewConfiguration`: profile ID → mesh, borrowed material, scale, tint and height offset. Runtime material copies enable instancing without changing source assets. Reusable **511-matrix** batches use `Graphics.RenderMeshInstanced`, compatible with shaders carrying both transform matrices. `ConveyorColor.shader` has separate URP (`SRPDefaultUnlit`) and Built-in (`Always`) passes; there is no per-grain Renderer or GameObject.
- `PooledPayloadViewConfiguration`: profile ID → visual-only prefab and scale. Objects are rented/released by handle generation and reused. Prefabs with a Rigidbody reject; authoritative physics belongs to transport. Pool warmup/first visual creation can allocate.
- Mesh dimensions/visual scale and logical footprints must be configured consistently by the consumer. Render providers do not change admission, delivery or ownership. Custom per-instance palette buffers, cross-conveyor aggregation, alternate culling or platform fallbacks belong behind this seam.
- GPU instancing availability is checked. Select a pooled/custom provider on unsupported devices; there is no silent fallback or platform-equivalence claim. Batch rendering does not remove the cost of future physical bodies/collisions.

## Assemblies and verification

| Assembly | References |
| --- | --- |
| `Com.Scheherazade.ConveyorSystem` | Unity engine only; no Common SDK closure or game reference. |
| `Com.Scheherazade.ConveyorSystem.Editor` | Runtime + UnityEditor. |
| `Com.Scheherazade.ConveyorSystem.ConsumerTests` | Runtime + TestAssemblies; public API only, no friend access. |
| `Com.Scheherazade.ConveyorSystem.EditorTests` | Runtime + Editor + TestAssemblies. |
| `Com.Scheherazade.ConveyorSystem.PlayModeTests` | Runtime + TestAssemblies. |

Run both EditMode assemblies and the PlayMode assembly in Unity Test Runner. In this workspace, `powershell -File artifacts/conveyor-system/verify.ps1 -Mode All` also compiles against the actual editor assemblies and runs copies in an isolated Unity 6000.0.72f1 / URP 17.0.4 project, without closing or changing the user's open scenes. Artifacts/logs/results stay under `artifacts/conveyor-system/verification/`.

Coverage includes conservation/randomized operations, stale/foreign tokens and handles, seam-spanning footprints, reservations/back-pressure, callbacks and command bounds, exclusive clocks/external barriers, module/provider isolation, gate crossings, both overflow policies, same/cross-loop transfers, contention/cyclic blocking, invalid endpoints/profiles, manufacturing rollback, connector compatibility, asset persistence, scene disable/destroy and actual GPU-instanced pixels across the 511-instance boundary.

The warmed **headless** core allocation test measures steady logical ticks/publication only. Editor/GPU/URP smoke tests do not establish Android performance, WebGL/playable exports, player builds, physics, or equivalence to the reference APK. Profile admission scans, view costs and actual device loads before setting performance budgets. Logical advancement uses shared lane phase; default-mode overlap admission and publication scan allocated payload records. Indexed mode replaces the overlap scan with direct cell lookup; publication and gate processing still have their documented scan costs.

## Workspace change log and next-step design

The workspace's `artifacts/conveyor-system/README.md` indexes the implementation change log, verification evidence and handoff. `artifacts/conveyor-system/PACKED_SWARM_NEXT_STEP.md` records the agreed packed per-grain description/empty-slot direction. That new packed store is **not implemented**; current preallocated transport storage and profile-based palette rendering remain in use.

## Explicit follow-up scope

Production physics adapters, automatic mixed-backend handoffs, branching/merging/routing, compressed traffic queues, occupied-route/backend migration, game-specific sand/target integrations, and device/player acceptance remain separate work. They are not hidden behavior of this v1.
