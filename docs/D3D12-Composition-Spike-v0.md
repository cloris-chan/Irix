# D3D12 Composition Spike v0

> Narrow implementation contract for the first composition/GPU-offload code path. This is not a generic compositor design; it validates D3D12-backed layer updates for translation, opacity, fixed-clip scroll presentation, and the first multi-layer composition frame.

## Goal

Prove the first composition IR and compositor-owned animation tick can be handed to the active D3D12 backend without rebuilding UI/runtime state, layout, or draw commands per animation tick.

The current spike owns:

- One composition layer referencing a contiguous draw-command range.
- Layer translation in logical pixels.
- Layer opacity in normalized `[0, 1]`.
- Fixed layer clip mode for single scroll containers whose retained children share one scroll clip.
- Multi-layer `CompositionFrame` publication with ordered `CompositionLayer` application on the D3D12 execution path.
- `CompositionAnimationPlan` data in `Irix.Rendering`.
- `CompositionAnimationDeclaration` data that targets stable retained `NodeKey` values and resolves through the retained input snapshot.
- `CompositionScrollPresentationDeclaration` data that targets a retained scroll container `NodeKey` and resolves to a fixed-clip `CompositionScrollPresentationPlan`.
- `DrawingBackendCompositor.RenderCompositionAnimationTickAsync`, which advances the animation over the retained frame.
- `DrawingBackendCompositor.RenderCompositionScrollPresentationTickAsync`, which advances presented scroll offset over the retained frame.
- `CompositorHitTestSnapshot` publication for transform/opacity and fixed-clip scroll layers, with inverse-mapped local coordinates and reverse paint-order hit selection.
- Typed composition clock values: `CompositionTimestamp` and `CompositionDuration` carry `Stopwatch.GetTimestamp()` ticks and keep frame indexes out of animation progress.
- Animation marker events for transform/opacity and fixed-clip scroll presentation. Markers are data on the declaration, evaluated by timeline interval crossing after a successful compositor tick, and drained through a runtime-facing pump that maps typed runtime event ids to app messages outside the backend.
- A D3D12 layer content cache for disjoint composition layers. The cache is keyed by stable layer id, command range, source command hash, resource frame identity, and display scale; it reuses materialized backend payloads across compositor ticks while transform/opacity/fixed-clip state is applied per tick.
- A D3D12 render-target-backed layer cache for transform-with-content rect and GlyphAtlas text layers. Stable layer content is recorded into D3D12 textures with RTV/SRV descriptors on cache miss and then composited back through command-order render segments using textured quads on subsequent compositor ticks.
- D3D12 backend consumption through `ICompositionDrawingBackend.ExecuteComposition`.
- A PoC demo that renders static draw commands once, then updates only compositor-owned transform/opacity per frame.
- A `--diagnose-composition-scroll` diagnostic that exercises D3D12 fixed-clip scroll presentation.
- A `--diagnose-composition-multilayer` diagnostic that exercises two D3D12 composition layers in one frame.
- A `--diagnose-composition-layer-cache` diagnostic that proves layer cache miss/hit behavior across two compositor ticks with the same retained source commands.
- A `--diagnose-composition-render-target-cache` diagnostic that proves D3D12 render target miss/hit behavior across two presented frames.
- Stable machine-readable diagnostics.

## Non-Goals

- No public composition API.
- No generalized composition tree extraction.
- No generalized render-target allocation model for fixed-clip scroll, image, or vector layers yet; the current D3D12 slice is transform-with-content rect/GlyphAtlas-text and preserves layer interleaving through command-order render segments. Fixed-clip scroll remains D3D12-composited through the direct/layer-content path so viewport-space offscreen textures cannot pre-clip content before the presented scroll transform.
- No broad fallback/degradation diagnostics for unsupported producer paths yet.
- No Vulkan/Metal work.
- No replacement of normal UI frame publication. `ICompositor.RenderAsync` remains the content-update path; compositor ticks are a separate retained-frame presentation path.

## IR Contract

`CompositionFrame` is platform-neutral and lives above D3D12:

| Type | Role |
|------|------|
| `CompositionLayerId` | Stable layer identity for diagnostics and future retained mapping. |
| `CompositionTransform` | Translation-only v0 transform. |
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

The D3D12 spike materializes translation and opacity at backend execution time:

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

Marker delivery is intentionally above the backend. `DrawingBackendCompositor` evaluates markers only after `ICompositionDrawingBackend.ExecuteComposition` returns successfully, records typed `CompositionAnimationMarkerEvent` values, and leaves message mapping to the UI runtime. `CompositionMarkerEventPump` drains queued events into an app-owned mapper and dispatches mapped messages through `IMessageDispatcher<TMessage>`. Device-lost/recovered skipped ticks do not publish marker events because presentation did not commit.

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
- cached command count is diagnostic-visible
- translated and opacity-applied command counts remain stable

`--diagnose-composition-render-target-cache` must prove:

- `renderTargetBacked=True`
- first presented frame misses the D3D12 render target cache and records retained transform-with-content rect/GlyphAtlas text layer content into an RTV/SRV texture
- second presented frame hits the render target cache and composites the existing texture
- render target cached command count is diagnostic-visible
- frame and present serials advance without device removal

`--diagnose-composition-marker-runtime` must prove:

- marker queue drain count
- dispatched runtime message count
- unmapped marker count
- final runtime model state after dispatch
- compositor execution count for the marker-producing ticks

## Next Gate

Normal UI output snapshots resolve retained `CompositionTarget` and `ScrollCompositionTarget` values. Runtime-owned transform declarations resolve `NodeKey` targets into `CompositionAnimationPlan` instances; runtime-owned scroll presentation declarations resolve scroll container `NodeKey` targets into fixed-clip `CompositionScrollPresentationPlan` instances. Transform/opacity and fixed-clip scroll hit testing now reads `CompositorHitTestSnapshot`, which maps pointer coordinates through active presented layer transforms and fixed clips without a UI runtime layout pass. Marker events now provide the first generic bridge from compositor/GPU animation execution back to UI runtime, including a PoC diagnostic that dispatches a marker into `CounterMessage`. Multi-layer `CompositionFrame` execution now exists on the D3D12 path, retained nested/mixed-clip scroll targets decompose into ordered fixed-clip layers, PoC runtime policy defines commit/cancel/retarget behavior for presented scroll interruption, live wheel input can retarget from active compositor scroll presentation through `ScrollPresentationInputBridge`, and `CompositorLoop` owns scroll presentation tick pacing after `ScrollPresentationCoordinator` installs the declaration from the main app wheel path. `CompositionRenderInvalidation` now cancels active scroll presentation before viewport/tree/layout/text/max-scroll-changing frames render. The D3D12 backend now has both a layer content cache and a render-target-backed layer cache for transform-with-content rect/GlyphAtlas text command-order layers; fixed-clip scroll intentionally does not use viewport-space render-target caching. The next gate is expanding render target reuse to content-space fixed-clip/image/vector layers and adding fallback diagnostics for unsupported producer paths.
