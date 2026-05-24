# Animation / Composition v0

> Design contract for animation ownership. This document separates UI-runtime animation from compositor/GPU animation and uses scroll as the first hybrid case. It does not implement an animation API.

## Goals

- Avoid rebuilding `VirtualNode`, layout, and draw commands for compositor-eligible animation ticks.
- Separate logical/app state from presented/composited state.
- Define the minimum animation model that can map to D3D12 now and Vulkan/Metal later.
- Keep scroll, input, and control state out of `Irix.Rendering` until a runtime owner is chosen.

## Non-Goals

- No public animation API.
- No timeline scheduler implementation.
- No GPU compositor implementation in this document.
- No scroll code extraction from `Irix.Poc`.
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
| Fill/text color | Compositor only after material/layer color contract exists | Otherwise update draw commands. |
| Width/height/padding/font size | UI runtime/layout | Layout target may animate at low frequency; compositor may present a transform but cannot change true layout. |
| Text content/wrapping/fallback | UI runtime/rendering | Not compositor-only. |

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
| Scroll hit-test mapping | Input/control adapter plus compositor state. | Pointer coordinates must map through current presented transform or use a committed-state fallback. |

v0 does not implement compositor scroll. It only defines the target architecture so the current PoC scroll code is not promoted into the wrong layer.

## Compositor Animation Contract

A future compositor animation entry should be expressed as data, not as a callback into app/runtime each tick:

| Field | Purpose |
|-------|---------|
| Target layer/visual id | Identifies the retained composition target. |
| Property | Transform, opacity, presented scroll offset, etc. |
| From/to or keyframes | Values in platform-neutral units. |
| Timing | Duration, delay, easing, repeat/cancel policy. |
| Clock domain | Runtime clock or backend/compositor clock. |
| Commit policy | Whether and when final presented state updates logical runtime state. |
| Cancellation policy | What happens on new input, layout change, or layer destruction. |

The runtime may create, cancel, or retarget animations. The backend/compositor should advance compositor animations without requiring a full UI frame rebuild.

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

Compositor animation introduces a coordinate mapping problem. v0 rule:

- The authoritative hit-test data remains generated by the render pipeline.
- A future compositor-aware input adapter may map pointer coordinates through current presented transforms before hit testing.
- If no compositor hit-test mapping exists, input should use committed logical state and may cancel/commit animations before dispatch.
- The renderer must not own app/control input state.

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
- Backend capability fallbacks.
- Whether an animation ran compositor-only or forced UI frames.

Diagnostics must not become the animation scheduler.
