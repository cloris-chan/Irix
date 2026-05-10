# v1 API / Control Boundary Prep

> Design-only boundary inventory for the next line after Program diagnostics runner split. This document does not move code, change APIs, enable StyleOnly fast-path, or introduce a unified diagnostics channel.

## 1. Scope

Current status:

- Program diagnostics runner split is complete and regression-only.
- Diagnostics snapshot v0 and debug UI bridge v0 remain regression-only.
- Unified diagnostics channel / event bus / registry remains postponed.
- StyleOnly fast-path implementation remains postponed; `StyleOnlyPatchPlanBuilder` is still diagnostic/planning support only.

This line only records ownership and naming boundaries for three areas:

1. Controls and `VirtualNode` construction.
2. Input event ownership and Counter routing.
3. Window/platform/render bridge glue.

Non-goals:

- Do not move runtime files.
- Do not rename public APIs.
- Do not change `VirtualNode`, layout, input, window, renderer, or compositor behavior.
- Do not introduce new abstractions just to tidy file placement.

## 2. Controls Boundary

Current controls are represented as `VirtualNodeKind` values plus loosely typed content/attributes. `VirtualNodeFactory` is a convenience layer over those primitives, not a final v1 controls API.

| Current surface | Current owner | Boundary conclusion |
|-----------------|---------------|---------------------|
| `VirtualNodeKind.Text` / `VirtualNodeFactory.Text` | `Irix.Core` node model; consumed by `Irix.Rendering` layout/draw recording | Framework primitive. Text content is core node data; layout and rendering own measurement/recording policy. |
| `VirtualNodeKind.Rectangle` / `VirtualNodeFactory.Rectangle` | `Irix.Core` node model with `Width` / `Height` attributes; consumed by `Irix.Rendering` | Framework primitive / demo drawing primitive. The factory is a low-level convenience for setting size attributes, not Counter-specific. |
| `VirtualNodeKind.Button` / `VirtualNodeFactory.Button` | `Irix.Core` node kind; `Irix.Rendering` consumes child text plus `ActionId`, `IsHovered`, `IsPressed`, `IsFocused` attributes | Framework primitive in the current renderer, but not a final controls API. The generic button shape and visual-state attributes are reusable; Counter action ids and message mapping are sample-owned. |
| `VirtualNodeKind.ScrollContainer` / `VirtualNodeFactory.ScrollContainer` | `Irix.Core` node kind; `Irix.Rendering` consumes `ScrollY` and optional `Height` attributes | Framework layout primitive. Scroll geometry/clip behavior belongs to rendering; scroll input policy and animation remain outside the node primitive. |

Counter sample convenience lives outside the primitive factory:

| Counter-specific surface | Why it is sample-owned |
|--------------------------|------------------------|
| `CounterApplication.BuildButton` | Attaches Counter action ids and derives visual-state attributes from `OwnershipSnapshot`. |
| `CounterMessage.Increment` / `Decrement` / `Reset` action ids | These are app commands, not framework command identifiers. |
| `CounterApplication.BuildScrollProbeRows` | Generates sample content to exercise scrolling. |
| Debug header rows in `CounterApplication` | Sample diagnostics presentation, not a reusable control surface. |

Naming and ownership conclusion:

- Keep `VirtualNodeFactory` as a low-level node-construction convenience for now.
- Treat `Button` as a current framework primitive, not yet a complete controls API.
- Treat attribute names (`ActionId`, `IsHovered`, `IsPressed`, `IsFocused`, `ScrollY`, `Width`, `Height`) as the current wire contract between `VirtualNode` and rendering.
- Treat attribute values and action-id vocabulary as app-owned unless the value is a generic rendering/layout concept.
- Future v1 controls work should introduce typed ownership for target ids and visual state before promoting sample helpers.

## 3. Input Boundary

Input currently enters through platform-neutral raw events, then flows through Counter-specific routing and Program runtime glue.

| Current surface | Current owner | Boundary conclusion |
|-----------------|---------------|---------------------|
| `RawInputEvent`, `RawInputEventKind`, `PointerButton` | `Irix.Platform` | Generic platform primitive. It describes native input facts and should not know about controls, actions, focus policy, or Counter messages. |
| `IPlatformHost.RawInputEvents` | `Irix.Platform` abstraction, implemented by `Irix.Platform.Windows` | Generic input source. It publishes raw events only. |
| `InputOwnershipState` / `OwnershipSnapshot` | `Irix.Poc` | Generic-looking ownership model, but still PoC-scoped: single pointer, string target ids, diagnostic event stream, and Counter visual-state needs. Candidate for future extraction only after target-id and focus/capture contracts are named. |
| `InputOwnershipEvent` | `Irix.Poc` | Diagnostic/observability detail for the current ownership state. Do not treat as a framework event bus. |
| `CounterInputRouter` | `Irix.Poc` | Counter-specific mapping. It translates raw input and ownership state into `CounterMessage` values. |
| `Program.TryMapInputForRuntime` | `Irix.Poc.Program` composition root | Runtime glue. It wraps Counter actions into `RoutedInput` or `InputVisualStateChanged` so the sample model stays in sync. |

Generic input primitive:

- Raw input facts: pointer move/press/release/wheel, key, character, focus events.
- A future target identifier concept may become generic, but current target ids are strings returned from hit testing.
- Hover/focus/press/capture ownership semantics are likely reusable, but the current implementation is intentionally scoped to the Counter PoC.

Counter-specific mapping:

- `Increment`, `Decrement`, and `Reset` action ids.
- Up/Down/R keyboard behavior.
- Enter/Space activation of the focused Counter action.
- Wheel conversion into `CounterMessage.WheelRaw` before scroll pump coalescing.
- `RoutedInput` model updates that combine action dispatch and ownership snapshot refresh.

Naming and ownership conclusion:

- Keep `RawInputEvent` in `Irix.Platform` as the generic input boundary.
- Keep `CounterInputRouter` name; it correctly advertises sample ownership.
- Do not promote `InputOwnershipState` until target id, focus scope, pointer capture, multi-pointer policy, and diagnostic event naming are designed.
- Do not introduce a global input channel as part of this prep line.

## 4. Window Glue Boundary

The current window path has three separate responsibilities: platform primitives, render bridge glue, and sample composition.

| Current surface | Current owner | Boundary conclusion |
|-----------------|---------------|---------------------|
| `IPlatformHost`, `INativeWindow`, `IScreenInfo`, `ScreenRegion`, `PixelRectangle` | `Irix.Platform` | Generic platform abstractions. They define the host/window/screen/input boundary without Counter semantics. |
| `WindowsPlatformHost` | `Irix.Platform.Windows` | Windows platform implementation. Owns native window creation, screen enumeration, raw input publication, topology changes, and platform thread lifetime. |
| `D3D12Renderer` / D2D/DWrite renderer primitives | `Irix.Platform.Windows` | Backend/platform primitives. Do not move as part of controls/API boundary prep. |
| `D3D12DrawingBackend` | `Irix.Poc` adapter today | Drawing backend adapter around the Windows renderer. Candidate for future backend adapter ownership, but not part of this no-behavior-change line. |
| `WindowDrawCommandTranslator` | `Irix.Poc` | Render bridge glue. Converts `PatchBatch` to `RenderFrameBatch` through `RetainedTree` and `RenderPipeline`, using the native window viewport and resize callback. Currently coupled to `CounterStylePreset` and max-scroll feedback. |
| `Program.Main` main loop | `Irix.Poc` composition root | Sample orchestration. Wires platform host, native window, renderer, drawing backend, compositor loop, runtime, scroll pump, input router, diagnostics, and debug UI refresh. |
| `CounterApplication` | `Irix.Poc` sample app | MVU sample semantics and view construction. Owns Counter model/messages, sample controls, debug header rows, and Counter-specific visual-state derivation. |

Platform layer:

- Owns native window lifecycle, platform thread, screens, topology, raw input publication, and native handles.
- Does not own application messages, hit-test action ids, Counter state, or render pipeline policy.

Render bridge:

- Owns translation from patch batches to render frame batches for a window viewport.
- Tracks retained tree and delegates layout/draw command generation to `RenderPipeline`.
- Current `WindowDrawCommandTranslator` is not yet a framework API because it embeds `CounterStylePreset` and sample scroll feedback shape.

Sample app glue:

- Owns composition order and event wiring.
- Owns scroll coalescing policy for the Counter PoC.
- Owns input routing from hit-test action ids to Counter messages.
- Owns debug UI row refresh and diagnostics dispatch.

Naming and ownership conclusion:

- Keep `WindowsPlatformHost` and platform abstractions in the platform layer.
- Treat `WindowDrawCommandTranslator` as the likely future render/platform bridge candidate, but do not move it until viewport, style, retained tree, and post-frame feedback contracts are explicit.
- Treat `Program.Main` as a composition root, not a framework service.
- Treat `CounterApplication` as sample app semantics, not a reusable controls package.

## 5. Prep Checklist

No code moves in this line. The next useful work is design inventory only:

| Area | Prep question | Current answer |
|------|---------------|----------------|
| Controls | Which node kinds are framework primitives? | `Text`, `Rectangle`, `Button`, and `ScrollContainer` are current primitives. Their factory methods are convenience constructors, not final controls APIs. |
| Controls | Which parts are Counter-specific? | Action-id values, ownership-derived visual state, sample rows, debug rows, and Counter messages. |
| Input | What is generic today? | Raw platform input facts and raw event publication. |
| Input | What is Counter-specific today? | Routing raw input to Counter messages and wrapping visual-state updates into the Counter model. |
| Window glue | What is platform-owned? | `IPlatformHost` / `INativeWindow` abstractions and `WindowsPlatformHost` implementation. |
| Window glue | What is bridge-owned later? | A future version of `WindowDrawCommandTranslator`, once style, viewport, retained-tree, and post-frame contracts are named. |
| Window glue | What remains sample-owned? | `Program.Main` orchestration and `CounterApplication` model/view semantics. |

Regression-only rules remain in force:

- Do not change CLI diagnostics text.
- Do not change debug UI rows.
- Do not enable StyleOnly fast-path.
- Do not move runtime, renderer, input, or backend files during this prep line.
- Do not introduce unified diagnostics channel / event bus / registry.
