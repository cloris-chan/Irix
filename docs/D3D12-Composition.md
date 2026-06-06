# D3D12 Composition

> Narrow implementation contract for the current composition/GPU-offload code path. This is not a generic compositor design; it validates D3D12-backed layer updates for translation, opacity, fixed-clip scroll presentation, and multi-layer composition frames.

## Goal

Prove the first composition IR and compositor-owned animation tick can be handed to the active D3D12 backend without rebuilding UI/runtime state, layout, or draw commands per animation tick.

The current implementation owns:

- Composition layers referencing retained draw-command ranges.
- Layer translation in logical pixels.
- Layer opacity in normalized `[0, 1]`.
- Fixed layer clip mode for retained scroll targets, with nested/mixed child clips decomposed into ordered layers.
- Multi-layer `CompositionFrame` publication with ordered `CompositionLayer` application on the D3D12 execution path.
- `CompositionAnimationPlan` data in `Irix.Rendering`.
- `CompositionAnimationDeclaration` data that targets stable retained `NodeKey` values and resolves through the retained input snapshot.
- `CompositionScrollPresentationDeclaration` data that targets a retained scroll container `NodeKey` and resolves to a fixed-clip `CompositionScrollPresentationPlan`.
- `DrawingBackendCompositor.RenderCompositionAnimationTickAsync`, which advances the animation over the retained frame.
- `DrawingBackendCompositor.RenderCompositionScrollPresentationTickAsync`, which advances presented scroll offset over the retained frame.
- `CompositorHitTestSnapshot` publication for transform/opacity and fixed-clip scroll layers, with inverse-mapped local coordinates, fixed-clip rejection before local bounds, nested layer remapping, and reverse paint-order hit selection.
- Typed composition clock values: `CompositionTimestamp` and `CompositionDuration` carry `Stopwatch.GetTimestamp()` ticks and keep frame indexes out of animation progress.
- Animation marker events for transform/opacity and fixed-clip scroll presentation. Markers are data on the declaration, evaluated by timeline interval crossing after a successful compositor tick, and drained through a runtime-facing pump that maps typed runtime event ids to app messages outside the backend.
- A D3D12 layer content cache for disjoint composition layers. The cache is keyed by stable layer id, command range, source command hash, resource resolver identity/frame identity, and display scale; it reuses materialized source payloads across compositor ticks while transform/opacity/fixed-clip state is applied from the current tick. Display-scale changes, resource resolver changes, same-resolver resource frame resets, source/range/layer-id changes, explicit cache clears, and device recovery force a rebuild of scale-, resource-, source-, or device-dependent payloads.
- D3D12 backend consumption through `ICompositionDrawingBackend.ExecuteComposition`.
- A PoC demo that renders static draw commands once, then updates only compositor-owned transform/opacity per frame.
- A `--diagnose-composition-scroll` diagnostic that exercises D3D12 fixed-clip scroll presentation.
- A `--diagnose-composition-multilayer` diagnostic that exercises two D3D12 composition layers in one frame.
- A `--diagnose-composition-layer-cache` diagnostic that proves layer cache miss/hit behavior across two compositor ticks with the same retained source commands.
- A `--diagnose-composition-skip` diagnostic that makes unsupported compositor producer paths explicit.
- A `--diagnose-scroll-presentation-runtime` diagnostic that exposes initial retarget, chained retarget, and typed cancellation matrix state.
- A `--diagnose-scroll-presentation-hittest` diagnostic that routes fixed pointer input through active fixed-clip scroll presentation, dispatches hover and press refresh while presentation remains active, and proves release action routing.
- A `--diagnose-scroll-presentation-interaction` diagnostic that runs the real Counter app/coordinator/compositor/backend path for active pointer interaction, chained wheel retarget, `EnsureRunning` rapid-wheel coalescing, clamped top/bottom boundary wheel input, and active-presentation cancellation from resize, DPI, and max-scroll lifecycle changes.
- Stable machine-readable diagnostics.

## Non-Goals

- No public composition API.
- No generalized composition tree extraction.
- No active internal offscreen/render-target cache. Fixed-clip scroll remains D3D12-composited through the direct/layer-content path; content-space offscreen surfaces need a separate bounds/origin/clip design and a proven direct-composition need before consideration.
- No generic fallback compositor for unsupported producer paths.
- No Vulkan/Metal work.
- No replacement of normal UI frame publication. `ICompositor.RenderAsync` remains the content-update path; compositor ticks are a separate retained-frame presentation path.

## IR Contract

`CompositionFrame` is platform-neutral and lives above D3D12:

| Type | Role |
|------|------|
| `CompositionLayerId` | Stable layer identity for diagnostics and future retained mapping. |
| `CompositionTransform` | Translation transform. |
| `CompositionOpacity` | Strong normalized opacity value. |
| `CompositionLayer` | Layer id, command range, transform, opacity, and clip mode. |
| `CompositionFrame` | Ordered multi-layer frame wrapper; single-layer frames remain allocation-free. |
| `CompositionAnimationDeclaration` | Runtime-facing internal descriptor keyed by retained `NodeKey`, with transform/opacity timeline data and no command-range knowledge. |
| `CompositionAnimationPlan` | Resolved data-driven transform/opacity animation descriptor for the layer command range. |
| `CompositionScrollPresentationDeclaration` | Runtime-facing internal descriptor keyed by retained scroll container `NodeKey`, with presented scroll timeline data and no command-range knowledge. |
| `CompositionScrollPresentationPlan` | Resolved data-driven fixed-clip scroll presentation descriptor for the retained scroll content command range. |
| `CompositionAnimationMarker` | Runtime-declared marker with progress, elapsed-time, progress-range, or every-tick trigger data. |
| `CompositionAnimationMarkerEvent` | Compositor-produced event carrying instance id, marker id, runtime event id, target, timestamp, progress, iteration, and direction. |
| `CompositionTarget` | Internal retained UI target resolved by `RenderPipelineRetainedInputSnapshot`, keyed by `NodeKey` and mapped to a command range plus `CompositionLayerId`. |
| `ScrollCompositionTarget` | Internal retained scroll target resolved by `RenderPipelineRetainedInputSnapshot`, keyed by scroll container `NodeKey` and mapped to command range, fixed clip, retained scroll position, and max scroll. |
| `CompositorHitTestSnapshot` | Input-facing snapshot of retained hit targets plus active compositor layer nodes, used to inverse-map pointer coordinates without UI runtime layout. |

The layer references existing `RenderFrameBatch` command ranges; it does not copy commands or own frame resources.

## D3D12 Execution

The D3D12 path materializes translation and opacity at backend execution time:

```text
RenderFrameBatch commands/resources
  -> DrawingBackendCompositor retained frame
  + CompositionAnimationDeclaration resolved to CompositionAnimationPlan
  -> CompositionFrame ordered layer set
  -> ICompositionDrawingBackend.ExecuteComposition
  -> transformed rect/text payloads
  -> existing D3D12 rectangle/GlyphAtlas passes
  -> Present
```

For scroll presentation, the retained draw output already contains content at the committed logical scroll position. The compositor applies `retainedScrollY - presentedScrollY` as a content transform while keeping the layer clip fixed. This preserves the viewport clip while allowing content to move without layout/draw rebuild.

This is intentionally D3D12-backed. Non-composition backends do not receive a CPU compatibility implementation for this tick path; they fail fast until a written blocker justifies a secondary path.

Marker delivery is intentionally above the backend. `DrawingBackendCompositor` evaluates markers only after `ICompositionDrawingBackend.ExecuteComposition` returns successfully, records typed `CompositionAnimationMarkerEvent` values, and leaves message mapping to the UI runtime. `CompositionMarkerEventPump` drains queued events into an app-owned mapper and the Poc runtime path dispatches mapped messages through the app runtime dispatch sink. Device-lost/recovered skipped ticks do not publish marker events because presentation did not commit, and clearing an active scroll presentation discards pending scroll marker events with the canceled presentation state.

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

`--diagnose-composition-scroll` must prove:

- `finalComposition=D3D12`
- `d3d12Backed=True`
- one fixed-clip layer was consumed
- translated command count is nonzero
- opacity-applied command count is zero for scroll presentation
- fixed clip remains in presentation space while content rect/text positions move

`--diagnose-composition-multilayer` must prove:

- `finalComposition=D3D12`
- `d3d12Backed=True`
- two layers were consumed in one composition frame
- a command can receive more than one layer application
- fixed clip layer count is diagnostic-visible

`--diagnose-composition-layer-cache` must prove:

- first tick misses the layer cache and materializes the retained layer content
- second tick hits the layer cache while applying different transform/opacity values
- display-scale changes miss the layer cache instead of reusing text payloads shaped for the previous scale
- resource resolver changes miss the layer cache even when command handles and source command values compare equal
- same resource resolver resets miss the layer cache even when source command values compare equal
- source command changes miss the layer cache even when layer id and command range are stable
- layer id changes miss the layer cache even when command range and source commands are stable
- command range changes miss the layer cache even when layer id and source command array are stable
- explicitly cleared layer cache state misses on the next compositor execution
- disjoint multi-layer frames can hit independently cached layer payloads
- overlapping layer command ranges bypass the layer cache and use direct composition so stacked transforms/opacity still compose in order
- fixed-clip cache hits apply the latest tick's transform and fixed clip rather than stale warm-tick state
- fixed-clip cache hits skip rect/text payloads when the latest layer clip intersects retained source clips to empty
- layer cache hits preserve source paint order when cached layers are interleaved with ordinary commands
- cached command count is diagnostic-visible
- translated and opacity-applied command counts remain stable

`--diagnose-composition-marker-runtime` must prove:

- marker queue drain count
- dispatched runtime message count
- unmapped marker count from unknown runtime event ids in the same drain path
- final runtime model state after dispatch
- compositor execution count for the marker-producing ticks

`--diagnose-composition-skip` must prove:

- transform/opacity tick skip when no compositor-only plan is active
- transform/opacity tick skip when the backend does not implement composition
- transform/opacity tick skip when no retained frame exists
- transform/opacity tick skip when the active plan no longer fits the retained frame
- fixed-clip scroll tick skip when no scroll presentation plan is active
- fixed-clip scroll tick skip when the backend lacks `ScrollPresentation`
- fixed-clip scroll tick skip when no retained frame exists
- fixed-clip scroll tick skip when the active plan no longer fits the retained frame
- retained-update scroll presentation skip when no scroll presentation plan is active before falling back to normal retained-frame render
- retained-update scroll presentation skip when the backend lacks `ScrollPresentation` before falling back to normal retained-frame render
- device-lost recovery records an explicit skipped compositor execution
- successful compositor execution clears the skip reason to `None`
- skip state, required/backend capabilities, pacing, layer count, and command count are machine-readable

`--diagnose-scroll-presentation-runtime` must prove:

- main-app wheel input can install a retained scroll presentation and advance it with compositor-only ticks
- chained wheel retarget records the current retained-frame invalidation behavior instead of hiding replacement cancellation
- cancel count, last cancel reason, and last render invalidation kind are machine-readable
- explicit, viewport-invalidation, and max-scroll-invalidation cancellation counts are separately visible when diagnostics are enabled
- render-invalidation cancellation clears active presentation before the retained frame render path observes it
- explicit cancellation, render-invalidation cancellation, and dispose release any existing scroll-presentation idle waiters
- render invalidation after an explicit cancellation does not record a second lifecycle cancellation
- stale queued delayed ticks after lifecycle cancellation are skipped without advancing composition or loop tick counts

`--diagnose-scroll-presentation-hittest` must prove:

- fixed pointer coordinates can map to a different action while presented scroll moves retained content under the pointer
- runtime hover mapping uses the active `CompositorHitTestSnapshot` through `DrawingBackendCompositorInputHitTestService`
- hover refresh during active presentation re-renders through composition execution, not a normal retained-frame `Execute`
- the active presented scroll value and hit-test mapping survive the hover refresh
- press without an intervening pointer move refreshes stale hover state from the active presentation snapshot, captures/focuses the presented target, and keeps the press refresh on composition execution
- release routes the captured action through `RoutedInput`

`--diagnose-scroll-presentation-interaction` must prove:

- the real Counter app path starts scroll presentation only through `ScrollPresentationCoordinator.AddPendingPixels` plus `RunUntilIdleAsync`
- fixed pointer hover/press/release during active presentation use `Program.TryMapInputForRuntime`, `InputOwnershipState`, and `DrawingBackendCompositorInputHitTestService`
- hover and press style refresh keep the active presentation on composition execution while normal retained `Execute` does not increase
- release routes the captured presented target through `RoutedInput`
- chained wheel input accumulates target distance through coordinator retargeting
- rapid wheel input through the `EnsureRunning` main-app pattern preserves total wheel distance while the coordinator loop is already running
- rapid top/bottom boundary wheel input clamps to the boundary without starting repeated same-target presentation segments
- resize, DPI, and max-scroll lifecycle changes clear active presentation through the main-app cancellation path and leave no active presented-scroll state behind
- lifecycle scenarios report `staleQueuedTickSuppressed=True` after the stale delayed tick window
- compositor-thread sample-and-cancel without an active presentation returns an empty sample without recording cancellation
- compositor-thread sample-and-cancel cancellation completes any existing scroll-presentation idle waiter

## Current Hardening Boundary

Normal UI output snapshots resolve retained `CompositionTarget` and `ScrollCompositionTarget` values. Runtime-owned transform declarations resolve `NodeKey` targets into `CompositionAnimationPlan` instances; runtime-owned scroll presentation declarations resolve scroll container `NodeKey` targets into fixed-clip `CompositionScrollPresentationPlan` instances.

Active hit testing reads `CompositorHitTestSnapshot`, so pointer coordinates are mapped through current presented transforms and fixed clips without a UI runtime layout pass. Covered behavior includes target-layer-only remapping, fixed-clip rejection before local bounds, clipped presented content not occluding lower static targets, and nested composition layer inverse mapping.

Layer content caching remains source-content caching, not presentation-state caching. Cached payloads are reused only for stable disjoint layer sources, while current tick transform, opacity, fixed clip, empty intersections, text clips, and source paint order are resolved during each composition execution.

Current narrow coverage pins skipped/failed tick marker non-publication, marker runtime mapping/unmapped accounting, scroll marker queue cleanup on cancellation, empty sample-and-cancel without cancellation, stale queued lifecycle tick suppression, superseded/sample-and-cancel/lifecycle scroll-presentation idle waiter completion, invalidation-after-explicit-cancel non-double-counting, and active hit-test routing through sibling and nested layers.

The next gate is broader main-app integration hardening around the existing D3D12-first path: cancellation/retarget edge cases, marker publication edge cases, and active hit-test routing regressions as concrete bugs appear. It is not a generic fallback compositor or an internal offscreen/render-target cache.
