# Animation / Composition

> Design contract for animation ownership. This document separates UI-runtime animation from compositor/GPU animation and uses scroll as the first hybrid case. The internal transform/opacity path can resolve `NodeKey` animation declarations to retained composition targets, publish active layer state into `CompositorHitTestSnapshot`, and publish marker events back to runtime-owned messages through an explicit drain pump; the internal scroll presentation path can resolve retained scroll container `NodeKey` declarations to fixed-clip composition plans. A public animation API does not exist.

## Goals

- Avoid rebuilding `VirtualNode`, layout, and draw commands for compositor-eligible animation ticks.
- Separate logical/app state from presented/composited state.
- Define the minimum animation model that can map to D3D12 now and Vulkan/Metal later.
- Bias the first implementation toward compositor/GPU execution for eligible properties.
- Keep scroll, input, and control state out of `Irix.Rendering` until a runtime owner is chosen.

## Non-Goals

- No public animation API.
- No public timeline scheduler.
- No public style transition API.
- No scroll code extraction from `Irix.Poc`.
- No public scroll animation API.
- No retained-array or snapshot ownership changes.

## Animation Ownership Classes

| Class | Owner | Examples | Requires layout per tick? |
|-------|-------|----------|---------------------------|
| UI-runtime animation | App/runtime/control layer | Width, height, padding, font size, content changes, state-machine transitions, gesture target calculation. | Usually yes or maybe. |
| Compositor animation | Composition layer / backend | Transform, opacity, presented scroll offset, layer clip reveal, simple visual interpolation after layer materialization. | No, if source content is unchanged. |
| Hybrid animation | Runtime target plus compositor presentation | Scroll inertia, snap-to-position, drag presentation, expand/collapse presentation phase. | Target changes may require runtime/layout; presentation ticks should not. |
| Backend-internal animation | Platform backend | GPU fence/timeline resource lifetime, transient upload scheduling, atlas residency work. | Not a UI animation and not exposed as UI state. |

## Runtime vs Presented State

Animated UI state should be split when the property can be presented independently:

| State | Meaning | Owner |
|-------|---------|-------|
| Logical state | Authoritative app/control value used for model update, layout targets, clamp rules, and accessibility. | App/runtime/control. |
| Layout target | Value used by layout to compute geometry and extents. | Rendering/layout pipeline. |
| Presented state | Interpolated value applied to the already-produced visual/layer content. | Compositor/backend. |

A compositor animation must not become the authoritative app model unless a synchronization contract commits the final value back to runtime.

## Property Eligibility

| Property | Preferred owner | Notes |
|----------|-----------------|-------|
| Translation / transform | Compositor | Source content and hit-test mapping must be clear. |
| Opacity | Compositor | Requires layer or material boundary. |
| Presented scroll offset | Compositor | Runtime owns target/clamp; compositor owns interpolation. |
| Clip reveal | Compositor when clip is a layer property | Layout clip still owns logical bounds. |
| Fill/text color | Compositor only after material/layer color and output mapping contracts exist | Otherwise update draw commands. |
| Width/height/padding/font size | UI runtime/layout | Layout target may animate at low frequency; compositor may present a transform but cannot change true layout. |
| Text content/wrapping/fallback | UI runtime/rendering | Not compositor-only. |

Internal style metadata marks opacity and translation properties as composite-eligible. `StyleTransitionCompiler` can precompile a pure internal opacity/translation delta into the existing transform/opacity declaration shape, but the active compositor path still installs declarations only after retained `NodeKey` target resolution. Fill/text/background color remains draw-command-owned until a material/layer color contract and the [Color-Pipeline.md](Color-Pipeline.md) output mapping boundary exist.

## Scroll As Hybrid Animation

Scroll should not remain a pure UI-layer per-frame rebuild long term. The correct model is hybrid:

```text
input delta / gesture
  -> app/control runtime computes targetScrollY
  -> layout observes content extent / maxScrollY
  -> runtime clamps logical scroll target
  -> compositor animates presentedScrollY as layer transform under a clip
  -> runtime receives completion/cancel/final-state events when needed
```

Ownership split:

| Concept | Owner | Notes |
|---------|-------|-------|
| `targetScrollY` / committed logical scroll | App/control runtime. | Used for app state, clamp, accessibility, and future virtualization. |
| `ScrollContainerDiag` / max scroll observation | Rendering/layout. | Published observation only. Rendering does not own scroll state. |
| `ScrollFeedback` | App/control feedback. | Projection from layout diagnostics to runtime. |
| `presentedScrollY` | Compositor. | Interpolated visual offset. Should not require layout rebuild per tick. |
| Scroll hit-test mapping | Input/control adapter plus compositor snapshot. | Pointer coordinates map through the current presented transform in `CompositorHitTestSnapshot`; the committed-state path is used only when no compositor presentation is active. |

The current implementation supports scroll presentation for retained scroll containers by resolving child draw-command runs into one or more fixed-clip composition layers. The compositor moves content by `retainedScrollY - presentedScrollY` while each resolved clip remains fixed; nested/mixed-clip scroll targets are decomposed into ordered `CompositionFrame` layers on the D3D12 path. Hit testing is centralized through `CompositorHitTestSnapshot`, which combines retained hit targets, command-range-backed layer nodes, current presented transforms, fixed clips, and reverse paint-order selection.

Logical scroll target/clamp still belongs to app/control runtime. The PoC runtime interrupt policy is commit/cancel/retarget: commit writes the presented value back to runtime state, cancel discards presentation and returns to the logical target, and retarget uses the presented value only as the visual animation origin while applying new input deltas to the accumulated logical target. Main-app wheel input runs through `ScrollPresentationCoordinator`, while `CompositorLoop` owns the presentation clock, queued compositor-only ticks, idle waiting, and reason-typed cancellation diagnostics. D3D12 marks composition as backend-present paced, so the loop lets `Present(1,0)` provide display cadence instead of adding a software frame cap.

Live wheel input probes the next logical target before cancellation, so repeated top/bottom boundary input does not restart a same-target segment. When a real retarget is needed, sampling and cancellation are serialized on the compositor thread before the replacement segment is installed.

## Implementation Bias

The first implementation cases are D3D12-backed transform/opacity and fixed-clip scroll presentation. They validate compositor state, timing, diagnostics, backend update plumbing, and hit-test remapping without extracting scroll state into `Irix.Rendering`.

Secondary runtime execution is acceptable only when the GPU-first path exposes a concrete blocker. That path must preserve logical state ownership, emit diagnostics showing why compositor execution did not run, and remain secondary to the D3D12-backed path.

## Compositor Animation Contract

Compositor animation entries are expressed as data, not as callbacks into app/runtime each tick. Runtime-facing internal code declares target intent with `CompositionAnimationDeclaration`; the compositor resolves that through `RenderPipelineRetainedInputSnapshot` into a `CompositionAnimationPlan` for a single transform/opacity layer:

| Field | Purpose |
|-------|---------|
| Target layer/visual id | Identifies the retained composition target. |
| Property | Transform, opacity, presented scroll offset, etc. |
| From/to or keyframes | Values in platform-neutral units. |
| Timing | Duration, delay, easing, repeat/cancel policy. |
| Clock domain | Explicit composition clock values. Current internal units are `Stopwatch.GetTimestamp()` ticks carried by `CompositionTimestamp` and `CompositionDuration`, not frame indexes. |
| Commit policy | Whether and when final presented state updates logical runtime state. |
| Cancellation policy | What happens on new input, layout change, or layer destruction. |

The runtime may create, cancel, or retarget animations. The backend/compositor advances compositor animations without requiring a full UI frame rebuild. The main runtime path uses the compositor clock owned by `CompositorLoop`; tests and deterministic diagnostics may call the explicit `RenderCompositionAnimationTickAtAsync` and `RenderCompositionScrollPresentationTickAtAsync` paths with typed timestamps. Normal render pipeline snapshots resolve internal `NodeKey`-addressable `CompositionTarget` and `ScrollCompositionTarget` values that map retained UI nodes to command ranges and stable `CompositionLayerId` values without per-frame target-list allocation. `DrawingBackendCompositor.SetCompositionAnimationDeclaration` and `SetCompositionScrollPresentationDeclaration` install runtime declarations only after the declaration resolves against the retained frame, so the runtime path no longer guesses command ranges. After normal rendering, retained-frame staging, or a compositor-only tick, `DrawingBackendCompositor` publishes a `CompositorHitTestSnapshot`; input routing reads that snapshot instead of recomputing active layer transforms from scattered state.

Future public style transitions should feed semantic style deltas into this internal compiler only after target property metadata, retained target resolution, backend capability, cancellation policy, and runtime commit policy are all explicit. The compiler must not allocate per-frame target lists or let diagnostics become the scheduler.

## Public Transition Authoring Preflight

Public transition authoring describes semantic properties and timing, not retained targets or execution lanes. A future UI layer may express a transition over opacity, translation, background, foreground, size, or state changes with duration, delay, easing, repeat, cancellation, and commit intent. It must not expose `NodeKey` target resolution, `StyleDeltaWork`, `AnimationChannel`, `VisualOnly`, `CompositeOnly`, `StyleOnly`, or backend capability names as authoring choices.

Runtime transition ownership is explicit:

| Responsibility | Owner |
|----------------|-------|
| Start, cancel, retarget, and commit decisions | App/control runtime. |
| Logical state and final committed values | App/control runtime. |
| Layout targets, measurement, and retained publication | Rendering/layout pipeline. |
| Presented interpolation for transform, opacity, and fixed-clip scroll | Compositor/backend. |
| Capability detection and explicit fallback reporting | Backend/compositor boundary. |

`StyleTransitionCompiler` remains a pure internal compiler. It may precompile semantic opacity/translation deltas into `CompositionAnimationDeclaration` data after the runtime supplies target identity, timing, and ownership policy, but it does not schedule, tick, cancel, retarget, commit logical state, resolve themes, or dispatch app messages.

Unsupported or mixed-property transitions fall back before presentation ownership changes. Background/foreground color transition remains draw/update-owned until a material or layer color contract and color-managed output mapping exist; size and text-metric transitions remain runtime/layout-owned; mixed draw plus composition deltas must not silently become compositor-only. Diagnostics may report those decisions, but diagnostics are not a public timeline scheduler.

Current runtime ownership preflight: `IStyleTransitionRuntimeAdapter`, `StyleTransitionRuntimeCoordinator`, `IStyleTransitionCompositorAdapter`, and `IStyleTransitionRetainedSnapshotProvider` live in `Irix.Poc`. The runtime adapter supplies a decision (`Start`, `Cancel`, `Retarget`, or `Commit`) and receives the result; the coordinator compiles only start/retarget decisions, installs compositor declarations only through the compositor adapter, and falls back before presentation ownership changes when the retained snapshot is missing or the compiler rejects a non-compositor-owned delta. The first concrete app/control integration is Counter-owned: `CounterStyleTransitionBridge` maps a single button control-state delta from `OwnershipSnapshot` into a semantic transform/opacity decision, classifies active scroll presentation and multi-target state changes as typed normal-dispatch outcomes, and records that completion/commit still requires an explicit runtime decision rather than happening after start. `StyleTransitionCompletionTracker` is a Poc-owned marker-driven completion tracker for this narrow path: it only tracks `Once` transform/opacity start or retarget decisions with valid animation instances, appends a progress-1 marker, preserves any existing markers, and converts the matching `CompositionAnimationMarkerEvent` into an explicit `Commit` decision that the runtime coordinator must still apply. Loop and alternate timelines are not auto-completed by this slice. `CounterStyleTransitionRuntimeBridge` waits for the app runtime render to publish the retained snapshot before the coordinator installs the declaration. `DrawingBackendCompositor` exposes only an internal transform/opacity animation compositor boundary for this path. There is still no public transition API, theme/cascade resolver, multi-target transition owner, implicit compiler/compositor commit path, or timeline scheduler.

## Animation Markers

Compositor/GPU animations can publish runtime-facing marker events without letting the backend call runtime callbacks. A marker is immutable data attached to the animation declaration:

| Field | Purpose |
|-------|---------|
| `CompositionAnimationMarkerId` | Stable marker identity inside one animation instance. |
| `CompositionRuntimeEventId` | Runtime-owned event key; the runtime maps it to app messages at its own boundary. |
| Trigger | Progress threshold, elapsed time threshold, progress range entry, or explicit every-tick delivery. |
| Repeat policy | Once or once per iteration. |

The declaration carries a `CompositionAnimationInstanceId`; after resolution the plan carries instance, target `NodeKey`, `CompositionLayerId`, and markers. A successful compositor tick evaluates the previous and current `CompositionTimelineSample` interval and appends `CompositionAnimationMarkerEvent` values to the compositor queue. Runtime-facing code drains that queue through `CompositionMarkerEventPump`, maps `CompositionRuntimeEventId` with an app-owned `ICompositionMarkerEventMapper<TMessage>`, and dispatches the resulting messages through the app runtime dispatch sink. `ICompositionDrawingBackend` and `D3D12DrawingBackend` do not know about runtime messages and never invoke runtime delegates.

Marker timing is interval-based, not point-sampled. If progress jumps from `0.2` to `0.8`, markers at `0.3`, `0.5`, and `0.7` are considered crossed even when no rendered tick sampled those exact values. Loop and alternate timelines include iteration and playback direction in the emitted event. Progress-range markers model keyframe/segment entry for the current linear timeline shape; future keyframe structs should compile keyframe markers to the same range-entry trigger. `EveryTick` is explicit and should be treated as presentation sampling/diagnostic delivery, not as a reliable high-level app scheduler.

The first runtime integration slice is internal and PoC-backed: `CounterCompositionMarkerMapper` maps marker runtime event ids to `CounterMessage` values, and `--diagnose-composition-marker-runtime` proves drain, dispatch, unmapped count, final runtime state, and compositor execution count. This is the generic boundary shape; app-specific marker ids and message mapping remain outside `Irix.Rendering`.

## Invalidation Rules

| Event | Required action |
|-------|-----------------|
| Layout target changes | Recompute layout/draw content. Existing compositor animation may be cancelled or retargeted. |
| Visual content changes but layer remains | Update layer content and keep compatible compositor animations. |
| Animated compositor property changes only | Update compositor state; no layout/draw rebuild. |
| Input arrives during animation | Runtime decides whether to retarget, cancel, or commit the presented value. |
| Layer removed | Cancel compositor animations targeting that layer. |
| Backend loses device | Renderer recovery owns GPU resources; runtime owns logical animation state. |

## Hit Testing During Compositor Animation

Transform/opacity compositor animation remaps hit testing by applying the inverse of active layer transforms before testing retained target bounds and clips. The current implementation keeps the authoritative hit-test data in the render pipeline and records retained command ranges on `HitTestTarget`, so only targets inside active composition layers receive inverse-transform mapping. Static targets in the same retained frame keep normal logical hit testing.

Fixed-clip scroll presentation first filters pointer coordinates against the fixed clip in presentation space, then applies the inverse content transform before testing retained target bounds. This keeps clipped-out presented content non-interactive while avoiding a per-tick layout rebuild. Nested layers are inverse-mapped in reverse application order, and clipped presented content must not occlude lower static hit targets.

Runtime interrupt handling is explicit: new input may commit, cancel, or retarget presented scroll state before dispatching the next layout frame. Wheel retarget keeps the visible origin continuous but accumulates distance on the logical target, so rapid N-notch input covers the same total distance as N separate one-notch inputs, including the main-app `EnsureRunning` path where new wheel events arrive while the coordinator loop is already active. Retarget sampling and cancellation are serialized through the compositor loop; app-side code must not read a presented value and then allow an older queued compositor tick to advance before the replacement segment is installed.

Geometry and layout lifecycle invalidation is explicit: `WindowDrawCommandTranslator` publishes a reason-typed `CompositionRenderInvalidation` when viewport, tree, layout, text measurement, or max-scroll feedback changes; `CompositorLoop` clears active scroll presentation before rendering that retained frame. Skipped compositor producer paths record the attempted path, blocker, required/backend capabilities, frame pacing, layers, and command count through optional diagnostics-only status.

## GPU Offload Expectations

The first compositor animation features should be GPU-friendly:

1. Layer transform / translation.
2. Opacity.
3. Presented scroll offset under a clip.
4. Simple clip reveal.

Later features such as blur, shadows, filters, path morphing, or color interpolation need separate material/effect contracts.

## Diagnostics

Animation diagnostics should remain observation, not ownership:

- Active compositor animations by layer/property.
- Runtime logical vs presented values for hybrid animations.
- Cancel/retarget counts.
- Marker drain/dispatch/unmapped counts at the runtime boundary.
- Backend capability skips and unsupported compositor producer reasons.
- Whether an animation ran compositor-only or forced UI frames.

Diagnostics must not become the animation scheduler.
