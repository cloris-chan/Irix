# Poc Promotion Contracts

> Current promotion boundary for non-app code living in `Irix.Poc`. This is a contract document only; no code move is implied by this file.

## Project Dependency Rules

Current project dependency shape:

```text
Irix.Core
Irix.Drawing -> Irix.Core
Irix.Platform
Irix.Rendering -> Irix.Drawing, Irix.Core, Irix.Platform
Irix.Platform.Windows -> Irix.Drawing, Irix.Platform, Irix.Rendering
Irix.Poc -> Irix.Core, Irix.Drawing, Irix.Rendering, Irix.Platform, Irix.Platform.Windows
```

Rules:

- `Irix.Rendering` may reference `Irix.Platform` for platform-neutral geometry/input/display contracts such as `PixelRectangle` and `INativeWindow` abstractions when the code still has no Win32, DXGI, D3D12, DirectWrite, WIC, or device ownership.
- `Irix.Rendering` must not reference `Irix.Platform.Windows` or own native window/GPU resources.
- `Irix.Platform.Windows` may reference `Irix.Drawing`, `Irix.Platform`, and the small backend adapter contracts in `Irix.Rendering` such as `IDeviceRecovery`. It must not own retained layout pipeline state.
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
- `TranslatorRenderPipelineFactory` for PoC-level `RenderPipeline` construction.
- `RenderPipelineProductionOwnerOptions` for segmented retained-frame ownership diagnostics.

It owns:

- A `TranslatorCore` that owns the `RetainedTree`, `RenderPipeline`, and optional `SegmentedRetainedFrameProductionOwnerFeed`.
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
| `TranslatorRenderPipelineFactory.CounterDefault` | Stay in Poc composition root | Counter default style is app-specific and must not become reusable translator core state. |

Mechanical move readiness: core-only ready, outer adapter not ready. `TranslatorCore` can move to `Irix.Rendering` with its neutral value types, while `WindowDrawCommandTranslator` remains Poc glue around viewport timing, app feedback, diagnostics, and Counter default composition.

### Translator Split Concepts

The next split should name the translation boundary before moving code:

| Concept | Ownership | Shape | Boundary |
|---------|-----------|-------|----------|
| `TranslatorInput` | Candidate `Irix.Rendering` value type | `PatchBatch`, logical viewport, `DisplayScale`, optional production-owner options, and optional previous-root/snapshot details if retained-tree ownership is externalized. | No `INativeWindow`, callbacks, app style defaults, scroll pump, CLI diagnostics, or allocation measurement. |
| `TranslatorOutput` | Candidate `Irix.Rendering` value type | `RenderFrameBatch`, layout diagnostics, dirty classifications, optional retained segment ownership, and renderer-neutral layout viewport. | No direct mutation of app model and no platform callback invocation. |
| `IViewportProvider` / `ViewportProvider` | Poc/platform adapter | Supplies physical viewport and applied renderer viewport; may call prepare-frame before reading backend/window state. | Stays outside the platform-neutral translation core because it couples window/backend timing to translation cadence. |
| `IFeedbackSink` / `FeedbackSink` | App/control adapter | Receives `ScrollFeedback`, legacy `MaxScrollY`, and future control feedback after layout. | It is app/control feedback, not rendering diagnostics and not platform feedback. |
| `TranslatorDiagnostics` | Diagnostics adapter | Allocation attribution, last viewport fields, and debug rows. | Optional wrapper around the core translator; not required for the core API. |
| `RenderPipelineFactory` | Composition root | Supplies style preset and `RenderPipeline` construction. | Do not default to `CounterStylePreset.Default` in reusable code. |

Proposed extraction order:

1. Introduce `TranslatorInput` / `TranslatorOutput` in place while the class remains in `Irix.Poc`.
2. Move viewport callbacks behind a Poc-owned `ViewportProvider`.
3. Move scroll/max-scroll callbacks behind a Poc-owned `FeedbackSink`.
4. Move allocation attribution into a diagnostics wrapper.
5. Only then move the platform-neutral translation core to `Irix.Rendering`.

### Translator Core Move Audit

`TranslatorCore` has no direct dependency on `Irix.Poc` types. Its current dependencies are neutral framework types:

| Dependency | Current owner | Move impact |
|------------|---------------|-------------|
| `PatchBatch`, `PatchBatchKind`, `RetainedTree`, `VirtualNode`, `TextBufferSnapshot` | `Irix.Core` | Compatible with `Irix.Rendering`, which already references `Irix.Core`. |
| `DisplayScale` | `Irix.Drawing` | Compatible with `Irix.Rendering`, which already references `Irix.Drawing`. |
| `PixelRectangle` | `Irix.Platform` | Compatible with the existing rule that `Irix.Rendering` may use platform-neutral geometry/display contracts. |
| `RenderPipeline`, `RenderFrameBatch`, `RenderPipelineProductionOwnerOptions`, `SegmentedRetainedFrameProductionOwnerFeed`, `RetainedRenderFrameSegmentOwnership`, `RenderPipelineBuildAllocationAttribution`, `LayoutTreeResult`, `LayoutRebuildReason`, `LayoutDirtyClassification` | `Irix.Rendering` | Natural home for the core; several dependencies are already `internal` to `Irix.Rendering`. |

Movable with the core:

| Type | Target | Reason |
|------|--------|--------|
| `TranslatorCore` | `Irix.Rendering` internal | Owns retained-tree apply and render-pipeline build only. |
| `TranslatorInput` | `Irix.Rendering` internal | Contains patch batch plus resolved physical/layout viewport and display scale; no window or callbacks. |
| `TranslatorOutput` | `Irix.Rendering` internal | Contains render frame batch plus renderer-neutral layout diagnostics; no feedback delivery. |
| `TranslatorRetainedState` | `Irix.Rendering` private/internal | Carries retained apply delta between `Apply` and `BuildOutput`; no app state. |

Remain in `Irix.Poc`:

| Type | Reason |
|------|--------|
| `WindowDrawCommandTranslator` | Adapter that composes viewport provider, feedback sink, allocation meter, core, and public last-state properties. |
| `TranslatorViewportProvider`, `TranslatorViewport` | Reads `INativeWindow`, invokes prepare-frame callbacks, and resolves Poc/window timing. |
| `TranslatorFeedbackSink` | Delivers app/control scroll feedback and legacy max-scroll callback. |
| `TranslatorAllocationMeter`, `WindowTranslateAllocationAttribution` | Diagnostics-only allocation attribution used by Poc diagnostic commands/tests. |
| `TranslatorRenderPipelineFactory.CounterDefault` | Poc composition root for `CounterStylePreset.Default`; must not move with reusable translator core. |

Rename decision: defer. `TranslatorCore` is acceptable for the in-place split. Rename to `RenderFrameTranslator` or `PatchRenderFrameTranslator` only when the mechanical move happens, so this contract update stays behavior-neutral.

### Scroll Feedback Ownership

Decision: `ScrollFeedback` is app/control feedback.

It is derived from render/layout results, but its consumer is app/control state: `CounterApplication` uses max scroll and typed scroll-container metrics to clamp or update scroll behavior. It should not be modeled as rendering diagnostics because diagnostics are read-only observation, while scroll feedback participates in runtime state correction. It also should not be modeled as platform feedback because it does not come from Win32 or the display backend.

Rules:

- `RenderPipeline` may continue to expose scroll diagnostics as layout observation.
- The translator or future translation core may project scroll diagnostics into a feedback value.
- Delivery to app/control state belongs to `FeedbackSink`, outside the platform-neutral translation core.
- CLI/debug formatting may observe scroll feedback, but must not become the owner.
- `ScrollController`, `ScrollState`, `ScrollFramePump`, and `ScrollFeedback` stay in `Irix.Poc` until a separate scroll ownership contract is written.

## `D3D12DrawingBackend`

Current role: Windows D3D12 drawing backend adapter in `Irix.Platform.Windows`.

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

Promotion decision: moved to `Irix.Platform.Windows`.

The move is intentionally mechanical: it wraps `D3D12Renderer`, owns Windows D3D12 execution concerns, and does not depend on `CounterApplication`, `WindowDrawCommandTranslator`, `ScrollFeedback`, or app message state. The helper structs in `D3D12DrawingBackend.cs` moved with it.

Move invariants:

- Preserve test visibility for `ResolveFillRectScissor`, `ResolveTextClip`, `ComputeFillRectScissorDiagnostics`, `ComputeTextClipDiagnostics`, `ExecuteCore`, and `ScaleTextStyleToPhysicalPixels`.
- Keep `FrameRenderList<T>` in `Irix.Drawing`.
- Keep `IDrawingBackend`, `IDirtyRangeAware`, `IClipScissorCapability`, and `IDeviceRecovery` contracts in their current owning projects.
- Keep D3D12-only final composition; do not add overlay fallback.

Mechanical move status: complete.

Move validation:

- `dotnet build --no-restore -c Release` passed.
- `D3D12DrawingBackendScissorTests`, `DisplayScaleTests`, and `PerformanceRegressionTests` targeted lane passed: 64 tests.
- `ProgramDiagnosticsTests` passed: 113 tests.
- Full `dotnet test --no-build -c Release --verbosity normal` passed: 701 tests.
- Glyph atlas Smoke was not run because the move did not change glyph renderer behavior, matrix expected values, or `D3D12Renderer` internals.

## `WindowBackend`

Current role: PoC legacy/debug window presentation adapter.

It converts `DrawCommand` and `HitTestTarget` into `WindowContentElement` records for `INativeWindow.SetContent`. This path is useful for GDI/window-model tests and simple debug presentation, but it is not the D3D12 renderer path and should not become framework runtime surface.

Decision: stay.

Reason:

- It depends on `WindowContentElement`, `WindowColor`, and direct `INativeWindow` presentation semantics.
- It infers button presentation by pairing rect and text commands, which is a PoC convention rather than a reusable rendering contract.
- It is valuable as a legacy/debug presentation path and test double for compositor behavior.

Future action: isolate or replace only if the GDI/window presentation tests become a maintenance burden. Do not move it into `Irix.Rendering` or `Irix.Platform.Windows` as-is.

WindowBackend remains intentionally unchanged by the translator split. It is a legacy/debug presentation path and not a reusable framework runtime surface.

## Source Grep Promotion Plan

Classes and structs in `Irix.Poc` that are not purely app model or CLI entrypoint:

| Candidate | Current category | Initial decision |
|-----------|------------------|------------------|
| `D3D12DrawingBackend` and helper structs | Windows D3D12 drawing adapter | Moved to `Irix.Platform.Windows`. |
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

1. Split `WindowDrawCommandTranslator` into a platform-neutral translation core and Poc/window glue.
2. Revisit scroll/input/control projection after translator split exposes the right contracts.
3. Keep `WindowBackend`, `WindowVisualCompositor`, and `PoCDrawingBackend` as legacy/debug presentation until replaced.
