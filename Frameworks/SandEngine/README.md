# SandEngine

Reusable Unity sand simulation. Namespace:
`Com.Scheherazade.Common.Frameworks.SandEngine` (array kernels/backend in `.Pla`).

## Assemblies and dependencies

| Assembly | Direct project/package references |
| --- | --- |
| `Com.Scheherazade.SandEngine` | `Com.Scheherazade.Common`, `Com.Scheherazade.GridSystem`, `Unity.Collections`, `Unity.Burst`; Unity engine APIs including Jobs, profiling and compute/readback |
| `Com.Scheherazade.SandEngine.Editor` | SandEngine; UnityEditor (generator only) |
| `Com.Scheherazade.SandEngine.EditorTests` | SandEngine, SandEngine.Editor, GridSystem, Collections, Burst and Unity TestAssemblies |
| `Com.Scheherazade.SandEngine.ConsumerTests` | SandEngine, Common (Luna rectangle compatibility), GridSystem, Collections and Unity TestAssemblies |

There is no production game assembly reference. Common's own service/SDK
dependency closure is retained; this is reuse inside Scheherazade, not a
Unity-free or standalone UPM distribution. No local version defines are needed:
`PLA_CREATIVE_BUILD` / `UNITY_LUNA` remain consumer build configuration symbols.

## Ownership and supported API

- Implement `ISandEngineSettings` with a plain object or asset, construct an
  `ISandBackend` and `SandEngine`, and dispose the backend in the caller's owner.
  Settings are borrowed/live. A scheduled tick receives a `StepParams` snapshot.
- `SandBackendFactory.Create(ISandBackendSettings, width, height)` retains Auto,
  Jobs, GPU/fallback and native WebGL SingleThread selection. Creative builds
  select `Pla.SandPlaBackend`. Regional/persistent gravity can use CPU solving
  even when GPU is selected.
- Packed `SandCell`, `SandViewCell`, `ElementType`, `StepParams`, backend,
  scheduling and view contracts retain their existing layouts and semantics.
- `SandCell.PackWall(frictionless: true, payload: colorByte)` marks a zero-friction
  wall; `IsFrictionlessWall` reads the material. Type remains Wall (2); bit 8 in
  the otherwise unused wall velocity byte holds the flag. Sand velocity/color
  encoding is unchanged. These walls bypass slide friction/edge-vacancy guards
  and allow speed-bounded travel across clear rasterized ledges toward a downhill
  exit. The empty path may pass above sand support. A transient Sand payload bit31
  remembers a wall-started lateral slide across multiple bounded ticks; ordinary
  gravity-axis moves clear it. This prevents sand support from aborting a partly
  completed slide. Color/variant, velocity, multiplier and gravity bits are separate.
  They never permit tunneling through occupied cells or draining a closed floor.
  A consumer must still accept grains at the bottom to prevent backpressure.
- Optionally implement `ISandSettlingSettings` and return false from
  `EnableImmediateSettling` to disable per-tick instant settling and collection
  vacancy compaction. Ordinary gravity/slide motion remains speed-bounded;
  explicit batch collapse is independent. Settings without this interface keep
  legacy immediate settling. Direct backend callers still control
  `StepParams.EnableVerticalSettle` themselves.
- Seed/edit using `WriteCells`, `WriteCellsWithVariantVariation`, `DrawStamp`,
  `TryEmitSand`, `Clear`, `Resize`, and backend `SetCell`. Authoritative backend
  mutations preserve counts and view dirtiness. Do not write borrowed arrays.
- Collection supports discs and `SandCollectionRegion` footprints, scan-cursor,
  nearest and randomized selection. `SandRemovalSampleBuffer` is bounded reservoir
  sampling with caller-owned capacity and seed; `Consider` retains removal order
  and random progression.
- `CommitReservedRemovals` accepts unique engine-local `(index, packedCell)`
  snapshots, validates the entire batch before mutation/sample RNG, removes in
  caller order and performs vacancy repair once. Complete scheduled work before
  selecting. Do not mutate/reenter between selection and commit. Stale, duplicate,
  non-sand or out-of-bounds reservations throw before mutation. External credits
  and history must be committed by the caller after success.
- `RegisterGravityRegion`, `SandGravityRegion.Update/Dispose`,
  `GetGravityDirection`, `SandGravityDirection`, and the visual revision/occluder
  query provide explicit gravity registration and read contracts. Dispose regions
  before resizing their engine.
- `ScheduleStep` / `CompleteStep` preserve two-phase execution. Schedule all
  independent engines before completing all; observers/publication follow the
  completion barrier. Never retain borrowed buffers across a scheduled tick.
- `ISandViewBackend.RefreshViewBuffer` suppresses unchanged visible uploads;
  initial and resized views publish. The contract assumes one consuming view.
  GPU publication can lag while asynchronous readback is pending.
  `SandViewExtraction.Extract` is the fallback for custom backends.
- Derived policy adapters can use the protected bounded candidate heap/comparer
  and scan marker. Mutation/repair internals and native/generated solver types
  remain internal. Only the shared diagnostic tests and existing game engine
  integration test assembly are friends; production game code is not a friend.

The game retains its `Com.PMA.SandSim.SandEngine` subclass with the borrowed
`Config` facade and Pit selection/budget/history policy. Pool accounting, covered
wall sand, pipe reserves, clocks, level gates, palette authoring and rendering
remain caller responsibilities. The framework has no Pit, Entt, palette or game
configuration dependency.

## Source and asset maintenance

`Runtime/` owns the orchestrator/contracts, native jobs, SingleThread/GPU
backends, collapse/repair/gravity, and `SandPlaBackend/Generated/`.
`Runtime/Shaders/SandSimulation.compute` belongs to the same maintenance unit.
`Editor/Generation/SandPlaKernelGenerator.cs` specializes authoritative native
sources; both source and output roots point into this framework. Run
`SandPlaKernelGenerator.ValidateCurrent()` after import and before export.
Never maintain a divergent generated physics algorithm.

Extraction moved existing files and their `.meta` files byte-for-byte. The
compute GUID remains `fe6d791651734544b940bd045e99c65e`. Game SandSimulator stays
in the game at its existing script GUID `1f815cf6807336d46b2a85453ca63543`, with
its serialized fields/defaults/copy behavior. Production simulator assets,
palettes, LUT shader, scenes and prefabs stay in the game. Enum numeric values
and cell bit layouts are unchanged; no scene resave is part of migration.
The moved engine/backend objects are transient C# objects, not serialized
MonoBehaviour or ScriptableObject instances. Consumer code imports the final
namespace; persistent game asset fields continue to use the same game asset.

## Verification after import

Run the shared `Com.Scheherazade.SandEngine.EditorTests`, the independent
`Com.Scheherazade.SandEngine.ConsumerTests`, and the game's
`Com.PMA.SandSim.Engine.Tests` together. Include adjacent HammerWall,
InfinitySand, CreativeControl, Playable and LevelEditor checks for changed
consumer references. The consumer assembly deliberately has neither a game
reference nor friend access and exercises actual public settings, stepping,
gravity, publication and atomic removal/repair.

Native Editor checks do not establish PLA/Luna export, WebGL player, GPU or
device acceptance. Verify creative compilation and actual Luna export/smoke,
WebGL backend selection/build, real simulator/scene asset reload, and isolated
GPU conservation/publication/disposal separately. Keep existing gameplay
schedule-all/complete-all and observer/accounting regression coverage.
