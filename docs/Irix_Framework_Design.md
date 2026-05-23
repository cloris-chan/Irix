# Irix Framework Design

> Current architecture boundary for the private Windows PoC and later framework extraction work. This document is intentionally not a process log; historical evidence lives in the specific evidence docs listed by [Project_Status_and_Todo.md](Project_Status_and_Todo.md).

## Current Scope

Irix is a native .NET UI framework prototype focused on a small, high-performance Windows path first:

- C# MVU core: `Model -> Update -> View -> VirtualNode`.
- Retained render pipeline: diff, layout, draw command recording, hit-test metadata, diagnostics.
- Windows PoC backend: Win32 window/input hosting plus D3D12 final composition.
- D3D12 text path: GlyphAtlas only. DirectWrite and WIC are source-data paths for shaping, metrics, raster, fallback, and glyph image decode.

The repository is still private. Public API compatibility is not frozen; when a boundary is wrong, migrate callers, tests, and docs directly instead of layering compatibility shims.

## Project Boundaries

| Project | Current responsibility | Boundary |
|---------|------------------------|----------|
| `Irix.Core` | MVU runtime, `VirtualNode`, typed property/value model, patch primitives. | No backend, Win32, D3D12, DirectWrite, WIC, or retained renderer ownership. |
| `Irix.Drawing` | Stable drawing/data contracts such as `DrawCommand`, resource handles, frame resources, text slices. | No platform renderer implementation. `DrawTextRun` must not carry source strings. |
| `Irix.Rendering` | Render pipeline, layout tree, retained frame model, hit targets, diagnostics, style-only eligibility. | No Win32 window ownership and no D3D12 device ownership. |
| `Irix.Platform` | Platform-neutral host/input/display abstractions. | No Windows-specific COM or GPU implementation details. |
| `Irix.Platform.Windows` | Windows host and D3D12 renderer implementation. | Owns Win32, DXGI, D3D12, DirectWrite/WIC source-data integration, and device/resource lifetimes. |
| `Irix.Poc` | App, CLI diagnostics, debug UI, and temporary adapter glue. | Not the reusable framework home. Promotion requires written ownership and diagnostic contracts first. |

Promotion candidates currently staying in `Irix.Poc`:

| Candidate | Why it stays in Poc for now | Required before moving |
|-----------|-----------------------------|------------------------|
| `WindowBackend` | Couples Win32 window presentation and PoC app lifetime. | Platform/window ownership contract and replacement boundary. |
| `WindowDrawCommandTranslator` | Owns app glue for viewport, display scale, scroll feedback, retained-frame feed, allocation attribution, and diagnostics. | Explicit input/output contract and tests around those dependencies. |
| `D3D12DrawingBackend` | App-facing adapter over the Windows D3D12 renderer. | Renderer ownership, device recovery, clip mode, dirty range, scale diagnostics, and failure contract. |

## Data Flow

```text
Win32 input
  -> app/runtime dispatch
  -> Model update
  -> VirtualNode build
  -> diff/patch
  -> RenderPipeline layout + retained frame
  -> DrawCommand + FrameDrawingResources + HitTestTarget
  -> D3D12 rect pass + D3D12 GlyphAtlas text pass
  -> Present
```

Ownership rules:

- The MVU/update side owns application state.
- The render pipeline owns retained layout/render state.
- `FrameDrawingResources` owns frame-local text/resource payloads.
- `TextSlice` is only a frame-local reference into resolver-owned text; retained/core paths must not hold raw source `string`.
- Cross-boundary data should be value typed, immutable after publication, and explicit about lifetime.

## Renderer Contract

The active Windows renderer is D3D12-only for final composition:

- Rectangles are drawn in the D3D12 rectangle pass.
- Accepted text is drawn in the D3D12 GlyphAtlas pass.
- DirectWrite may shape, measure, map fallback fonts, raster glyphs, and expose color glyph image data.
- WIC may decode PNG/JPEG/TIFF glyph image data for upload into BGRA atlas pages.
- DirectWrite/WIC output is source data only; final text composition must not go through Direct2D.

Hard removal boundaries:

- No D3D11On12 final composition.
- No Direct2D factory/device/context final overlay.
- No `TextOverlaySyncStrategy`, `D3D12TextRenderer`, explicit overlay mode, hidden overlay CLI alias, or overlay fallback.
- No `IDWriteTextLayout` in active renderer/oracle paths.
- No runtime shader compilation in active D3D12 renderer source.

Glyph atlas coverage is frozen until oracle/regression split is stable. Allowed work is bugfix, guard, diagnostics, tests, evidence updates, renderer structure cleanup, and documentation. New script or glyph-image-format support requires a matching oracle/regression case first. The detailed contract lives in [Glyph-Atlas-Post-GA-Design.md](Glyph-Atlas-Post-GA-Design.md).

## Layout, Clip, And Diagnostics

Layout dirty v1 classifies why layout is dirty but does not skip layout yet. `StyleOnly` fast paths remain design-only until retained layout/resource ownership is proven. See [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md).

Clip/scissor v0 is default-on:

- `DrawCommand.ClipBounds` and `HitTestTarget.ClipBounds` carry clip data.
- Scroll container descendants receive intersected clip bounds.
- FillRect uses D3D12 rasterizer scissor.
- Accepted GlyphAtlas text runs use D3D12 text clip.
- Unsupported text degrades explicitly; it does not route to Direct2D/D3D11On12 overlay composition.

Diagnostic snapshots are stable formatter contracts, not renderer ownership models. They should describe current state without becoming a second source of truth. See [Diagnostics-Snapshot-v0.md](Diagnostics-Snapshot-v0.md).

## Version Boundary

Current v1/private baseline:

- Windows 10 1703 / `10.0.15063.0` runtime floor.
- `IDWriteFactory4` is assumed available.
- Single Windows D3D12 backend is the only active graphics backend.
- Minimal control/workflow surface remains focused on text, rectangles, button, scroll, input, layout, diagnostics, and local PoC execution.
- GitHub Actions quota is currently exhausted; local gates are authoritative until quota returns.

Deferred:

- Cross-platform backend.
- Second graphics backend.
- Local/remote UI remoting.
- MVVM/XAML bridge.
- Full accessibility/UIA.
- Theme/resource dictionary system.
- Complex path/image/vector drawing beyond the current backend contract.
- Pixel/layout oracle.
- Entry-level glyph atlas eviction.

## Guarded Invariants

- Framework/core paths do not retain raw text strings.
- Public typed property/value surfaces must not regress to string-keyed domain models.
- Retained state must not hold stack memory or rented arrays.
- Renderer failures must be explicit diagnostics or explicit D3D12-only degradation.
- Device/resource ownership stays in the platform renderer.
- `Irix.Poc` code is not promoted without a contract.
- Workflow/CI churn is deferred while Actions quota is exhausted.

## Current Backlog Shape

The active backlog is intentionally narrow:

- Keep local Smoke gate authoritative for broad changes.
- Write framework-promotion contracts before moving Poc adapter code.
- Use allocation attribution before optimizing tree/layout/snapshot boundaries.
- Maintain GlyphAtlas inside the coverage freeze.
- Keep entry eviction and pixel/layout oracle as future work.

The actionable backlog lives in [Post-V1-MVP-Backlog.md](Post-V1-MVP-Backlog.md); the current status source is [Project_Status_and_Todo.md](Project_Status_and_Todo.md).

## ADR Index

| ADR | Decision | Status |
|-----|----------|--------|
| ADR-001 | D3D12 is the v1 Windows graphics backend. | Accepted |
| ADR-002 | `DrawCommand` isolates UI semantics from backend APIs. | Accepted |
| ADR-003 | Cross-thread/render payload ownership is explicit. | Accepted |
| ADR-004 | `HitTestTarget` travels beside drawing data. | Accepted |
| ADR-005 | Update/model execution is serialized. | Accepted |
| ADR-006 | Skia is not the active architecture center; it remains a possible future backend adapter. | Accepted |
| ADR-007 | MVVM/XAML-style authoring, if added, is compile-time bridge work, not a runtime binding engine. | Deferred |
| ADR-008 | Local UI remoting is deferred until local framework boundaries are stable. | Deferred |
| ADR-009 | Runtime XAML/IXAML parsing is out of v1 scope. | Accepted |
| ADR-010 | `VirtualNode` stays a lightweight immutable value model, not an all-`ref struct` tree. | Accepted |
| ADR-011 | `DrawCommand` does not inline text; it uses `TextSlice` and frame resource resolution. | Accepted |
| ADR-012 | Patch/root metadata is explicit; consumers do not infer roots from buffers. | Accepted |
| ADR-013 | Windows interop uses CsWin32-generated COM wrappers where practical. | Accepted |
| ADR-014 | DirectWrite/Direct2D-over-D3D11On12 overlay was a bootstrap path and is replaced. | Replaced |
| ADR-015 | Text content uses frame-local arena/slice boundaries, not a global raw string pool. | Accepted |
| ADR-016 | `TextStyle` is referenced through resource handles and backend cache ownership. | Accepted |
| ADR-017 | Cross-frame partial rendering needs stable resource snapshots; v1 keeps same-frame/full-resource ownership. | Design-only |
| ADR-018 | D3D12 scissor/clipping v0 is default-on with diagnostic rollback. | Accepted |
