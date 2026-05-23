# Poc Promotion Contracts

> Current promotion boundary for non-app code living in `Irix.Poc`. This is a contract document only; no code move is implied by this file.

## Project Dependency Rules

Current project dependency shape:

```text
Irix.Core
Irix.Drawing -> Irix.Core
Irix.Platform
Irix.Rendering -> Irix.Drawing, Irix.Core, Irix.Platform
Irix.Platform.Windows -> Irix.Drawing, Irix.Platform
Irix.Poc -> Irix.Core, Irix.Drawing, Irix.Rendering, Irix.Platform, Irix.Platform.Windows
```

Rules:

- `Irix.Rendering` may reference `Irix.Platform` for platform-neutral geometry/input/display contracts such as `PixelRectangle` and `INativeWindow` abstractions when the code still has no Win32, DXGI, D3D12, DirectWrite, WIC, or device ownership.
- `Irix.Rendering` must not reference `Irix.Platform.Windows` or own native window/GPU resources.
- `Irix.Platform.Windows` may reference `Irix.Drawing` and `Irix.Platform`, but not `Irix.Rendering` unless a separate adapter contract is accepted. The low-level Windows renderer should stay free of retained layout pipeline ownership.
- `Irix.Poc` may compose all projects and may keep app, CLI diagnostics, debug UI, and temporary adapters.

Consequence: `WindowDrawCommandTranslator` is not blocked from `Irix.Rendering` solely because it uses `Irix.Platform`; it is blocked because it currently mixes runtime adapter, app scroll feedback, diagnostics, and allocation attribution.

## `WindowDrawCommandTranslator`

Current role: runtime adapter plus PoC glue.

It consumes:

- `INativeWindow` for physical viewport fallback.
- Optional `prepareFrame` callback for backend/window pre-frame synchronization.
- Optional viewport provider for applied renderer viewport.
- Optional post-frame callback for legacy `MaxScrollY` feedback.
- `DisplayScale` for logical/physical coordinate conversion.
- `PatchBatch` from the app/runtime.
- `TranslatorRenderPipelineFactory` for the concrete `RenderPipeline`.
- `RenderPipelineProductionOwnerOptions` for segmented retained-frame ownership diagnostics.

It owns:

- A `RetainedTree`.
- A `RenderPipeline`.
- Optional `SegmentedRetainedFrameProductionOwnerFeed`.
- Last viewport/layout/dirty diagnostics.
- Scroll feedback projection from `LayoutTreeResult.ScrollDiagnostics`.
- Allocation attribution around retained apply, viewport, pipeline build, and feedback stages.

It outputs:

- `RenderFrameBatch`.
- `LastViewport`, `LastLayoutViewport`, layout rebuild diagnostics, dirty classifications.
- `LastMaxScrollY` and typed `ScrollFeedback`.
- Optional `WindowTranslateAllocationAttribution`.
- Optional retained-frame segment ownership diagnostics.

Promotion decision: stay in `Irix.Poc` until split.

Required split before move:

| Piece | Target | Reason |
|-------|--------|--------|
| Patch-to-render-frame translation core | Candidate for `Irix.Rendering` | It is a platform-neutral adapter over `RetainedTree` and `RenderPipeline`. |
| `INativeWindow` viewport fallback / prepare-frame callback | Stay in app/platform adapter | It couples translation cadence to window/backend timing. |
| Scroll feedback projection | Candidate shared runtime contract after scroll ownership is written | It is app-visible control feedback, not pure render output. |
| Allocation attribution wrapper | Diagnostics layer | It should not be mandatory for the core translator API. |
| `TranslatorRenderPipelineFactory` defaulting to `CounterStylePreset.Default` | Stay in Poc or replace with explicit style input | Current default is app-specific. |

Mechanical move readiness: no. First extract an explicit translator input struct and output struct, remove `CounterStylePreset` default coupling, and decide whether scroll feedback belongs to rendering diagnostics or app control feedback.

## `D3D12DrawingBackend`

Current role: app-facing Windows D3D12 drawing backend adapter.

It consumes:

- `D3D12Renderer` from `Irix.Platform.Windows`.
- `DrawingBackendClipMode`.
- `FrameContext`, `DrawCommand` spans, and `IFrameResourceResolver`.
- Optional dirty command ranges via `IDirtyRangeAware`.

It owns:

- Per-frame rect/text staging lists.
- Clip/scissor planning and diagnostics.
- Logical-to-physical display scale conversion for commands and text style.
- Background clear color state.
- Device recovery forwarding to `D3D12Renderer`.

It outputs:

- D3D12 renderer calls: `RenderFrame` or `ClearAndPresent`.
- Clip diagnostics: clipped command count, empty intersection skips, scissor state changes, last effective scissor, text clip skip count, last effective text clip.
- Dirty range diagnostics.
- Frame serial diagnostics pass-through.
- Device removed / recovery status.

Promotion decision: move candidate for `Irix.Platform.Windows`.

It is mechanically close to move because it already wraps `D3D12Renderer`, owns Windows D3D12 execution concerns, and does not depend on `CounterApplication`, `WindowDrawCommandTranslator`, `ScrollFeedback`, or app message state. The helper structs in `D3D12DrawingBackend.cs` should move with it.

Move preconditions:

- Preserve test visibility for `ResolveFillRectScissor`, `ResolveTextClip`, `ComputeFillRectScissorDiagnostics`, `ComputeTextClipDiagnostics`, `ExecuteCore`, and `ScaleTextStyleToPhysicalPixels`.
- Keep `FrameRenderList<T>` accessible from the target project or move the minimal reusable buffer helper intentionally.
- Keep `IDrawingBackend`, `IDirtyRangeAware`, `IClipScissorCapability`, and `IDeviceRecovery` contracts in their current owning projects.
- Keep D3D12-only final composition; do not add overlay fallback.

Mechanical move readiness: yes, after dependency check for `FrameRenderList<T>` and test namespace updates. This should be the first code move if promotion proceeds.

## `WindowBackend`

Current role: PoC legacy/debug window presentation adapter.

It converts `DrawCommand` and `HitTestTarget` into `WindowContentElement` records for `INativeWindow.SetContent`. This path is useful for GDI/window-model tests and simple debug presentation, but it is not the D3D12 renderer path and should not become framework runtime surface.

Decision: stay.

Reason:

- It depends on `WindowContentElement`, `WindowColor`, and direct `INativeWindow` presentation semantics.
- It infers button presentation by pairing rect and text commands, which is a PoC convention rather than a reusable rendering contract.
- It is valuable as a legacy/debug presentation path and test double for compositor behavior.

Future action: isolate or replace only if the GDI/window presentation tests become a maintenance burden. Do not move it into `Irix.Rendering` or `Irix.Platform.Windows` as-is.

## Source Grep Promotion Plan

Classes and structs in `Irix.Poc` that are not purely app model or CLI entrypoint:

| Candidate | Current category | Initial decision |
|-----------|------------------|------------------|
| `D3D12DrawingBackend` and helper structs | Windows D3D12 drawing adapter | Move candidate to `Irix.Platform.Windows`. |
| `WindowDrawCommandTranslator`, `TranslatorRenderPipelineFactory`, `WindowTranslateAllocationAttribution` | Runtime adapter + Poc glue | Split before move. |
| `WindowBackend`, `WindowBackendRenderResult` | Legacy/debug window presentation | Stay in Poc. |
| `WindowVisualCompositor` | Poc compositor over `INativeWindow.SetContent` | Stay with `WindowBackend`. |
| `PoCDrawingBackend` | Test/Poc backend over `INativeWindow.SetContent` | Stay as test/debug adapter unless replaced. |
| `ScrollController`, `ScrollState`, `ScrollDelta`, `ScrollFramePump`, `ScrollFeedback` | App/control runtime behavior | Candidate later, after scroll ownership contract. |
| `InputOwnershipState`, `OwnershipSnapshot`, `ControlVisualState*`, `ActionHitTestResolver` | Input/control state projection | Candidate later, after input/control contract. |
| `DiagnosticsSnapshots`, `DiagnosticsFormatter`, `DebugDiagnosticsFormatter`, `DebugUiDiagnosticsSnapshotBridge`, `IDiagnosticsProvider` | Diagnostics surfaces | Candidate later, after unified diagnostics channel decision. |
| `BackendClipTextSmokeDiagnostics`, `FullDiagnosticRunner`, `ResizeDiagnosticRunner`, `SyncDiagnosticRunner`, `TextCacheAllocationDiagnosticRunner`, glyph atlas diagnostic runners | CLI/local diagnostics | Stay in Poc. |
| `CounterApplication`, `CounterInputRouter`, `CounterStylePreset`, `ActionIdRegistry` | Sample app | Stay in Poc. |

Recommended move order:

1. Move `D3D12DrawingBackend` and its helper structs to `Irix.Platform.Windows`.
2. Split `WindowDrawCommandTranslator` into a platform-neutral translation core and Poc/window glue.
3. Revisit scroll/input/control projection after translator split exposes the right contracts.
4. Keep `WindowBackend`, `WindowVisualCompositor`, and `PoCDrawingBackend` as legacy/debug presentation until replaced.

