# D3D12 Composition Spike v0

> Narrow implementation contract for the first composition/GPU-offload code path. This is not a generic compositor design; it validates a D3D12-backed layer update path for translation and opacity.

## Goal

Prove the first composition IR and compositor-owned animation tick can be handed to the active D3D12 backend without rebuilding UI/runtime state, layout, or draw commands per animation tick.

The current spike owns:

- One composition layer referencing a contiguous draw-command range.
- Layer translation in logical pixels.
- Layer opacity in normalized `[0, 1]`.
- `CompositionAnimationPlan` data in `Irix.Rendering`.
- `CompositionAnimationDeclaration` data that targets stable retained `NodeKey` values and resolves through the retained input snapshot.
- `DrawingBackendCompositor.RenderCompositionAnimationTickAsync`, which advances the animation over the retained frame.
- Composition-aware hit testing for transform/opacity layers by inverse-mapping input through the active presented transform.
- Typed composition clock values: `CompositionTimestamp` and `CompositionDuration` carry `Stopwatch.GetTimestamp()` ticks and keep frame indexes out of animation progress.
- D3D12 backend consumption through `ICompositionDrawingBackend.ExecuteComposition`.
- A PoC demo that renders static draw commands once, then updates only compositor-owned transform/opacity per frame.
- Stable machine-readable diagnostics.

## Non-Goals

- No public composition API.
- No scroll presentation model.
- No scroll hit-test coordinate remapping.
- No retained layer cache or intermediate render target.
- No Vulkan/Metal work.
- No replacement of normal UI frame publication. `ICompositor.RenderAsync` remains the content-update path; compositor ticks are a separate retained-frame presentation path.

## IR Contract

`CompositionFrame` is platform-neutral and lives above D3D12:

| Type | Role |
|------|------|
| `CompositionLayerId` | Stable layer identity for diagnostics and future retained mapping. |
| `CompositionTransform` | Translation-only v0 transform. |
| `CompositionOpacity` | Strong normalized opacity value. |
| `CompositionLayer` | Layer id, command range, transform, and opacity. |
| `CompositionFrame` | Single-layer v0 frame wrapper. |
| `CompositionAnimationDeclaration` | Runtime-facing internal descriptor keyed by retained `NodeKey`, with transform/opacity timeline data and no command-range knowledge. |
| `CompositionAnimationPlan` | Resolved data-driven transform/opacity animation descriptor for the layer command range. |
| `CompositionTarget` | Internal retained UI target resolved by `RenderPipelineRetainedInputSnapshot`, keyed by `NodeKey` and mapped to a command range plus `CompositionLayerId`. |

The layer references existing `RenderFrameBatch` command ranges; it does not copy commands or own frame resources.

## D3D12 Execution

The D3D12 spike materializes translation and opacity at backend execution time:

```text
RenderFrameBatch commands/resources
  -> DrawingBackendCompositor retained frame
  + CompositionAnimationDeclaration resolved to CompositionAnimationPlan
  -> CompositionFrame single layer
  -> ICompositionDrawingBackend.ExecuteComposition
  -> transformed rect/text payloads
  -> existing D3D12 rectangle/GlyphAtlas passes
  -> Present
```

This is intentionally D3D12-backed. Non-composition backends do not receive a CPU compatibility implementation for this tick path; they fail fast until a written blocker justifies a secondary path.

## Diagnostics

`--diagnose-composition-transform` must prove:

- `finalComposition=D3D12`
- `d3d12Backed=True`
- one layer was consumed
- command range accounting is stable
- translated command count is nonzero
- opacity-applied command count is nonzero
- no layout rebuild or draw-command regeneration is required inside the backend path

`--composition-demo [durationMs]` is the visible PoC sample. It builds normal retained UI output once, installs a `CompositionAnimationDeclaration` targeting a retained `NodeKey`, resolves it to a `CompositionAnimationPlan`, then calls compositor-only ticks for transform/opacity presentation on the D3D12 execution path until the wall-clock duration expires. The animation clock is `Stopwatch`; the internal renderer boundary is typed as `CompositionTimestamp`/`CompositionDuration`, so display refresh changes tick density but not movement speed. The machine line includes `renderCount=1`, `demoDurationMs=<durationMs>`, and `compositionTicks=<actualTicks>` to prove UI frame publication is not driving each animation frame.

## Next Gate

Normal UI output snapshots resolve retained `CompositionTarget` values, runtime-owned animation declarations resolve `NodeKey` targets into `CompositionAnimationPlan` instances, and transform/opacity hit testing maps pointer coordinates through the active presented layer transform. The next gate is scroll presentation: it needs a scroll-specific clip/target/commit contract before presented scroll offset uses the same retained composition spine.
