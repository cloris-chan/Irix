# GPU Composition Architecture v0

> Design contract for the future composition layer. D3D12 remains the only implemented backend today, but the contract should map to modern explicit GPU APIs such as D3D12, Vulkan, and Metal.

## Goals

- Define a platform-neutral composition model above backend-specific D3D12/Vulkan/Metal objects.
- Enable compositor-eligible animation without rebuilding UI/layout/draw commands every tick.
- Keep device/resource ownership inside platform backends.
- Prepare for GPU offload while preserving retained publication and diagnostics contracts.
- Avoid reintroducing D3D11On12, Direct2D final overlay, or immediate-mode 2D rendering as architecture centers.

## Non-Goals

- No Vulkan or Metal backend implementation.
- No full compositor implementation.
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

This remains valid. The composition architecture adds a future layer model that can sit between retained draw output and backend execution; it does not remove the current D3D12 renderer path immediately.

## Composition IR

A future composition IR should describe retained visual/layer intent without exposing backend resources.

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

The IR must be immutable after publication for a frame or version. Backend implementations may cache translated GPU objects behind stable handles.

## Backend Capability Model

Backends should report capabilities instead of forcing one feature baseline:

| Capability | Meaning |
|------------|---------|
| `SupportsCompositorAnimations` | Backend can advance at least transform/opacity animations without UI rebuild. |
| `SupportsIndependentScrollTransform` | Backend can apply presented scroll offset under a clip. |
| `SupportsLayerOpacity` | Backend can apply per-layer opacity without re-recording content. |
| `SupportsLayerClip` | Backend can clip a layer independently of content generation. |
| `SupportsRenderTargetCaching` | Backend can cache layer content into GPU textures/render targets. |
| `SupportsDescriptorIndexing` | Backend can bind many resources through descriptor indexing or equivalent. |
| `SupportsIndirectDraw` | Backend can issue GPU-generated/indirect draws. |
| `SupportsComputePasses` | Backend can run compute culling, compaction, or effects. |
| `SupportsTimelineSynchronization` | Backend has timeline semaphore/fence style synchronization suitable for async work. |

Unsupported capabilities should fall back to draw-command updates, CPU-side batching, or explicit degradation depending on the feature.

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
| 1 | Add composition IR design and diagnostics only. | Lets style/animation contracts settle before code. |
| 2 | Layer transform/opacity property updates. | Lowest-risk compositor animation path. |
| 3 | Independent scroll presentation transform. | First major UI benefit; avoids per-tick layout/draw rebuild. |
| 4 | Layer content caching / render target reuse. | Enables larger compositor animation payoff. |
| 5 | GPU culling / batching / indirect draw. | Useful for large retained command lists. |
| 6 | Effects/material graph. | Deferred until style/material contract exists. |

## Work Placement

| Work | Preferred location |
|------|--------------------|
| MVU update and app state | App/runtime. |
| Logical scroll target and clamp | App/control runtime using layout observation. |
| Layout measurement and hit-test metadata | `Irix.Rendering`. |
| Draw command generation | `Irix.Rendering` / `Irix.Drawing`. |
| Composition IR construction | Future rendering/composition layer, platform neutral. |
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

## Advanced GPU Features

Do not design the framework around advanced GPU features before the composition IR is stable. The intended order is:

1. CPU-built retained command lists.
2. Backend-side batching and persistent upload rings.
3. Descriptor-indexed material/resource tables.
4. GPU culling/compaction for large retained scenes.
5. Indirect draw for stable batches.
6. Compute-assisted effects or vector/path preparation.
7. GPU-driven glyph atlas residency only after retained atlas command ownership is explicit.

## Diagnostics

Composition diagnostics should answer:

- Which layers are present.
- Which layers have compositor animations.
- Which properties are compositor-updated vs draw-updated.
- Which backend capabilities are active or missing.
- Whether a scroll animation ran independently or forced UI frames.
- GPU memory retained by layer caches and atlas pages.

Diagnostics must not own composition state.

## Guardrails

- `Irix.Rendering` must not own D3D12/Vulkan/Metal devices or native window resources.
- Platform backends must not own app/control state.
- Compositor animation must not mutate logical app state without a commit/cancel contract.
- Retained publication arrays and snapshots must not expose mutable pooled storage.
- D3D11On12/Direct2D final overlay must not return as a composition layer implementation.
