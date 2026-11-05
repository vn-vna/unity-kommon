# Sand PLA Backend

The playable-ad backend lives here. `SandBackendFactory` selects `Pla.SandPlaBackend`
when `PLA_CREATIVE_BUILD` is enabled. Other builds retain the native Single Thread,
Jobs/Burst and GPU backends. Native WebGL fallback is still Single Thread.

```text
SandEngine / SandPuppeteer
           |
           v
     ISandBackend
           |
           +-- native: SingleThreadBackend / JobBackend / GpuBackend
           |
           +-- playable: SandPlaBackend
                              |
                              +-- SandPlaBuffer<T> (reference ownership)
                              +-- SandPlaStep (direct int[] physics)
                              +-- SandPlaRegionalStep (direct regional physics)
                              +-- SandPlaAlgorithmCore (shared rule specialization)
```

## Responsibilities

- **`SandPlaBackend.cs`**: synchronous stepping, buffer swaps, persistent-gravity
  counts, resizing, disposal, view publication and timing. Its scheduling contract
  executes immediately; completion is a no-op. It uses no fake jobs or Burst API.
- **`SandPlaBuffer.cs`**: managed-array ownership. Copies of an owner reference see
  the same writes/disposal. `ToArray()` copies; `GetArrayForStep()` borrows without
  copying. Borrows must not outlive a swap, resize or disposal.
- **`Generated/`**: direct-array specializations of the authoritative native solver.
  These are generated source, not separately maintained physics implementations.
- **`SandPlaBatchCollapse`**: generated surface relaxation used by the shared engine
  in PLA builds; mutations still go through the backend to maintain counts/versions.

`SandStepSettings`, `StepParams`, `SandCell` and the gameplay configuration remain
shared. The 32-bit cell-mask corrections remain shared because their results are
identical on native and PLA. There are no PLA/native parameter branches inside
the authoritative native solver files.

## Editing physics

Edit the original native C# sources:

| Native source | Generated PLA specialization |
| --- | --- |
| `SandAlgorithmCore.cs` | `SandPlaAlgorithmCore.g.cs` |
| `SandResolutionJobs.cs` / `SingleThreadSandStepJob` | `SandPlaStep.g.cs` |
| `RegionalSandStepJob.cs` | `SandPlaRegionalStep.g.cs` |
| `SandBatchCollapse.cs` | `SandPlaBatchCollapse.g.cs` |

`Editor/SandSim/SandPlaKernelGenerator.cs` automatically refreshes the generated
files after these sources are imported and when the Editor loads. It substitutes
storage types and class names and removes job annotations/profiler scopes; it
does not rewrite movement rules. Each output records the normalized source hash.
Unexpected native dependencies fail generation rather than silently retaining a
slow/unsupported path. No additional package or external generator is required.

Manual commands:

- **Tools > SandSim > Sand PLA Backend > Regenerate Kernels**
- **Tools > SandSim > Sand PLA Backend > Validate Kernels**

Do not hand-edit `Generated/`. Existing generated file GUIDs are retained when
content changes. Native player builds exclude the PLA module; native Editor builds
include the pure array step kernels for native-versus-PLA parity tests. The collapse
specialization uses the PLA backend contract and is compiled only in PLA mode.

## Compatibility boundaries

`CreativeCompatibility/ManagedSandExecution.cs` remains only for presentation's
retained synchronous fill-job API. Its `NativeArray<T>` name is a thin adapter over
`SandPlaBuffer<T>`; it no longer owns simulation storage or dispatches simulation.
The backend contract and simulation use the explicit `SandPlaBuffer<T>` type.
`SandViewExtraction` isolates fallback view extraction from the rendering callers.

Existing behavior fixtures run against the selected backend in each configuration.
Their `JobBackend`/`SingleThreadBackend` aliases in PLA tests mean the PLA backend,
not native worker scheduling. Independent native/PLA kernel comparison tests run
in the normal Editor configuration, across all cardinal directions, multiple
mixing settings, mutations and regional gravity. Kernel freshness is tested in
both configurations. Golden cavity/collection/pipe trajectories remain required.

The backend extraction changes neither simulation rate nor mixing quality. Verify
an exported playable after changes: native Editor speed cannot establish Luna's
generated-code performance.
