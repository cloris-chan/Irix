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

It also expands those boundaries with draft naming for target ids, action ids, visual state projection, scroll ownership, and a future window translator contract. These names are design vocabulary only; they do not rename the current API surface.

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

## 5. Target and Action Identity Boundary

The current PoC uses string ids in several places. For v1 API design, those strings should not collapse into a single `targetId` concept. The boundary vocabulary should separate hit testing, focus, pointer capture, control activation, and app commands.

| Draft concept | Meaning | Current source or equivalent | Owner boundary |
|---------------|---------|------------------------------|----------------|
| `HitTestTargetId` | The rendered target under a pointer for the current frame. It answers "what visual/control surface was hit?" | Current hit-test result strings used by input routing | Render/input boundary. It is frame-derived and should not be treated as an app command. |
| `ControlActionId` | The activation identifier attached to a control, such as a button action. It answers "what control action should activation request?" | Current `ActionId` attribute on `Button` nodes | Control/view boundary. The attribute name stays as-is for now; future API can type this concept. |
| `FocusTargetId` | The target that receives keyboard focus and keyboard activation policy. | Current focused target inside `InputOwnershipState` / `OwnershipSnapshot` | Input ownership boundary. It can differ from hover and capture targets. |
| `PointerCaptureTargetId` | The target that owns an active pointer press/drag sequence until release or cancel. | Current pressed/captured target inside `InputOwnershipState` / `OwnershipSnapshot` | Input ownership boundary. It should outlive transient hover changes during a press. |
| `AppCommandId` | The application command vocabulary after routing a control action into app semantics. | Counter `Increment`, `Decrement`, and `Reset` message/action vocabulary | App boundary. It is sample-owned today and should not leak into platform/render layers. |

Current flow, named without changing code:

1. `RawInputEvent` enters from platform.
2. Hit testing yields a `HitTestTargetId` candidate for pointer events.
3. Input ownership updates hover, focus, and capture targets.
4. A control activation reads a `ControlActionId` from the target/control.
5. `CounterInputRouter` maps the control action to an `AppCommandId` / `CounterMessage`.
6. `Program.TryMapInputForRuntime` wraps the result into the current runtime messages.

Naming rules for future work:

- Do not use bare `targetId` in promoted APIs when the role is known; choose hit-test, focus, or capture vocabulary.
- Do not use `ActionId` to mean app command once a typed control API exists; keep `ControlActionId` and `AppCommandId` distinct.
- Keep the current `ActionId` attribute and string target ids unchanged during this prep line.
- Treat Counter command ids as sample vocabulary until an app command abstraction is explicitly designed.

Typed identity representation prep:

| Option | Fit | Recommendation |
|--------|-----|----------------|
| Raw `string` everywhere | Matches current PoC and is simple to serialize/debug | Do not promote as the v1 API shape. It keeps the current ambiguity between hit-test, focus, capture, action, and app command ids. |
| Shared generic id, such as `Id<TRole>` | Provides compile-time separation with one implementation | Defer. It may be useful internally later, but it makes public API signatures less obvious and can leak marker-type plumbing into app code too early. |
| Non-generic `readonly record struct` wrappers around string values | Gives role-specific types while preserving current string payloads and diagnostics readability | Recommended first v1 direction. Use separate wrappers such as `HitTestTargetId`, `ControlActionId`, `FocusTargetId`, `PointerCaptureTargetId`, and possibly app-owned command wrappers. |
| Numeric handles | Fast and compact if backed by a registry | Do not start here. The current system has no stable control registry, generation counter, or lifetime model for handles. |

Recommended direction: start with role-specific string-backed `readonly record struct` identity wrappers when implementation begins. They keep equality/value semantics straightforward, preserve existing string ids as the payload, and make accidental cross-use visible at compile time. Avoid a generic id type in the first public pass unless a repeated implementation cost appears after the non-generic wrappers are designed.

Identity prep gates before implementation:

- Decide which ids are frame-local (`HitTestTargetId`) and which may persist across frames (`FocusTargetId`, `PointerCaptureTargetId`, `ControlActionId`).
- Decide whether empty/default values are allowed and how they map to "no target" without changing current behavior.
- Keep conversion from current strings at the boundary; do not require all `VirtualNode` attributes to become typed in the same step.
- Keep `AppCommandId` app-owned unless a separate command abstraction is explicitly opened.

## 6. Visual State Ownership

`IsHovered`, `IsPressed`, and `IsFocused` are currently `VirtualNode` attributes consumed by rendering. They should be treated as renderable visual-state inputs produced from input ownership state, not as independent Counter domain state and not as renderer-owned interaction state.

| Attribute | Current storage | Current producer | Current consumer | Boundary conclusion |
|-----------|-----------------|------------------|------------------|---------------------|
| `IsHovered` | Boolean `VirtualNodeAttribute` on button-like nodes | `CounterApplication` derives it from `OwnershipSnapshot` | `Irix.Rendering` style/layout/draw recording | Visual state projection from input ownership. Rendering may style it, but should not define hover policy. |
| `IsPressed` | Boolean `VirtualNodeAttribute` on button-like nodes | `CounterApplication` derives it from pressed/capture ownership | `Irix.Rendering` style/layout/draw recording | Visual state projection from pointer capture/press state. It is not the app command itself. |
| `IsFocused` | Boolean `VirtualNodeAttribute` on button-like nodes | `CounterApplication` derives it from focused ownership | `Irix.Rendering` style/layout/draw recording | Visual state projection from focus ownership. It is not keyboard routing policy by itself. |

Future Button API boundary:

- Rendering owns how visual-state attributes affect drawing, style selection, and layout invalidation.
- Input ownership owns hover, focus, press, and capture facts.
- A future controls layer may own a typed `ControlVisualState` projection from input ownership into renderable attributes.
- App code owns command handling and model updates after activation; it should not need to hand-roll hover/press/focus projection once a generic controls layer exists.
- Until that layer exists, `CounterApplication` remains the projection site and the existing attributes remain the wire contract.

`ControlVisualState` prep:

| Field | Source | Projection rule | Compatibility with current attributes |
|-------|--------|-----------------|---------------------------------------|
| `IsHovered` | Hit-test target plus hover ownership | True when the control's target id is the current hover target | Continue emitting the existing `IsHovered` boolean attribute. |
| `IsPressed` | Pointer capture / active press ownership | True when the control owns the active press/capture sequence | Continue emitting the existing `IsPressed` boolean attribute. |
| `IsFocused` | Focus ownership | True when the control owns keyboard focus | Continue emitting the existing `IsFocused` boolean attribute. |
| `IsFocusVisible` | Future focus modality policy | Optional future field for keyboard-visible focus rings | Do not add an attribute in this prep line; renderer can keep using `IsFocused`. |
| `IsEnabled` | Future control/app policy | Optional future field for disabled controls | Do not add an attribute in this prep line; no disabled behavior exists today. |

Projection location recommendation:

1. Today: `CounterApplication` continues deriving the three attributes from `OwnershipSnapshot`.
2. First future controls step: introduce a pure projection helper that maps typed target/focus/capture ownership into `ControlVisualState`.
3. Compatibility adapter: convert `ControlVisualState` back into the existing `IsHovered`, `IsPressed`, and `IsFocused` attributes so rendering behavior and diagnostics remain unchanged.
4. Later renderer cleanup: rendering may read a typed visual-state payload only after the attribute compatibility path is proven equivalent.

Do not make the renderer compute `ControlVisualState`; it should consume visual-state inputs and decide style/draw output. Do not make app command handling depend on visual-state fields; activation remains a routing concern.

Design answer for `Button` v1 prep: the three attributes are current rendering attributes whose values are produced by input-state projection. They are likely future control visual state, but they are not Counter app state and should not be computed inside the renderer.

## 7. Scroll Ownership

Scroll currently spans node attributes, layout feedback, input conversion, animation, and sample state. The v1 boundary should keep these roles separate before extracting a reusable scroll primitive.

| Scroll surface | Current owner | Boundary conclusion |
|----------------|---------------|---------------------|
| `ScrollY` attribute | `VirtualNode` wire contract consumed by `Irix.Rendering` | Layout/render input. It is the pixel offset used for layout and clipping, not the animation state. |
| `ScrollState` | `Irix.Poc` | PoC scroll model containing accumulator, target position, animated position, max scroll, and animation flag. Candidate for extraction only after container ids and feedback contracts are named. |
| `ScrollController` | `Irix.Poc` | Pure scroll transformation policy. Reusable-looking, but still tied to current PoC contracts and naming. |
| `ScrollFramePump` | `Irix.Poc` | Animation/coalescing scheduler for the Counter PoC. It dispatches Counter messages and should not be treated as runtime infrastructure yet. |
| `ScrollMetrics` | `Irix.Poc` | Per-container geometry and content extent input for delta conversion. Future bridge candidate as layout feedback. |
| `SystemScrollSettings` | `Irix.Poc` defaults today | Platform/user preference boundary. Future Windows ownership can read system wheel settings, but the current defaults stay unchanged. |
| App-level scroll model | `CounterApplication` / `CounterModel` | Sample-owned state choice: which container scrolls, how messages update the model, and how debug rows expose it. |

Future extraction boundary:

- Layout should consume `ScrollY` and report scroll metrics/max scroll; it should not own wheel conversion or animation.
- Input should produce scroll deltas from raw input; it should not own layout metrics or app persistence.
- A scroll controller may convert deltas and ticks into `ScrollState`, using `ScrollMetrics` and `SystemScrollSettings` as explicit inputs.
- A scheduler/pump may request animation frames, but it should not dispatch Counter-specific messages in a promoted API.
- App state remains responsible for which scroll container is active and how scroll state is stored until a generic multi-container scroll model exists.

Do not extract scroll primitives until these names are explicit: scroll container id, scroll state owner, metrics feedback owner, settings provider, frame scheduler, and app model handoff.

Extraction sequence and blockers:

| Step | Candidate | Must be true before extraction | Blockers that keep it in `Irix.Poc` |
|------|-----------|--------------------------------|------------------------------------|
| 1 | `ScrollState` value model | Scroll container identity, max-scroll feedback, and app persistence ownership are named | Current state is single-container and stored in the Counter model shape. |
| 2 | `ScrollController` pure transforms | Inputs are explicit: `ScrollDelta`, `ScrollMetrics`, `SystemScrollSettings`, `ScrollState`, and elapsed time | Any hidden dependency on Counter messages, debug rows, or current default metrics blocks promotion. |
| 3 | `ScrollMetrics` / layout feedback contract | Render/layout feedback reports typed container metrics instead of only a positional max-scroll double | `WindowDrawCommandTranslator` currently reports `Action<double>` max-scroll feedback only. |
| 4 | `ScrollFramePump` scheduler | Frame request, cancellation, pending-delta draining, and dispatch callback are named without `CounterMessage` | Current pump dispatches `CounterMessage.ScrollFrame` and reads Counter scroll state. It must move last. |

Recommended order: extract nothing until the feedback contract is typed; then move pure value/types and `ScrollController` before any scheduler. `ScrollFramePump` is last because it touches async scheduling, cancellation, render wait timing, and app message dispatch. Moving the pump before the model/controller contract is stable would bake Counter-specific flow into framework runtime.

## 8. Window Translator Contract Draft

`WindowDrawCommandTranslator` is the likely future render/platform bridge candidate, but promotion requires an explicit contract. The current type constructs `RenderPipeline` with `CounterStylePreset.Default`, pulls viewport data from a window/provider, owns a retained tree, and reports only max scroll through an `Action<double>` callback.

If promoted later, the contract should inject or name these dependencies explicitly:

| Contract part | Current shape | Future boundary requirement |
|---------------|---------------|-----------------------------|
| Style source | Hardcoded `CounterStylePreset.Default` inside the translator | Inject a style preset/resolver/pipeline factory. A framework translator must not depend on Counter styling. |
| Viewport source | `INativeWindow.Region.PhysicalBounds` or optional `Func<PixelRectangle>` | Inject a viewport source with clear pixel units and resize timing. The translator should not assume one native window shape. |
| Retained tree owner | Private `RetainedTree` inside the translator | Decide whether the bridge owns retained state or receives it from a higher render service. Patch application ownership must be explicit. |
| Render pipeline | Private `RenderPipeline` instance | Inject or factory-create with explicit style and options. Avoid hidden sample defaults. |
| Prepare-frame hook | Optional `Action? prepareFrame` | Keep as a named frame-preparation hook only if the backend/resource lifetime contract needs it. |
| Post-frame feedback | Optional `Action<double>` receiving max scroll | Replace with typed feedback if promoted. Feedback should include named scroll metrics and layout diagnostics, not a positional double. |
| Scroll metrics | `LastMaxScrollY` plus current layout viewport exposure | Model as explicit render feedback: max scroll, viewport extent, content extent, and target container identity. |
| Diagnostics exposure | `LastViewport`, `LastLayoutViewport`, rebuild counts, dirty classifications | Keep local diagnostics accessors or typed feedback; do not introduce a global diagnostics channel in this line. |

Promotion preconditions checklist:

| Gate | Required before promotion |
|------|---------------------------|
| Style injection | Translator construction accepts a style preset/resolver or render-pipeline factory; no `CounterStylePreset.Default` dependency remains. |
| Viewport source | Viewport source is explicit, typed in physical pixels, and has a clear resize timing contract. |
| Retained-tree ownership | The promoted contract states whether the translator owns `RetainedTree` or receives retained state from a render service. |
| Render-pipeline creation | `RenderPipeline` creation is injected or factory-created with explicit style/options; no hidden sample defaults. |
| Post-frame feedback | Feedback is typed instead of `Action<double>`; max scroll, viewport, content extent, and diagnostics have named fields. |
| Scroll metrics | Scroll feedback identifies the relevant scroll container and can support future multi-container scroll state. |
| Prepare-frame hook | Any prepare-frame callback has a named backend/resource-lifetime purpose, or it is removed before promotion. |
| Diagnostics access | Existing local diagnostics can still be read without introducing a global diagnostics channel. |
| Responsibility boundary | Translator remains patch-to-render-frame glue only; it does not own app messages, input routing, compositor execution, or backend rendering. |
| Regression proof | CLI diagnostics text, debug UI rows, scroll/input behavior, and rendering output have explicit regression coverage for the migration. |
| Migration scope | No promotion happens in this prep line; this checklist is a future gate. |

## 9. Prep Checklist

No code moves in this line. The next useful work is design inventory only:

| Area | Prep question | Current answer |
|------|---------------|----------------|
| Controls | Which node kinds are framework primitives? | `Text`, `Rectangle`, `Button`, and `ScrollContainer` are current primitives. Their factory methods are convenience constructors, not final controls APIs. |
| Controls | Which parts are Counter-specific? | Action-id values, ownership-derived visual state, sample rows, debug rows, and Counter messages. |
| Target/action ids | Which ids must stay distinct? | Hit-test target, control action, focus target, pointer capture target, and app command id are separate design concepts. Current strings remain unchanged. |
| Typed identity | What representation is recommended? | Role-specific string-backed `readonly record struct` wrappers are the recommended first v1 direction; raw strings remain current behavior and generic ids are deferred. |
| Visual state | Who owns `IsHovered` / `IsPressed` / `IsFocused`? | They are current rendering attributes whose values are projected from input ownership state. Future controls may own a typed visual-state projection. |
| Visual state | How does future `ControlVisualState` stay compatible? | Project typed fields back to existing `IsHovered`, `IsPressed`, and `IsFocused` attributes before changing renderer inputs. |
| Input | What is generic today? | Raw platform input facts and raw event publication. |
| Input | What is Counter-specific today? | Routing raw input to Counter messages and wrapping visual-state updates into the Counter model. |
| Scroll | What does `ScrollY` own? | `ScrollY` is the layout/render offset attribute, not scroll animation state. |
| Scroll | What remains outside the node primitive? | `ScrollState`, pump scheduling, system settings, metrics feedback, and app-level scroll storage remain separately owned until extraction contracts are explicit. |
| Scroll | What extraction order is allowed? | Type metrics/feedback first, then pure state/controller; `ScrollFramePump` moves last only after it no longer dispatches Counter messages. |
| Window glue | What is platform-owned? | `IPlatformHost` / `INativeWindow` abstractions and `WindowsPlatformHost` implementation. |
| Window glue | What is bridge-owned later? | A future version of `WindowDrawCommandTranslator`, once style, viewport, retained-tree, and post-frame contracts are named. |
| Window translator | What must be injected before promotion? | Style source, viewport source, retained-tree ownership, render-pipeline creation, post-frame feedback, and scroll metrics. |
| Window translator | What blocks promotion? | Hidden Counter styling, positional max-scroll feedback, implicit retained-tree ownership, and missing regression proof block promotion. |
| Window glue | What remains sample-owned? | `Program.Main` orchestration and `CounterApplication` model/view semantics. |

Regression-only rules remain in force:

- Do not change CLI diagnostics text.
- Do not change debug UI rows.
- Do not enable StyleOnly fast-path.
- Do not move runtime, renderer, input, or backend files during this prep line.
- Do not rename current target/action attributes or public APIs during this prep line.
- Do not introduce unified diagnostics channel / event bus / registry.
