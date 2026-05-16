# v1 API / Control Boundary Prep

> Design-only boundary inventory for the next line after Program diagnostics runner split. This document does not move code, change APIs, enable StyleOnly fast-path, or introduce a unified diagnostics channel.
> Status: design inventory complete and regression-only. Controls-boundary helpers are complete and regression-only: `ControlVisualState projection helper`, `Control action property helper`, and `Button property bundle helper` are implemented as PoC-owned internal code. Do not continue controls-boundary helper splitting.

## 1. Scope

Current status:

- This design inventory is complete and sealed as regression-only.
- Controls-boundary helpers are complete and regression-only.
- `ControlVisualState projection helper`, `Control action property helper`, and `Button property bundle helper` are implemented in `Irix.Poc` and remain PoC-owned.
- `ScrollFeedback` / `ScrollContainerMetrics` vocabulary v0 is implemented in `Irix.Poc` as side-channel translator feedback; the legacy `Action<double>` max-scroll callback remains unchanged.
- Program diagnostics runner split is complete and regression-only.
- Diagnostics snapshot v0 and debug UI bridge v0 remain regression-only.
- Unified diagnostics channel / event bus / registry remains postponed.
- StyleOnly layout skipping remains postponed; `StyleOnlyFastPathOptions` is only an internal/default-off pre-switch over the guarded selected render-source path.

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
- Do not continue splitting this document line; use it as the closed inventory for the next code line.

## 2. Controls Boundary

Current controls are represented as `VirtualNodeKind` values plus loosely typed content/properties. `VirtualNodeFactory` is a convenience layer over those primitives, not a final v1 controls API.

| Current surface | Current owner | Boundary conclusion |
|-----------------|---------------|---------------------|
| `VirtualNodeKind.Text` / `VirtualNodeFactory.Text` | `Irix.Core` node model; consumed by `Irix.Rendering` layout/draw recording | Framework primitive. Text content is core node data; layout and rendering own measurement/recording policy. |
| `VirtualNodeKind.Rectangle` / `VirtualNodeFactory.Rectangle` | `Irix.Core` node model with `Width` / `Height` properties; consumed by `Irix.Rendering` | Framework primitive / demo drawing primitive. The factory is a low-level convenience for setting size properties, not Counter-specific. |
| `VirtualNodeKind.Button` / `VirtualNodeFactory.Button` | `Irix.Core` node kind; `Irix.Rendering` consumes child text plus `ActionId`, `IsHovered`, `IsPressed`, `IsFocused` properties | Framework primitive in the current renderer, but not a final controls API. The generic button shape and visual-state properties are reusable; Counter action ids and message mapping are sample-owned. |
| `VirtualNodeKind.ScrollContainer` / `VirtualNodeFactory.ScrollContainer` | `Irix.Core` node kind; `Irix.Rendering` consumes `ScrollY` and optional `Height` properties | Framework layout primitive. Scroll geometry/clip behavior belongs to rendering; scroll input policy and animation remain outside the node primitive. |

Counter sample convenience lives outside the primitive factory:

| Counter-specific surface | Why it is sample-owned |
|--------------------------|------------------------|
| `CounterApplication.BuildButton` | Attaches Counter action ids and derives visual-state properties from `OwnershipSnapshot`. |
| `CounterMessage.Increment` / `Decrement` / `Reset` action ids | These are app commands, not framework command identifiers. |
| `CounterApplication.BuildScrollProbeRows` | Generates sample content to exercise scrolling. |
| Debug header rows in `CounterApplication` | Sample diagnostics presentation, not a reusable control surface. |

Naming and ownership conclusion:

- Keep `VirtualNodeFactory` as a low-level node-construction convenience for now.
- Treat `Button` as a current framework primitive, not yet a complete controls API.
- Treat property names (`ActionId`, `IsHovered`, `IsPressed`, `IsFocused`, `ScrollY`, `Width`, `Height`) as the current wire contract between `VirtualNode` and rendering.
- Treat property values and action-id vocabulary as app-owned unless the value is a generic rendering/layout concept.
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
| `ControlActionId` | The activation identifier attached to a control, such as a button action. It answers "what control action should activation request?" | Current `ActionId` property on `Button` nodes | Control/view boundary. The property name stays as-is for now; future API can type this concept. |
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
- Keep the current `ActionId` property and string target ids unchanged during this prep line.
- Treat Counter command ids as sample vocabulary until an app command abstraction is explicitly designed.

Typed identity prep:

| Option | Fit for v1 | Notes |
|--------|------------|-------|
| Bare `string` | Current implementation only | Easy to pass through `VirtualNodeProperty`, but it lets hit-test, focus, capture, action, and app command identities be mixed accidentally. Keep for existing wire compatibility, not as the recommended promoted API shape. |
| Role-specific string wrapper | Recommended direction | Prefer small role-specific value types such as `HitTestTargetId`, `ControlActionId`, `FocusTargetId`, `PointerCaptureTargetId`, and `AppCommandId` that wrap the current string payload. This preserves current id vocabulary while making API boundaries explicit. |
| Shared generic id, such as `Id<TScope>` | Not first choice for v1 | It can reduce duplicate code, but it hides the domain vocabulary at call sites and makes public APIs harder to read. Reconsider only after role-specific wrappers prove too repetitive. |
| Numeric/runtime handle | Not appropriate yet | Hit-test and control action ids are currently authored as stable strings in the view tree. A numeric handle would require lifetime, recycling, and diagnostics naming rules that do not exist yet. |

Recommendation: when this becomes implementation work, prefer `readonly record struct`-style role wrappers over strings, with explicit conversion at the current property/hit-test boundary. Do not introduce a generic id abstraction before the roles are stable. Default/empty identity semantics must be designed before any wrapper becomes public API.

## 6. Visual State Ownership

`IsHovered`, `IsPressed`, and `IsFocused` are currently `VirtualNode` properties consumed by rendering. They should be treated as renderable visual-state inputs produced from input ownership state, not as independent Counter domain state and not as renderer-owned interaction state.

| Property | Current storage | Current producer | Current consumer | Boundary conclusion |
|-----------|-----------------|------------------|------------------|---------------------|
| `IsHovered` | Boolean `VirtualNodeProperty` on button-like nodes | `CounterApplication` derives it from `OwnershipSnapshot` | `Irix.Rendering` style/layout/draw recording | Visual state projection from input ownership. Rendering may style it, but should not define hover policy. |
| `IsPressed` | Boolean `VirtualNodeProperty` on button-like nodes | `CounterApplication` derives it from pressed/capture ownership | `Irix.Rendering` style/layout/draw recording | Visual state projection from pointer capture/press state. It is not the app command itself. |
| `IsFocused` | Boolean `VirtualNodeProperty` on button-like nodes | `CounterApplication` derives it from focused ownership | `Irix.Rendering` style/layout/draw recording | Visual state projection from focus ownership. It is not keyboard routing policy by itself. |

Future Button API boundary:

- Rendering owns how visual-state properties affect drawing, style selection, and layout invalidation.
- Input ownership owns hover, focus, press, and capture facts.
- A future controls layer may own a typed `ControlVisualState` projection from input ownership into renderable properties.
- App code owns command handling and model updates after activation; it should not need to hand-roll hover/press/focus projection once a generic controls layer exists.
- Until that layer exists, `CounterApplication` remains the projection site and the existing properties remain the wire contract.

Design answer for `Button` v1 prep: the three properties are current rendering properties whose values are produced by input-state projection. They are likely future control visual state, but they are not Counter app state and should not be computed inside the renderer.

`ControlVisualState` prep:

| Draft field | Source | Projection rule | Current compatibility |
|-------------|--------|-----------------|-----------------------|
| `IsHovered` | Current hover target from input ownership | True when the control target is the current hover target and not blocked by capture/focus policy. | Writes the existing `IsHovered` property. |
| `IsPressed` | Current pointer capture / pressed target from input ownership | True while the control owns the active press/capture sequence. | Writes the existing `IsPressed` property. |
| `IsFocused` | Current focus target from input ownership | True when keyboard focus belongs to the control target. | Writes the existing `IsFocused` property. |
| `IsEnabled` | Future control/app policy | Default would be enabled unless app/control state says otherwise. | No current property; do not add during this prep line. |

Projection location:

- Input ownership should expose facts such as hover target, focus target, and pointer capture target.
- A future controls layer should project those facts plus control policy into `ControlVisualState`.
- Rendering should consume the projected state as style/layout input; it should not compute hover, focus, or capture rules.
- App code should receive activation results and own command handling, but it should not manually derive the common hover/press/focus properties once a controls layer exists.

Compatibility rule: the future `ControlVisualState` must remain source-compatible with existing `IsHovered`, `IsPressed`, and `IsFocused` properties until the renderer has a typed state input. The first implementation step should be an adapter that emits the existing properties, not a renderer contract change.

## 7. Scroll Ownership

Scroll currently spans node properties, layout feedback, input conversion, animation, and sample state. The v1 boundary should keep these roles separate before extracting a reusable scroll primitive.

| Scroll surface | Current owner | Boundary conclusion |
|----------------|---------------|---------------------|
| `ScrollY` property | `VirtualNode` wire contract consumed by `Irix.Rendering` | Layout/render input. It is the pixel offset used for layout and clipping, not the animation state. |
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

Scroll extraction preconditions:

| Step | Candidate extraction | Allowed only after | Blocking issue if skipped |
|------|----------------------|--------------------|---------------------------|
| 1 | `ScrollMetrics` and max-scroll feedback vocabulary | `WindowDrawCommandTranslator` feedback names container id, viewport extent, content extent, and max scroll explicitly. | Complete as side-channel `ScrollFeedback` / `ScrollContainerMetrics`; legacy `Action<double>` still drives runtime behavior. |
| 2 | `SystemScrollSettings` provider | Platform ownership for wheel lines/chars is named, with defaults preserved for non-Windows or missing settings. | Design tracked in [V1-Scroll-Settings-Provider-Prep.md](V1-Scroll-Settings-Provider-Prep.md); no provider wiring or delta conversion change. |
| 3 | Pure `ScrollController` | Metrics and settings are explicit inputs, and controller messages no longer mention Counter types. | The controller would look reusable while still depending on sample message flow and single-container assumptions. |
| 4 | `ScrollState` | Scroll container identity, app storage ownership, and default/empty state semantics are named. | State extraction would force a framework decision about where per-container scroll state lives before the model boundary is ready. |
| 5 | `ScrollFramePump` or scheduler | Frame request/cancellation semantics are independent from `CounterMessage.ScrollFrame`. | The pump would become runtime infrastructure while still dispatching Counter-specific messages. |

Extraction order is fixed for future work:

1. Define metrics / feedback vocabulary.
2. Extract settings provider.
3. Extract pure controller logic.
4. Decide state ownership.
5. Extract pump / scheduler last.

Until those gates are met, keep `ScrollController`, `ScrollState`, and `ScrollFramePump` together in `Irix.Poc`.

## 8. Window Translator Contract Draft

`WindowDrawCommandTranslator` is the likely future render/platform bridge candidate, but promotion requires an explicit contract. The current type creates `RenderPipeline` through an internal `TranslatorRenderPipelineFactory` seam that defaults to `CounterStylePreset.Default`, pulls viewport data from a window/provider, owns a retained tree, and reports max scroll through an `Action<double>` callback plus `LastScrollFeedback` side-channel. The active follow-up draft is [V1-Translator-Feedback-Contract-Prep.md](V1-Translator-Feedback-Contract-Prep.md).

If promoted later, the contract should inject or name these dependencies explicitly:

| Contract part | Current shape | Future boundary requirement |
|---------------|---------------|-----------------------------|
| Style source | Internal factory defaults to `CounterStylePreset.Default` | Inject a style preset/resolver/pipeline factory before promotion. A framework translator must not depend on Counter styling. |
| Viewport source | `INativeWindow.Region.PhysicalBounds` or optional `Func<PixelRectangle>` | Inject a viewport source with clear pixel units and resize timing. The translator should not assume one native window shape. |
| Retained tree owner | Private `RetainedTree` inside the translator | Decide whether the bridge owns retained state or receives it from a higher render service. Patch application ownership must be explicit. |
| Render pipeline | Private `RenderPipeline` instance created by internal `TranslatorRenderPipelineFactory` | Promotion still needs explicit style/options and framework-owned factory shape. Avoid hidden sample defaults. |
| Prepare-frame hook | Optional `Action? prepareFrame` | Keep as a named frame-preparation hook only if the backend/resource lifetime contract needs it. |
| Post-frame feedback | Optional `Action<double>` receiving max scroll, plus `LastScrollFeedback` side-channel | Keep runtime callback unchanged for now. Promotion still needs a typed feedback contract that includes layout diagnostics, not only scroll metrics. |
| Scroll metrics | `LastMaxScrollY`, current layout viewport exposure, and `LastScrollFeedback` side-channel | Side-channel now names max scroll, viewport extent, content extent, and container identity. It is not yet a promoted framework contract. |
| Diagnostics exposure | `LastViewport`, `LastLayoutViewport`, rebuild counts, dirty classifications | Keep local diagnostics accessors or typed feedback; do not introduce a global diagnostics channel in this line. |

Promotion preconditions:

- Style injection no longer references Counter defaults.
- Viewport, retained-tree ownership, render-pipeline creation, and post-frame feedback are named independently.
- Scroll feedback is typed before it is shared with a generic scroll primitive.
- The translator remains a patch-to-render-frame bridge; it should not own app messages, input routing, or backend rendering execution.
- No migration happens in this prep line.

Promotion checklist:

| Gate | Requirement | Status in current PoC |
|------|-------------|-----------------------|
| Style injection | Style preset/resolver or pipeline factory is injected by the composition root. | Partial: internal factory seam exists; default remains `CounterStylePreset.Default`. |
| Viewport source | Viewport provider contract names pixel units, resize timing, and fallback behavior. | Partial: optional `Func<PixelRectangle>` exists, but it is not a named framework contract. |
| Retained-tree ownership | Patch application and retained tree lifetime are assigned to either the bridge or a higher render service. | Blocked: private retained tree is embedded without an external ownership contract. |
| Render-pipeline creation | Pipeline construction takes explicit style/options and has no sample defaults. | Partial: internal factory creates the pipeline; promoted factory/options contract is still absent. |
| Prepare-frame hook | Backend/resource preparation needs are named, or the hook is removed. | Partial: optional action exists but has no semantic contract. |
| Post-frame feedback | Feedback is a typed result carrying layout diagnostics and scroll metrics. | Partial: scroll metrics side-channel exists, but runtime still uses `Action<double>` and no promoted feedback contract exists. |
| Scroll metrics | Feedback names scroll container id, viewport extent, content extent, and max scroll. | Partial: `LastScrollFeedback` side-channel exists in PoC; extraction and promotion remain postponed. |
| Diagnostics access | Local diagnostics stay local or are returned in typed feedback. | Partial: accessors exist; no global diagnostics channel should be introduced here. |
| App isolation | Translator has no Counter messages, input router, or app command dependency. | Mostly satisfied, except style and scroll feedback remain sample-shaped. |

Migration rule: do not move `WindowDrawCommandTranslator` until every blocked gate has either a concrete contract or an explicit decision to keep it sample-owned.

## 9. Implementation Candidates

Design inventory is complete. The next work should be a small implementation line, with all broader extraction candidates held behind explicit gates.

| Priority | Candidate | Scope | Decision |
|----------|-----------|-------|----------|
| P0 implemented | `ControlVisualState projection helper` | Small PoC-owned helper projects existing input ownership facts into the current `IsHovered`, `IsPressed`, and `IsFocused` properties. Existing properties and behavior remain unchanged. | Done as internal `Irix.Poc` code. Keep target/action ids as strings; do not promote this to a framework controls API yet. |
| P0 implemented | `Control action property helper` | Small PoC-owned helper constructs the current string-backed `ActionId` property. | Done as internal `Irix.Poc` code. Keep target/action ids as strings; do not introduce typed id wrappers. |
| P1 implemented | `Button property bundle helper` | Small PoC-owned helper combines `ActionId` plus `ControlVisualState` properties for `CounterApplication.BuildButton`. | Done as internal `Irix.Poc` code. It reduces hand-written wire contract in the sample without changing the `VirtualNode` contract. |
| P1 complete | Controls-boundary helper seal | Static scan found no remaining raw `ActionId` construction in `src/**/*.cs`; focused tests cover bundle output and `BuildView` integration. | Complete / regression-only. Do not continue controls-boundary helper splitting. |
| P2 postponed | Typed identity wrappers | Future role-specific wrappers for `HitTestTargetId`, `ControlActionId`, `FocusTargetId`, `PointerCaptureTargetId`, and `AppCommandId`. | Do not implement yet. Keep current string ids until the control helper boundary is explicitly promoted. |
| P3 first step complete | Scroll feedback vocabulary | PoC-owned `ScrollFeedback` / `ScrollContainerMetrics` records name scroll container id, viewport extent, content extent, and max scroll. | Complete as translator side-channel only. Do not extract scroll yet; settings provider, pure controller, state ownership, and pump/scheduler work remain postponed. |
| P3 design draft active | Translator options / feedback records | Future contract records for style source, viewport source, retained-tree ownership, render-pipeline creation, post-frame feedback, and local diagnostics. | Draft is tracked in [V1-Translator-Feedback-Contract-Prep.md](V1-Translator-Feedback-Contract-Prep.md). Do not promote `WindowDrawCommandTranslator` yet. Use the promotion checklist as the migration gate. |
| P3 parked | `StyleOnly fast-path implementation` | Future render pipeline optimization. | Internal/default-off pre-switch exists; actual layout skip/default enablement remains postponed, with no `RenderPipeline.Build` behavior change in this line. |

Implemented helper line:

1. `ControlVisualState projection helper` exists as PoC-owned internal code.
2. `Control action property helper` exists as PoC-owned internal code.
3. `Button property bundle helper` exists as PoC-owned internal code.
4. Existing `ActionId`, `IsHovered`, `IsPressed`, and `IsFocused` properties remain the compatibility output.
5. Target/action ids remain strings.
6. Typed id wrappers remain postponed.
7. Scroll feedback vocabulary v0 exists as side-channel translator data.
8. Scroll controller, state ownership, settings provider implementation, and pump/scheduler remain unextracted; settings provider design is tracked separately.
9. `WindowDrawCommandTranslator` remains unpromoted.
10. StyleOnly fast-path remains disabled.

Next chosen low-risk line: [translator feedback contract draft](V1-Translator-Feedback-Contract-Prep.md). Do not continue controls-boundary helper splitting, and do not use this line as permission to start typed ids, scroll extraction, translator promotion, or StyleOnly fast-path.

## 10. Prep Checklist

No code moves in this line. The design inventory is complete and regression-only:

| Area | Prep question | Current answer |
|------|---------------|----------------|
| Controls | Which node kinds are framework primitives? | `Text`, `Rectangle`, `Button`, and `ScrollContainer` are current primitives. Their factory methods are convenience constructors, not final controls APIs. |
| Controls | Which parts are Counter-specific? | Action-id values, ownership-derived visual state, sample rows, debug rows, and Counter messages. |
| Boundary prep | Is this document line still active design work? | No. Design inventory is complete; only regression repairs should touch this line. |
| Implemented helpers | What shipped first? | `ControlVisualState projection helper`, `Control action property helper`, and `Button property bundle helper`, preserving existing button properties and behavior as PoC-owned internal code. |
| Controls helper seal | Is there remaining helper split work? | No. Static scan found no raw `ActionId` construction in PoC source; helper line is complete / regression-only. |
| Target/action ids | Which ids must stay distinct? | Hit-test target, control action, focus target, pointer capture target, and app command id are separate design concepts. Current strings remain unchanged. |
| Typed identity | What representation is preferred later? | Role-specific string wrappers, preferably `readonly record struct`-style value types. Do not start with bare strings or a shared generic id as the promoted API shape. |
| Visual state | Who owns `IsHovered` / `IsPressed` / `IsFocused`? | They are current rendering properties whose values are projected from input ownership state. Future controls may own a typed visual-state projection. |
| Control visual state | What should `ControlVisualState` contain first? | Hovered, pressed, and focused fields mapped back to existing properties; enabled can be designed later without changing current properties now. |
| Input | What is generic today? | Raw platform input facts and raw event publication. |
| Input | What is Counter-specific today? | Routing raw input to Counter messages and wrapping visual-state updates into the Counter model. |
| Scroll | What does `ScrollY` own? | `ScrollY` is the layout/render offset property, not scroll animation state. |
| Scroll | What remains outside the node primitive? | `ScrollState`, pump scheduling, system settings, metrics feedback, and app-level scroll storage remain separately owned until extraction contracts are explicit. |
| Scroll feedback | What first step is complete? | `ScrollFeedback` / `ScrollContainerMetrics` side-channel vocabulary exists in `Irix.Poc`; old max-scroll callback remains active. |
| Scroll extraction | What order prevents premature movement? | Metrics / feedback vocabulary first is complete; settings provider second; pure controller third; state ownership fourth; pump / scheduler last. |
| Window glue | What is platform-owned? | `IPlatformHost` / `INativeWindow` abstractions and `WindowsPlatformHost` implementation. |
| Window glue | What is bridge-owned later? | A future version of `WindowDrawCommandTranslator`, once style, viewport, retained-tree, and post-frame contracts are named. |
| Window translator | What must be injected before promotion? | Style source, viewport source, retained-tree ownership, render-pipeline creation, post-frame feedback, and scroll metrics. |
| Translator promotion | What blocks movement today? | Counter style construction, private retained-tree ownership, positional max-scroll callback, incomplete viewport contract, and untyped feedback. |
| Window glue | What remains sample-owned? | `Program.Main` orchestration and `CounterApplication` model/view semantics. |

Regression-only rules remain in force:

- Treat this document as sealed design inventory; do not continue scope discovery here unless a regression in the documented boundary is found.
- Do not change CLI diagnostics text.
- Do not change debug UI rows.
- Do not enable StyleOnly fast-path.
- Do not move runtime, renderer, input, or backend files during this prep line.
- Do not rename current target/action properties or public APIs during this prep line.
- Do not continue controls-boundary helper splitting.
- Do not implement typed id wrappers, scroll extraction, or translator promotion as part of this sealed controls-boundary line.
- Do not move `ScrollController`, `ScrollState`, or `ScrollFramePump`; scheduler/message flow remains PoC-owned and unchanged.
- Do not introduce unified diagnostics channel / event bus / registry.
