# GPU Composition Architecture v0

> Design contract for the composition layer. D3D12 is the implemented backend today, and the contract should map to modern explicit GPU APIs such as D3D12, Vulkan, and Metal later.

## Goals

- Define a platform-neutral composition model above backend-specific D3D12/Vulkan/Metal objects.
- Enable compositor-eligible animation without rebuilding UI/layout/draw commands every tick.
- Keep D3D12/GPU-backed composition as the implementation path.
- Keep device/resource ownership inside platform backends.
- Prepare for GPU offload while preserving retained publication and diagnostics contracts.
- Keep the D3D12 renderer path as the first implementation target instead of adding a separate immediate-mode renderer architecture.

## Non-Goals

- No Vulkan or Metal backend implementation.
- No full composition tree implementation.
- No shader/effect graph implementation.
- No retained-array pooling.
- No public composition API.
- No replacement of the existing D3D12 rectangle/GlyphAtlas passes in this document.

## Current Renderer Baseline

The current Windows path is:

```text
RenderPipeline
  -> DrawCommand + FrameDrawingResources + HitTestTarget
  -> D3D12DrawingBackend
  -> D3D12Renderer rectangle pass + GlyphAtlas text pass
  -> Present
```

This remains valid. The composition architecture adds layer animation and backend composition capability between retained draw output and backend execution; it does not remove the current D3D12 renderer path.

## Implementation Bias

The project should move aggressively toward the GPU path once ownership contracts are clear. The current implementation has a D3D12-backed transform/opacity composition spine with compositor-updated properties, diagnostics, typed composition clock values, internal `NodeKey`-addressable composition targets from retained UI output, runtime animation declarations that resolve through those targets, inverse-transform hit-test remapping for active transform layers, fixed-clip scroll presentation for retained scroll containers, retained nested/mixed-clip scroll decomposition into ordered composition layers, a marker-event pump that maps compositor-produced runtime event ids to UI runtime messages outside the backend, and first-slice multi-layer composition frame execution on D3D12. The next architectural gap is layer caching and runtime commit/cancel policy.

Do not build a broad CPU/generic compatibility compositor as the first implementation step. The existing draw-command renderer is the compatibility fallback.

New fallback code is justified only when a D3D12/GPU-first spike exposes a concrete short-term blocker such as unsafe hit-test mapping, unresolved resource lifetime, or a missing backend capability.

Fallback must be explicit and diagnostic-visible:

- State which GPU path was attempted.
- State the blocker or unsupported capability.
- Preserve current D3D12 rectangle/GlyphAtlas behavior.
- Avoid adding a second long-term renderer architecture that competes with the GPU composition path.

## Composition IR

Composition IR should describe retained visual/layer intent without exposing backend resources.

| Concept | Purpose |
|---------|---------|
| Composition tree | Parent/child visual hierarchy, z-order, and clipping. |
| Layer id | Stable handle used by runtime, compositor animation, diagnostics, and backend caches. |
| Content source | Draw-command range, cached render target, glyph/rect batch, or future image/vector content. |
| Transform | 2D matrix or decomposed translation/scale/rotation for presentation. |
| Opacity | Per-layer opacity for compositor animation. |
| Clip | Layer clip rectangle/rounded clip where supported. |
| Scroll presentation | Presented scroll offset applied as content transform under clip. |
| Dirty region | Optional invalidation region for content update. |
| Animation descriptors | Data-driven compositor animations on eligible properties. |

The IR must be immutable after publication for a frame or version. Backend implementations may cache translated GPU objects behind stable handles. The current internal runtime descriptors are `CompositionAnimationDeclaration` for transform/opacity and `CompositionScrollPresentationDeclaration` for fixed-clip scroll presentation; both target stable `NodeKey` values and resolve against `RenderPipelineRetainedInputSnapshot` into compositor plans. Declarations can include typed animation markers (`CompositionAnimationMarker`) and a `CompositionAnimationInstanceId`; marker evaluation stays in the compositor, emits `CompositionAnimationMarkerEvent` records into a queue, and never enters backend callback code. Runtime-facing code drains those events with `CompositionMarkerEventPump` and app-owned mapping to `IMessageDispatcher<TMessage>`, keeping `Irix.Rendering` generic and the backend unaware of app messages. `CompositionFrame` now carries an ordered set of `CompositionLayer` values; single-layer frames remain the zero-allocation case, while explicit multi-layer frames copy layers into immutable publication state. `DrawingBackendCompositor.RenderCompositionAnimationTickAsync` and `RenderCompositionScrollPresentationTickAsync` evaluate those plans over the retained frame, and `ICompositionDrawingBackend.ExecuteComposition` consumes the resulting `CompositionFrame`. Animation progress uses `CompositionTimestamp` and `CompositionDuration`, whose current units are `Stopwatch.GetTimestamp()` ticks; frame counters are not valid animation time.

## Backend Capability Model

Backends should report capabilities instead of forcing one feature baseline:

| Capability | Meaning |
|------------|---------|
| `TransformOpacity` | Backend can advance transform/opacity composition ticks without UI rebuild. Implemented by D3D12 through `ICompositionDrawingBackend`. |
| `ScrollPresentation` / `SupportsIndependentScrollTransform` | Backend can apply presented scroll offset under fixed clips. Implemented by D3D12 for single-layer and decomposed nested/mixed-clip retained scroll targets. |
| `SupportsLayerOpacity` | Backend can apply per-layer opacity without re-recording content. |
| `SupportsLayerClip` | Backend can clip a layer independently of content generation. |
| `SupportsRenderTargetCaching` | Backend can cache layer content into GPU textures/render targets. |
| `SupportsDescriptorIndexing` | Backend can bind many resources through descriptor indexing or equivalent. |
| `SupportsIndirectDraw` | Backend can issue GPU-generated/indirect draws. |
| `SupportsComputePasses` | Backend can run compute culling, compaction, or effects. |
| `SupportsTimelineSynchronization` | Backend has timeline semaphore/fence style synchronization suitable for async work. |

Unsupported capabilities should fall back to draw-command updates, CPU-side batching, or explicit degradation only after the D3D12/GPU-backed path has been attempted or a written blocker says it cannot be attempted safely.

## API Mapping Notes

| Concept | D3D12 | Vulkan | Metal |
|---------|------|--------|-------|
| Command recording | Command lists/allocators | Command buffers | Command buffers/encoders |
| Resource binding | Descriptor heaps/tables | Descriptor sets/bindless extensions | Argument buffers/resource tables |
| Synchronization | Fences | Fences/semaphores/timeline semaphores | Shared events/command buffer completion |
| Indirect draw | ExecuteIndirect | vkCmdDrawIndirect / indirect count | drawPrimitives indirect buffers |
| Compute culling | Compute PSO | Compute pipeline | Compute pipeline |
| Layer cache | Render target textures | Images/framebuffers | Textures/render passes |

The composition contract should not expose these backend objects to `Irix.Rendering` or `Irix.Poc`.

## GPU Offload Phases

| Phase | Work | Rationale |
|-------|------|-----------|
| 0 | Keep current D3D12 rectangle/GlyphAtlas passes. | Stable baseline. |
| 1 | Add a D3D12-first composition spine: immutable IR publication, backend handoff, and diagnostics. | Implemented for transform/opacity over retained draw-command ranges. |
| 2 | Layer transform/opacity property updates on the D3D12 path. | Implemented as compositor-owned ticks over the retained frame. |
| 3 | Stable retained layer identity from normal UI output. | Implemented as internal `CompositionTarget` resolution on retained input snapshots. |
| 4 | Runtime animation declarations targeting retained `NodeKey`/`CompositionTarget` values. | Implemented internally for transform/opacity and used by the visible composition demo. |
| 5 | Compositor-aware hit-test remapping. | Implemented for transform/opacity layers by retaining hit-target command ranges and inverse-mapping active layer transforms. |
| 6 | Independent scroll presentation transform. | Implemented for single fixed-clip retained scroll targets and nested/mixed clips through generated multi-layer target decomposition. |
| 7 | Compositor marker event queue. | Implemented for transform/opacity and scroll presentation; marker triggers use timeline interval crossing and runtime-owned event ids. |
| 8 | Runtime marker event dispatch bridge. | Implemented internally through `CompositionMarkerEventPump`, `ICompositionMarkerEventMapper<TMessage>`, and a PoC `--diagnose-composition-marker-runtime` proof. |
| 9 | Multi-layer composition frame execution. | Implemented on the D3D12 execution path with ordered layer application and `--diagnose-composition-multilayer`. |
| 10 | Nested/mixed-clip retained target decomposition. | Implemented by splitting retained scroll target command runs by fixed clip into ordered composition layers. |
| 11 | Layer content caching / render target reuse. | Enables larger compositor animation payoff. |
| 12 | GPU culling / batching / indirect draw. | Useful for large retained command lists. |
| 13 | Effects/material graph. | Deferred until style/material contract exists. |

## Work Placement

| Work | Preferred location |
|------|--------------------|
| MVU update and app state | App/runtime. |
| Logical scroll target and clamp | App/control runtime using layout observation. |
| Animation marker event mapping | App/runtime drains compositor marker events and maps `CompositionRuntimeEventId` to app messages. |
| Layout measurement and hit-test metadata | `Irix.Rendering`. |
| Draw command generation | `Irix.Rendering` / `Irix.Drawing`. |
| Composition IR construction | Platform-neutral rendering/composition layer, consumed first by `Irix.Platform.Windows`. |
| GPU resource lifetime | Platform backend, such as `Irix.Platform.Windows`. |
| Compositor animation advancement | Backend/compositor, with runtime-owned logical state. |
| Diagnostics formatting | PoC/diagnostics or future diagnostics channel. |

## Scroll On The GPU

The intended scroll composition model is:

```text
stable content draw output
  -> content layer
  -> clip layer / viewport
  -> compositor applies presentedScrollY transform
  -> present
```

Runtime still owns:

- Target scroll position.
- Clamp against max scroll.
- Gesture/input interpretation.
- Accessibility/logical state.
- Cancellation/retarget policy.

Compositor owns:

- Presented scroll offset interpolation.
- Content transform under clip.
- Optional frame pacing independent of UI rebuild.

Current implementation scope:

- Single retained scroll container.
- One contiguous command range.
- Uniform clip across the target range.
- Fixed clip in presentation space and translated content underneath.

Nested scroll, mixed child clips, and independently animated descendant layers should be decomposed into ordered composition layers instead of widening the single-layer scroll contract.

## Advanced GPU Features

Do not wait for a broad compatibility abstraction before exercising the D3D12 GPU path. The intended order is:

1. CPU-built retained command lists feeding explicit D3D12 layer updates.
2. Backend-side batching and persistent upload rings.
3. Descriptor-indexed material/resource tables.
4. GPU culling/compaction for large retained scenes.
5. Indirect draw for stable batches.
6. Compute-assisted effects or vector/path preparation.
7. GPU-driven glyph atlas residency only after retained atlas command ownership is explicit.

## Diagnostics

Composition diagnostics should answer:

- Which layers are present, including multi-layer frame counts.
- Which layers have compositor animations.
- Which properties are compositor-updated vs draw-updated.
- Which backend capabilities are active or missing.
- Whether a scroll animation ran independently or forced UI frames.
- Which compositor marker events were produced, including animation instance, target, iteration, progress, and playback direction.
- Which marker events were drained, dispatched, or left unmapped by runtime-owned mapping.
- GPU memory retained by layer caches and atlas pages.

Diagnostics must not own composition state.

## Guardrails

- `Irix.Rendering` must not own D3D12/Vulkan/Metal devices or native window resources.
- Platform backends must not own app/control state.
- Compositor animation must not mutate logical app state without a commit/cancel contract.
- Retained publication arrays and snapshots must not expose mutable pooled storage.
- Composition layer implementation must build on the active D3D12 backend path first.
