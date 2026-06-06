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

Promotion decision: outer adapter stays in `Irix.Poc`; core moved to `Irix.Rendering`.

Post-split boundary:

| Piece | Target | Reason |
|-------|--------|--------|
| Patch-to-render-frame translation core | Moved to `Irix.Rendering` | It is a platform-neutral adapter over `RetainedTree` and `RenderPipeline`. |
| `INativeWindow` viewport fallback / prepare-frame callback | Stay in app/platform adapter | It couples translation cadence to window/backend timing. |
| Scroll feedback projection | Candidate shared runtime contract after scroll ownership is written | It is app-visible control feedback, not pure render output. |
| Allocation attribution wrapper | Diagnostics layer | It should not be mandatory for the core translator API. |
| `TranslatorRenderPipelineFactory.CounterDefault` | Stay in Poc composition root | Counter default style is app-specific and must not become reusable translator core state. |

Mechanical move status: core moved, outer adapter remains Poc glue. `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` now live in `Irix.Rendering`; `WindowDrawCommandTranslator` remains Poc glue around viewport timing, app feedback, diagnostics, and Counter default composition.

### Translator Split Concepts

The current split names the translation boundary before any public API is introduced:

| Concept | Ownership | Shape | Boundary |
|---------|-----------|-------|----------|
| `TranslatorInput` | `Irix.Rendering` internal value type | `PatchBatch`, physical viewport, layout viewport, and `DisplayScale`. | No `INativeWindow`, callbacks, app style defaults, scroll pump, CLI diagnostics, or allocation measurement. |
| `TranslatorOutput` | `Irix.Rendering` internal value type | `RenderFrameBatch`, layout diagnostics, dirty classifications, layout result, max scroll, and renderer-neutral layout viewport. | No direct mutation of app model and no platform callback invocation. |
| `IViewportProvider` / `ViewportProvider` | Poc/platform adapter | Supplies physical viewport and applied renderer viewport; may call prepare-frame before reading backend/window state. | Stays outside the platform-neutral translation core because it couples window/backend timing to translation cadence. |
| `IControlFeedbackSink` / `TranslatorFeedbackSink` | App/control adapter | Receives `ScrollFeedback`, legacy `MaxScrollY`, and future control feedback after layout. | It is app/control feedback, not rendering diagnostics and not platform feedback. |
| `TranslatorDiagnostics` | Diagnostics adapter | Allocation attribution, last viewport fields, and debug rows. | Optional wrapper around the core translator; not required for the core API. |
| `RenderPipelineFactory` | Composition root | Supplies style preset and `RenderPipeline` construction. | Do not default to `CounterStylePreset.Default` in reusable code. |

Extraction status:

Completed extraction:

1. `TranslatorInput` / `TranslatorOutput` introduced in place.
2. Viewport callbacks moved behind a Poc-owned `TranslatorViewportProvider`.
3. Scroll/max-scroll callbacks moved behind a Poc-owned `TranslatorFeedbackSink`.
4. Allocation attribution moved into a Poc-owned `TranslatorAllocationMeter`.
5. Platform-neutral `TranslatorCore` moved to `Irix.Rendering`.

### Translator Core Move Audit

`TranslatorCore` has no direct dependency on `Irix.Poc` types. Its dependencies are neutral framework types:

| Dependency | Current owner | Move impact |
|------------|---------------|-------------|
| `PatchBatch`, `PatchBatchKind`, `RetainedTree`, `VirtualNode`, `TextBufferSnapshot` | `Irix.Core` | Compatible with `Irix.Rendering`, which already references `Irix.Core`. |
| `DisplayScale` | `Irix.Drawing` | Compatible with `Irix.Rendering`, which already references `Irix.Drawing`. |
| `PixelRectangle` | `Irix.Platform` | Compatible with the existing rule that `Irix.Rendering` may use platform-neutral geometry/display contracts. |
| `RenderPipeline`, `RenderFrameBatch`, `RenderPipelineProductionOwnerOptions`, `SegmentedRetainedFrameProductionOwnerFeed`, `RetainedRenderFrameSegmentOwnership`, `RenderPipelineBuildAllocationAttribution`, `LayoutTreeResult`, `LayoutRebuildReason`, `LayoutDirtyClassification` | `Irix.Rendering` | Natural home for the core; several dependencies are already `internal` to `Irix.Rendering`. |

Moved with the core:

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
| `IControlFeedbackSink` / `TranslatorFeedbackSink` | Delivers app/control scroll feedback and legacy max-scroll callback. |
| `TranslatorAllocationMeter`, `WindowTranslateAllocationAttribution` | Diagnostics-only allocation attribution used by Poc diagnostic commands/tests. |
| `TranslatorRenderPipelineFactory.CounterDefault` | Poc composition root for `CounterStylePreset.Default`; must not move with reusable translator core. |

Rename decision: defer. `TranslatorCore` remains internal; rename to `RenderFrameTranslator` or `PatchRenderFrameTranslator` only if a later public/runtime API boundary needs the more specific name.

### Scroll Feedback Ownership

Decision: scroll is split across layout observation, app/control feedback, and app runtime state. It is not one movable unit yet.

`ScrollFeedback` is derived from render/layout results, but its consumer is app/control state: `CounterApplication` uses max scroll and typed scroll-container metrics to clamp or update scroll behavior. It should not be modeled as rendering diagnostics because diagnostics are read-only observation, while scroll feedback participates in runtime state correction. It also should not be modeled as platform feedback because it does not come from Win32 or the display backend.

Current ownership:

| Type / concept | Current owner | Contract |
|----------------|---------------|----------|
| `ScrollContainerDiag` / `LayoutTreeResult.ScrollDiagnostics` | `Irix.Rendering` layout observation | Published immutable layout diagnostics. Same-frame consumers may derive max scroll and feedback, but the render pipeline does not own app scroll state. |
| `ScrollFeedback`, `ScrollContainerMetrics`, `ScrollContainerId` | `Irix.Poc` app/control feedback | Projection from layout diagnostics into app-visible control feedback. Delivery is owned by `TranslatorFeedbackSink`; future extraction needs a framework control-feedback contract. |
| `ScrollController` | `Irix.Poc` app runtime behavior | Pure functions for delta conversion, clamping, and animation tick over app-owned `ScrollState`. Candidate for framework runtime only after input and control ownership are no longer Counter-specific. |
| `ScrollState`, `ScrollDelta`, `ScrollMetrics`, `SystemScrollSettings` | `Irix.Poc` app runtime state/config | App-owned runtime values. They are not retained render state and must not be written by `RenderPipeline`. |
| `ScrollFramePump` | `Irix.Poc` app runtime scheduling | Coalesces pending pixel deltas and dispatches app messages. It is not a renderer frame pump and should not move into `Irix.Rendering`. |
| `ScrollDiagnosticsSnapshot` / formatter rows | `Irix.Poc` diagnostics | Observation/output only. Diagnostics may read scroll state and layout diagnostics, but do not own either. |

Rules:

- `RenderPipeline` may continue to expose scroll diagnostics as layout observation.
- The translator or future control adapter may project scroll diagnostics into a feedback value.
- Delivery to app/control state belongs to `FeedbackSink`, outside the platform-neutral translation core.
- CLI/debug formatting may observe scroll feedback, but must not become the owner.
- `ScrollController`, `ScrollState`, `ScrollFramePump`, and `ScrollFeedback` stay in `Irix.Poc` until a separate code-extraction commit chooses a framework runtime owner.
- Do not move scroll runtime types together with renderer, glyph, D3D12 backend, or allocation changes.

Extraction candidates after the contract is promoted beyond documentation:

| Candidate | Possible target | Blocker |
|-----------|-----------------|---------|
| Pure scroll state/update functions | Framework runtime/control package | Needs a framework-level control state owner and non-Counter input message model. |
| Scroll feedback projection | Framework control adapter | Needs a stable control feedback channel separate from diagnostics and render output. |
| Scroll frame pump | App/runtime scheduler | Needs a runtime frame scheduling contract; must stay separate from renderer presentation cadence. |

### Input / Control Projection Ownership

Decision: input/control projection remains Counter/Poc runtime for now.

The current input path is useful and tested, but it is not yet a reusable framework input runtime. It assumes a single pointer, left-button capture semantics, Counter `ActionId` mapping, button-only visual projection, and direct app message construction.

Current ownership:

| Type / concept | Current owner | Contract |
|----------------|---------------|----------|
| `InputOwnershipState` / `OwnershipSnapshot` | `Irix.Poc` app runtime state | Tracks single-pointer hover, pressed, captured, and focused targets for Counter. Candidate runtime state, but not movable until multi-control ownership and action dispatch are no longer Counter-specific. |
| `InputOwnershipEvent` / diagnostics ring | `Irix.Poc` diagnostics over app input state | Diagnostic observation only. It should follow the input owner if a future runtime extraction happens. |
| `IInputHitTestService`, `IActionHitTestResolver`, and resolver/service implementations | `Irix.Poc` input adapter | Bridges physical input coordinates to `ActionId` through Poc/compositor hit targets. The first renderer-neutral service shape exists, but the identity model is still Counter/Poc `ActionId`. |
| `IAppMessageDispatchMapper`, `IControlFeedbackDispatchMapper`, and `CounterAppMessageDispatchMapper` | `Irix.Poc` app message adapter | Converts routed input results, ownership snapshots, and max-scroll feedback into `CounterMessage` dispatch values. Runtime and marker dispatch use a separate Poc dispatch sink, and wheel raw input dispatches through a Poc scroll presentation sink; broader framework runtime ownership still needs a non-Counter contract. |
| `CounterInputRouter` | `Irix.Poc` sample app router | Maps raw input plus ownership state to `CounterMessage`; it must not be promoted as framework runtime. |
| `ControlVisualState`, `ControlVisualStateProjection`, `ControlVisualStatePropertyAdapter`, `ButtonPropertyBundle` | `Irix.Poc` control projection | Converts ownership snapshot into Counter button properties. Candidate concept, but current shape is button-specific and property-array publishing remains app-authoring glue. |

Rules:

- Input ownership is app/control runtime state, not rendering diagnostics and not platform backend state.
- Hit testing may consume renderer-produced hit targets, but the input owner must not own retained render frames.
- `ControlVisualState` projection is a control feedback/projection layer. It should not move into `Irix.Rendering` because it emits `VirtualNodeProperty` updates for app/control state.
- `ActionId` dispatch and `CounterMessage` mapping are sample-app concerns. A framework extraction must introduce typed control actions or routed commands first.
- Keep the current code in `Irix.Poc` until a future commit can move one coherent unit with tests and without Counter-specific assumptions.

### Scroll / Input Runtime Owner Boundary

Decision: the next scroll/input extraction must be an app/control runtime boundary, not a renderer move. `Irix.Rendering` may publish layout observation, retained hit targets, and compositor presentation samples, but it must not become the owner of logical scroll state, input ownership state, control visual state, or app message dispatch.

The current Poc path already has the correct split, even though the names still live in one project:

```text
platform input
  -> app input owner resolves hit target
  -> app/control runtime updates logical state
  -> translator projects layout feedback after render
  -> compositor may sample/cancel presented scroll for continuity
  -> app/control runtime dispatches the next app message
```

Framework extraction may introduce these contracts, in this order:

| Boundary | Owner | Contract |
|----------|-------|----------|
| Logical state owner | App/control runtime | Owns `ScrollState`, accumulator, target/current position, max-scroll clamp state, animation-active bit, input ownership snapshot, focus, capture, and pressed/hovered targets. Rendering can observe derived geometry only. |
| Feedback owner | App/control feedback adapter | Receives layout-produced scroll metrics after translation and delivers them to runtime state. Feedback is mutable runtime correction, not renderer diagnostics. |
| Compositor sampling owner | App/control scroll presentation coordinator | Calls compositor-loop sample/cancel APIs to preserve presented-origin continuity before dispatching a new logical layout frame. The compositor owns the presented value only while the presentation is active. |
| Hit-test service access | Input/control adapter | Reads a renderer-neutral hit-test service that accounts for active compositor presentation. The adapter resolves input coordinates to control/action identity without owning retained render frames. |
| App message dispatch owner | App runtime dispatcher | Maps control actions, scroll interrupts, wheel deltas, marker events, and feedback corrections into app messages. `Irix.Rendering` and platform backends must not construct app messages. |

Required interface shapes before code moves:

| Interface shape | Purpose | Must not include |
|-----------------|---------|------------------|
| Scroll feedback sink | Accepts immutable control-feedback values derived from layout diagnostics. | CLI/debug formatting, renderer state mutation, or platform callbacks. |
| Scroll presentation sampler | Samples and cancels active presented scroll on the compositor thread. | App model mutation, `CounterMessage`, or direct hit-test routing. |
| Hit-test service | Resolves physical or logical input coordinates against the current retained/composited hit-test snapshot. | `ActionIdRegistry`, `CounterApplication`, or renderer ownership transfer. |
| Input action mapper | Maps resolved control identity plus raw input to runtime messages. | Retained frame access, D3D12/backend types, or layout mutation. |
| App message dispatch mapper | Maps routed input/control results and ownership snapshots into app dispatch messages. | Renderer/backend types, retained-frame access, or direct `Runtime<TModel, TMessage>` dispatch calls. |

Extraction guardrails:

- Moving pure scroll state/update functions is allowed only after the new owner can dispatch runtime messages without `CounterMessage`.
- Moving `ScrollPresentationCoordinator` requires a compositor sampler interface and a retained-snapshot provider interface; it must not depend on `WindowDrawCommandTranslator`.
- Moving input ownership requires a renderer-neutral hit-test service and a control identity model wider than `ActionIdRegistry`.
- Moving control visual projection requires a framework control-state contract; button property publishing alone is not enough.
- Diagnostics may follow the owner as read-only snapshots, but diagnostics must not become the scheduling, feedback, or message-dispatch channel.
- No extraction commit may also move renderer, glyph, D3D12 backend, or allocation-optimization code.
- Current source guards enforce the pre-extraction boundary by keeping `CounterMessage`, `ActionIdRegistry`, feedback/input/dispatch adapters, and app runtime sinks out of `Irix.Rendering` and `Irix.Platform.Windows`. These guards do not extract runtime code; they make the prerequisite boundary auditable before a future extraction commit.

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
- Keep D3D12 final composition; do not add a second text composition path.

Mechanical move status: complete.

Validation status:

- The mechanical move is complete and covered by the normal local test gates.
- Keep current pass counts and broad-rendering evidence in [Project_Status_and_Todo.md](Project_Status_and_Todo.md), local test output, and glyph-atlas guard summaries rather than duplicating stale numbers here.

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
| `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, `TranslatorRetainedState` | Platform-neutral translation core | Moved to `Irix.Rendering`. |
| `WindowDrawCommandTranslator`, `TranslatorRenderPipelineFactory`, `WindowTranslateAllocationAttribution` | Runtime adapter + Poc glue | Stay in Poc until app/control feedback, diagnostics, allocation attribution, and composition-root contracts change. |
| `WindowBackend`, `WindowBackendRenderResult` | Legacy/debug window presentation | Stay in Poc. |
| `WindowVisualCompositor` | Poc compositor over `INativeWindow.SetContent` | Stay with `WindowBackend`. |
| `PoCDrawingBackend` | Test/Poc backend over `INativeWindow.SetContent` | Stay as test/debug adapter unless replaced. |
| `ScrollController`, `ScrollState`, `ScrollDelta`, `ScrollFramePump`, `ScrollFeedback` | App/control runtime behavior | Candidate later, after scroll ownership contract. |
| `InputOwnershipState`, `OwnershipSnapshot`, `ControlVisualState*`, `ActionHitTestResolver` | Input/control state projection | Candidate later, after input/control contract. |
| `DiagnosticsSnapshots`, `DiagnosticsFormatter`, `DebugDiagnosticsFormatter`, `DebugUiDiagnosticsSnapshotBridge`, `IDiagnosticsProvider` | Diagnostics surfaces | Candidate later, after unified diagnostics channel decision. |
| `BackendClipTextSmokeDiagnostics`, `FullDiagnosticRunner`, `ResizeDiagnosticRunner`, `SyncDiagnosticRunner`, `TextCacheAllocationDiagnosticRunner`, glyph atlas diagnostic runners | CLI/local diagnostics | Stay in Poc. |
| `CounterApplication`, `CounterInputRouter`, `CounterStylePreset`, `ActionIdRegistry` | Sample app | Stay in Poc. |

Recommended move order:

1. Keep allocation measurement/hardening on hold; reopen only with an ownership design and one measured target bucket.
2. Use the scroll and input/control contracts above before extracting app/control runtime state from Poc.
3. Keep `WindowBackend`, `WindowVisualCompositor`, and `PoCDrawingBackend` as legacy/debug presentation until replaced.
