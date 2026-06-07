# Irix Framework Design

> Current architecture boundary for the private Windows PoC and later framework extraction work. This document is intentionally not a process log; current evidence lives in local gates and the status document.

## Current Scope

Irix is a native .NET UI framework prototype focused on a small, high-performance Windows path first:

- C# MVU core: `Model -> Update -> View -> VirtualNode`.
- Retained render pipeline: diff, layout, draw command recording, hit-test metadata, diagnostics.
- Windows PoC backend: Win32 window/input hosting plus D3D12 final composition.
- D3D12 text path: GlyphAtlas only. DirectWrite and WIC are source-data paths for shaping, metrics, raster, fallback, and glyph image decode.
- Color direction: future standard Irix `Color` is an ideal linear BT.2020 / Rec.2020 straight-alpha value; the active implementation still outputs SDR/sRGB.
- Current architecture work: style layers, animation ownership, and D3D12-first GPU composition.

The repository is still private and has no public compatibility target. When a boundary is wrong, migrate callers, tests, and docs directly instead of layering compatibility shims.

## Private Execution Mode

This is a personal private repository. Documents are working notes and regression context, not public commitments, compatibility promises, or reasons to preserve weak APIs.

Default execution mode is target-architecture first:

- Prefer the intended architecture directly: typed value models, explicit ownership, retained publication safety, D3D12/GPU-first rendering, and high-performance hot paths.
- Break internal APIs when the boundary is wrong, then migrate callers, tests, scripts, and docs in the same change.
- Do not add compatibility shims, legacy aliases, or generic fallback layers. Secondary paths are allowed only as explicit diagnostic rollback paths or to unblock a documented short-term architecture blocker.
- Treat local gates and evidence as regression guards, not as a product freeze.

## Project Boundaries

| Project | Current responsibility | Boundary |
|---------|------------------------|----------|
| `Irix.Core` | MVU runtime, `VirtualNode`, typed property/value model, patch primitives. | No backend, Win32, D3D12, DirectWrite, WIC, or retained renderer ownership. |
| `Irix.Drawing` | Stable drawing/data contracts such as `DrawCommand`, resource handles, frame resources, text slices. | No platform renderer implementation. `DrawTextRun` must not carry source strings. |
| `Irix.Rendering` | Render pipeline, layout tree, retained frame model, hit targets, diagnostics, style-only eligibility, and platform-neutral translator core. | No Win32 window ownership and no D3D12 device ownership. |
| `Irix.Platform` | Platform-neutral host/input/display abstractions. | No Windows-specific COM or GPU implementation details. |
| `Irix.Platform.Windows` | Windows host and D3D12 renderer implementation. | Owns Win32, DXGI, D3D12, DirectWrite/WIC source-data integration, and device/resource lifetimes. |
| `Irix.Poc` | App, CLI diagnostics, debug UI, and temporary adapter glue. | Not the reusable framework home. Promotion requires written ownership and diagnostic contracts first. |

Promotion contract summary:

| Candidate | Why it stays in Poc for now | Required before moving |
|-----------|-----------------------------|------------------------|
| `D3D12DrawingBackend` | Windows D3D12 drawing backend adapter over `D3D12Renderer`. | Moved to `Irix.Platform.Windows`; helper structs and tests moved with the namespace boundary. |
| `WindowDrawCommandTranslator` | Owns app glue for viewport, display scale, app/control scroll feedback, retained-frame feed, allocation attribution, and diagnostics. | Core moved to `Irix.Rendering`; outer adapter stays in Poc until scroll/input/control ownership is ready for extraction. |
| `WindowBackend` | Converts draw commands into `INativeWindow.SetContent` elements for legacy/debug GDI-style presentation. | Stay in `Irix.Poc`; do not promote as framework runtime surface. |

Detailed input/output contracts and the source grep move plan live in [Poc-Promotion-Contracts.md](Poc-Promotion-Contracts.md).

## Data Flow

```text
Win32 input
  -> app/runtime dispatch
  -> Model update
  -> VirtualNode build
  -> diff/patch
  -> RenderPipeline layout + retained frame
  -> DrawCommand + FrameDrawingResources + HitTestTarget
  -> DrawingBackendCompositor retained frame / optional CompositionAnimationDeclaration tick
  -> D3D12 rect pass + D3D12 GlyphAtlas text pass
  -> Present
```

Ownership rules:

- The MVU/update side owns application state.
- The render pipeline owns retained layout/render state.
- Layout scroll diagnostics are render/layout observation; scroll feedback and scroll state remain app/control runtime state until a framework runtime owner is chosen.
- Compositor presentation state is not app state unless a commit/cancel contract says so.
- `FrameDrawingResources` owns frame-local text/resource payloads.
- `TextSlice` is only a frame-local reference into resolver-owned text; retained/core paths must not hold raw source `string`.
- Cross-boundary data should be value typed, immutable after publication, and explicit about lifetime.

## Style, Animation, And Composition

Architecture work is design-first, but implementation should be GPU-first once a narrow contract is accepted:

| Doc | Purpose |
|-----|---------|
| [Active-Worklist.md](Active-Worklist.md) | Current work items for style, animation, GPU composition, and runtime ownership planning. |
| [Style-System.md](Style-System.md) | Splits layout style, visual style, text shaping style, composition style, and control-state style. |
| [Animation-Composition.md](Animation-Composition.md) | Splits UI-runtime animation, compositor animation, hybrid animation, and backend-internal animation. |
| [GPU-Composition-Architecture.md](GPU-Composition-Architecture.md) | Defines platform-neutral composition IR, backend capabilities, and GPU offload phases. |
| [D3D12-Composition.md](D3D12-Composition.md) | Tracks active D3D12 transform/opacity and fixed-clip scroll composition implementation details. |
| [Color-Pipeline.md](Color-Pipeline.md) | Defines canonical Irix color as linear BT.2020 and keeps current HDR work deferred behind the output mapping boundary. |

High-level rules:

- Layout style changes require layout work.
- Visual style changes may update draw commands or future layer materials without layout.
- Text shaping style changes may invalidate shaping, glyph cache, and layout metrics.
- Composition style covers transform, opacity, layer clip, and presented scroll offset.
- Control-state style is app/control runtime projection and is not owned by `Irix.Rendering`.
- Color values should canonicalize authoring input into the Irix linear BT.2020 value model; output color mapping belongs to compositor/backend output context, not to public style authoring names or per-color source metadata.
- Scroll should move toward a hybrid model: logical scroll target in app/control runtime, extent observation in layout, and presented scroll offset in compositor animation.
- The active composition implementation is D3D12-backed transform/opacity ticks, fixed-clip scroll presentation resolved from retained `NodeKey` declarations, multi-layer retained clip decomposition, active hit-test remapping, marker dispatch, and layer content caching. Normal retained-frame rendering remains the secondary path when the GPU-first path exposes an explicit blocker.

## Renderer Contract

The active Windows renderer is D3D12-only for final composition:

- Rectangles are drawn in the D3D12 rectangle pass.
- Accepted text is drawn in the D3D12 GlyphAtlas pass.
- Current color output is SDR/sRGB. Future canonical `Color` work may feed higher-precision internal color through the same drawing/composition boundaries, but HDR/scRGB/Rec.2100 output is not part of the current renderer baseline.
- DirectWrite may shape, measure, map fallback fonts, raster glyphs, and expose color glyph image data.
- WIC may decode PNG/JPEG/TIFF glyph image data for upload into BGRA atlas pages.
- DirectWrite/WIC output is source data only; final text composition stays in the D3D12 command stream.

Hard renderer boundaries:

- GlyphAtlas is the only active text composition mode.
- DirectWrite analyzer/font/raster data can feed the atlas, but does not own final composition.
- WIC decode can feed BGRA atlas pages, but does not own final composition.
- No runtime shader compilation in active D3D12 renderer source.

Glyph atlas coverage is guard-gated. New script or glyph-image-format support should move forward when it carries matching oracle/regression coverage and explicit degradation behavior. Opportunistic unguarded coverage expansion is still rejected. The detailed contract lives in [Glyph-Atlas-Design.md](Glyph-Atlas-Design.md).

## GPU / Composition Direction

Irix should target modern explicit GPU APIs for future backends. D3D12 remains the implemented backend, but design should map to Vulkan and Metal without exposing backend device objects above platform backends.

Implementation bias:

- Validate layer identity, immutable composition IR publication, compositor property updates, and diagnostics against the active D3D12 backend first.
- Do not build a generic CPU/compatibility compositor as the initial route.
- Add secondary-path code only for documented blockers found while exercising the D3D12 path.
- Keep secondary paths behind diagnostics so they do not become a second unowned renderer architecture.

Preferred GPU offload order:

1. Layer transform and opacity property updates. Implemented on D3D12.
2. Compositor-aware hit-test remapping. Implemented for active transform and fixed-clip presentation.
3. Presented scroll offset under a fixed layer clip. Implemented for retained scroll targets.
4. Multi-layer composition for nested/mixed clips. Implemented on D3D12.
5. Layer content payload caching. Implemented for disjoint D3D12 composition layers.
6. Backend-side batching and persistent upload rings.
7. GPU culling/compaction for large retained scenes.
8. Content-space internal offscreen surfaces only after bounds/origin/clip semantics are designed and direct composition still needs them.
9. Indirect draw and descriptor-indexed resource tables.
10. Color-managed material/output mapping after the canonical color value and output profile contracts exist.
11. Effects/material graph after style/material contracts exist.

Do not implement Vulkan/Metal or advanced GPU paths until the platform-neutral composition contract is stable.

## Layout, Clip, And Diagnostics

Layout dirty classification records why layout is dirty but `RenderPipeline.Build` does not skip layout yet. Retained partial apply and segmented selected render-source handoff can reuse command/resource/hit-target metadata after normal publication when guards pass; a true `StyleOnly` layout-skip branch remains future work. See [LayoutDirty-Design.md](LayoutDirty-Design.md).

Clip/scissor is default-on:

- `DrawCommand.ClipBounds` and `HitTestTarget.ClipBounds` carry clip data.
- Scroll container descendants receive intersected clip bounds.
- FillRect uses D3D12 rasterizer scissor.
- Accepted GlyphAtlas text runs use D3D12 text clip.
- Unsupported text degrades explicitly.

Diagnostic snapshots are stable formatter contracts, not renderer ownership models. They should describe current state without becoming a second source of truth. See [Diagnostics-Snapshot.md](Diagnostics-Snapshot.md).

## Execution Baseline

Current private baseline:

- Windows 10 1703 / `10.0.15063.0` runtime floor.
- `IDWriteFactory4` is assumed available.
- Single Windows D3D12 backend is the only active graphics backend.
- Current control/workflow surface remains focused on text, rectangles, button, scroll, input, layout, diagnostics, and local PoC execution.
- Current CI/local gate status is tracked in [Project_Status_and_Todo.md](Project_Status_and_Todo.md).

Not currently selected unless an explicit target-architecture task pulls them forward:

- Vulkan/Metal backend implementation.
- Public composition API.
- Public animation API.
- Theme/resource dictionary system.
- HDR color output / Rec.2100 HLG/PQ pipeline.
- Local/remote UI remoting.
- MVVM/XAML bridge.
- Full accessibility/UIA.
- Complex path/image/vector drawing beyond the current backend contract.
- Pixel/layout oracle.
- Entry-level glyph atlas eviction.

## Guarded Invariants

- Framework/core paths do not retain raw text strings.
- Public typed property/value surfaces must not regress to string-keyed domain models.
- Retained state must not hold stack memory or rented arrays.
- Allocation work must not pool or reuse retained publication arrays/snapshots without an ownership design.
- Renderer failures must be explicit diagnostics or explicit D3D12-only degradation.
- Device/resource ownership stays in the platform renderer.
- App/control runtime state does not move into `Irix.Rendering` just because layout exposes observation data.
- Compositor animation does not mutate logical app state without a commit/cancel contract.
- `Irix.Poc` code is not promoted without a contract.
- Workflow/CI churn is deferred unless [Project_Status_and_Todo.md](Project_Status_and_Todo.md) says remote validation is authoritative again.
- Composition skip and secondary-path behavior must be explicit, diagnostic-visible, and secondary to the D3D12/GPU-backed path.
- Current `DrawColor` / `WindowColor` SDR payloads must not be promoted as the canonical Irix color value; canonical color work follows [Color-Pipeline.md](Color-Pipeline.md).

## Current Work Shape

The active worklist is intentionally narrow:

- Keep local Smoke gate authoritative for broad changes.
- Treat style, animation, and GPU composition as design-first tracks with a D3D12/GPU-first implementation bias.
- Treat allocation measurement/hardening as on hold; reopen only with an ownership design and one measured target bucket.
- Use the scroll and input/control ownership contracts before extracting runtime state from Poc.
- Maintain and expand GlyphAtlas only through guarded oracle/regression-backed changes.
- Keep entry eviction and pixel/layout oracle as future work.

The actionable worklist lives in [Active-Worklist.md](Active-Worklist.md); the current status source is [Project_Status_and_Todo.md](Project_Status_and_Todo.md).

## ADR Index

| ADR | Decision | Status |
|-----|----------|--------|
| ADR-001 | D3D12 is the active Windows graphics backend. | Accepted |
| ADR-002 | `DrawCommand` isolates UI semantics from backend APIs. | Accepted |
| ADR-003 | Cross-thread/render payload ownership is explicit. | Accepted |
| ADR-004 | `HitTestTarget` travels beside drawing data. | Accepted |
| ADR-005 | Update/model execution is serialized. | Accepted |
| ADR-006 | Skia is not the active architecture center; it remains a possible future backend adapter. | Accepted |
| ADR-007 | MVVM/XAML-style authoring, if added, is compile-time bridge work, not a runtime binding engine. | Deferred |
| ADR-008 | Local UI remoting is deferred until local framework boundaries are stable. | Deferred |
| ADR-009 | Runtime XAML/IXAML parsing is outside the current selected scope. | Accepted |
| ADR-010 | `VirtualNode` stays a lightweight immutable value model, not an all-`ref struct` tree. | Accepted |
| ADR-011 | `DrawCommand` does not inline text; it uses `TextSlice` and frame resource resolution. | Accepted |
| ADR-012 | Patch/root metadata is explicit; consumers do not infer roots from buffers. | Accepted |
| ADR-013 | Windows interop uses CsWin32-generated COM wrappers where practical. | Accepted |
| ADR-014 | GlyphAtlas is the active D3D12 text composition path; DirectWrite/WIC are source-data providers. | Accepted |
| ADR-015 | Text content uses frame-local arena/slice boundaries, not a global raw string pool. | Accepted |
| ADR-016 | `TextStyle` is referenced through resource handles and backend cache ownership. | Accepted |
| ADR-017 | Cross-frame partial rendering needs stable resource snapshots; the current baseline keeps same-frame/full-resource ownership. | Design-only |
| ADR-018 | D3D12 scissor/clipping is default-on with diagnostic rollback. | Accepted |
| ADR-019 | Style layers are split into layout, visual, text shaping, composition, and control-state categories. | Design-only |
| ADR-020 | Animation ownership is split between UI runtime, compositor, hybrid, and backend-internal classes. | Design-only |
| ADR-021 | Future composition architecture targets modern explicit GPU APIs through platform-neutral composition IR and backend capabilities. | Design-only |
| ADR-022 | Irix canonical `Color` represents an ideal linear BT.2020 / Rec.2020 straight-alpha value; current output remains SDR/sRGB until HDR output mapping is implemented. | Design-only |
