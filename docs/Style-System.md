# Style System

> Design contract for style ownership after the renderer foundation. This file defines internal style categories and invalidation boundaries. It does not introduce a public styling API or theme system.

## Goals

- Separate layout-affecting style from visual/composition style.
- Define which style changes require layout, draw recording, text shaping, or compositor-only updates.
- Make future animation decisions property-driven instead of ad hoc.
- Keep the current Windows/D3D12 implementation usable while preserving a path to Vulkan/Metal backends.
- Avoid turning `Irix.Rendering` into an app/control state owner.

## Non-Goals

- No CSS-compatible cascade.
- No theme/resource dictionary implementation.
- No public animation API.
- No cross-platform backend implementation.
- No `RenderPipeline.Build` StyleOnly layout-skip implementation yet. Retained partial apply and selected render-source handoff exist after normal publication; they are not layout skip.
- No public UI style authoring surface. Public UI code should not be asked to label a property as layout, visual, text, or composition style; those categories are internal classification results.
- No HDR color output or tone-mapping implementation in the current style slice. Color output policy belongs to the compositor/backend output mapping context.

## Style Layers

| Layer | Examples | Owner | Invalidation |
|-------|----------|-------|--------------|
| Layout style | Width, height, min/max size, padding, margin, gap, layout direction, line height, font size when it changes text metrics. | Layout/rendering pipeline. | Layout rebuild, then draw recording and possibly composition update. |
| Visual draw style | Fill color, border color, border width, corner radius, text color, non-animated clip materialization. | Drawing/recording pipeline. | Draw command rebuild or partial draw-command update; no layout unless geometry changes. |
| Text shaping style | Font family, weight, style, stretch, size, fallback policy, shaping flags, color glyph policy. | Rendering text pipeline plus platform glyph source. | Text shaping/glyph cache invalidation; layout may rebuild if metrics change. |
| Composition style | Transform, opacity, layer clip, z-order within a composition parent, presented scroll offset, effect parameters. | Composition layer / platform backend. | Compositor property update; no layout or draw rebuild if source content is unchanged. |
| Control-state style | Hover, pressed, focused, disabled, selected, active gesture state. | App/control runtime. | Projects state into layout/visual/composition style; state itself is not rendering-owned. |
| Diagnostic style | Debug UI surfaces, diagnostic text, guard visualization. | Poc/diagnostics. | Output-boundary only; must not become a core style owner. |

## Authoring Boundary

The current low-level `VirtualNodeProperty` style entries are internal IR for renderer classification, not the final public UI styling API. Public UI style should remain semantic: component variants, state styles, tokens, and modifiers can describe properties such as width, background, foreground, opacity, or translation without exposing `StyleOnly`, `LayoutAffecting`, `VisualOnly`, or `CompositeOnly`.

Internally, style values stay compact and typed. The current pre-public-API slice adds an internal semantic declaration layer: `StylePropertyId`, `StyleValue`, `StyleDeclaration`, and `StyleDeclarationMapper`. It lets app/control adapters express width, height, background, foreground, opacity, translation, and control-state style terms, then maps one-way into the existing `VirtualNodeProperty` IR for classification. This layer does not store or expose `StyleEffect`, `AnimationChannel`, `StyleDeltaWork`, invalidation kinds, or layout rebuild reasons. Visual color values use canonical `Color` through `StyleColor`; the color value direction is defined in [Color-Pipeline.md](Color-Pipeline.md): standard Irix `Color` canonicalizes authoring input into an ideal linear BT.2020 / Rec.2020 straight-alpha value and does not retain source color-space metadata. `StyleDeltaPlanner` turns changed internal properties into explicit work flags for layout, text measure, draw, composition, and control-state projection, so future optimizers do not need to infer execution policy from public API names.

Style owns the semantic property value. It does not own SDR/HDR mode, tone mapping, system SDR brightness, Rec.2100 HLG/PQ selection, swapchain format, or per-screen color-profile mapping. Those belong to material/compositor/backend output mapping after the draw or composition payload has been produced.

## Public Authoring Contract Preflight

This preflight defines the future public authoring boundary without adding the public API. Public authoring terms stay semantic: a UI layer may describe component variants, state styles, modifiers, and transitions over concepts such as size, background, foreground, opacity, and translation. It must not ask callers to pick renderer processing layers.

Forbidden public authoring names: `LayoutAffecting`, `VisualOnly`, `CompositeOnly`, `StyleOnly`, `StyleEffect`, `AnimationChannel`, `StyleDeltaWork`, `StyleDeltaPlan`, `StyleDeltaPlanner`, and `StyleTransitionCompiler`. These names can appear in internal renderer code and diagnostics, but they are not user-facing style concepts.

Semantic-to-internal mapping remains one-way. The implemented internal `StyleDeclaration` layer is the current pre-public bridge for this mapping; it is not a public authoring API.

| Future semantic authoring term | Internal target |
|--------------------------------|-----------------|
| Size and spacing requests | Layout metadata, then normal layout publication. |
| Background and foreground colors | Internal visual color properties and draw payload updates. |
| Opacity and translation | Internal composition properties and transform/opacity declaration precompile when eligible. |
| Hover, pressed, focused, disabled, selected | Runtime/control-state projection before renderer classification. |
| Transition timing/easing over compositor-eligible values | Internal `StyleTransitionCompiler` preflight only after runtime ownership policy is supplied. |

Internal classification remains the renderer boundary. The UI-facing layer should pass semantic style deltas down; `VirtualPropertyMetadata`, `StyleDeltaPlanner`, retained partial apply, and backend capability checks decide whether the result needs layout, draw recording, composition update, or fallback. A public style declaration must not store or echo internal invalidation categories as part of its authoring contract.

No theme, resource dictionary, CSS-like cascade, or scheduler is introduced by this preflight. Those are separate design decisions and require ownership rules for resolution order, lifetime, invalidation, and runtime scheduling before code is promoted.

## Invalidation Rules

| Change | Required work | Notes |
|--------|---------------|-------|
| Layout style changed | Layout dirty classification and layout rebuild. | StyleOnly skip remains design-only until retained layout ownership is proven. |
| Visual style changed | Re-record affected draw commands or update visual command payload. | Does not require layout when geometry and text metrics are unchanged. |
| Composition style changed | Update composition layer properties. | Preferred path for transform/opacity/scroll presentation animation. |
| Text shaping style changed | Re-shape text and update glyph/cache dependencies. | May require layout if metrics, line height, or run segmentation change. |
| Control-state changed | Runtime/control projection decides which style layer changed. | Hover color can be visual-only; pressed size would be layout-affecting. |
| Diagnostic style changed | Update diagnostics output only. | Must not drive app/runtime state. |

## Layout Style

Layout style is the set of properties that changes measured size, position, line breaking, or layout-tree topology. Examples:

- Explicit or implicit width/height.
- Padding, margin, gap, alignment, layout direction.
- Text metrics: font size, line height, wrapping mode, paragraph constraints.
- Properties that change scroll extent or clipping geometry.

A layout-style change is not eligible for compositor-only animation. It may still have a compositor presentation phase after layout produces a new target, but the target computation is a runtime/layout responsibility.

## Visual Style

Visual style changes pixels but not layout. Examples:

- Fill color and text color.
- Border color and possibly border width when border width is not part of layout metrics.
- Corner radius when it does not alter layout bounds.
- Non-animated visual clip parameters already reflected in command clip bounds.

Visual style can be either draw-recorded or promoted to composition style if the backend can update it without re-recording the content. The current rule is conservative: keep draw-command ownership unless the composition contract explicitly says the property is layer-owned.

Current implementation: internal semantic background/foreground declarations map to background/foreground color properties, which can override rectangle, button, and text draw-command colors. They are classified as visual-only and keep layout geometry, clips, and hit targets stable, but `RenderPipeline.Build` still performs the normal full layout publication when a dirty patch asks it to rebuild.

Current color implementation stage: style/property color values use canonical linear BT.2020 `Color` internally and still downgrade to the active SDR/sRGB draw-command representation at the draw-recording boundary. This preserves the current renderer while leaving draw/material payload migration and HDR output mapping as future work.

## Text Shaping Style

Text style needs its own category because it can affect both layout and glyph cache state.

| Property type | Effect |
|---------------|--------|
| Font family/fallback | Changes face selection, glyph indices, metrics, atlas keys, and fallback runs. |
| Font size/line height | Changes metrics and layout. |
| Font weight/style/stretch | Changes face selection and glyph atlas keys; may change metrics. |
| Color glyph policy | Changes atlas format and shader path but not necessarily layout. |
| Text color | Visual style only when glyph shape/metrics stay the same. |

DirectWrite remains a source-data path for shaping, fallback, metrics, raster, and glyph image data. It is not a style owner and must not become a final composition path.

## Composition Style

Composition style is the subset of visual state that can be applied after draw-command content is produced.

Initial compositor-eligible properties:

- 2D translation / transform.
- Opacity.
- Clip rectangle for layer presentation.
- Presented scroll offset.
- Layer visibility.
- Simple color modulation only after the backend has a stable layer/color contract.

Composition style must not require rebuilding `VirtualNode`, layout, or draw command buffers for every animation tick. It is the main mechanism for GPU/off-main-pipeline animation.

Current implementation: internal opacity and translation declarations map to composition metadata, classify as composite-only, and identify compositor-eligible property intent. `StyleTransitionCompiler` can compile a pure internal opacity/translation delta into the existing `CompositionAnimationDeclaration` shape. It does not schedule transitions, resolve public style rules, commit runtime state, or bypass retained target validation; existing compositor execution remains driven by resolved transform/opacity and scroll presentation declarations. The narrow Poc-owned `StyleTransitionRuntimeCoordinator` proves the future ownership shape: runtime supplies start/cancel/retarget/commit decisions, retained snapshots are required before compositor install, and rejected draw/layout-owned deltas fall back before presentation ownership changes. Counter now has the first app/control integration and lifecycle preflight slice: `CounterStyleTransitionBridge` maps a single button `OwnershipSnapshot` state delta through semantic opacity/translation declarations, classifies active scroll presentation and multi-target state changes as normal app dispatch, and marks started transitions as requiring an explicit runtime completion decision. Unsupported active-scroll or multi-target fallback now carries an explicit presentation policy: before normal dispatch publishes app-owned state, the Poc path aborts any active style transition presentation and clears the tracker. `StyleTransitionCompletionTracker` is the first narrow completion bridge: for `Once` transform/opacity transitions it appends a progress-1 marker, tracks the installed target/instance after runtime start or retarget succeeds, and turns the matching compositor marker event into an explicit `Commit` decision. `StyleTransitionCompletionPump` wires that bridge into the Counter main-app path by rendering transform/opacity compositor ticks, draining marker events, and applying the explicit commit through the coordinator; it remains Poc-owned and is not a public or generic scheduler. The runtime bridge waits for app render publication before coordinator install. There is still no generic scheduler, implicit compiler/compositor commit, public style transition API, theme/cascade, loop/alternate auto-completion, or true concurrent multi-owner transition owner.

## Control-State Style

Control-state style is a projection from app/control runtime state to layout, visual, text, or composition style. It is not itself a rendering style layer.

Examples:

- Hover changes button fill color: visual style.
- Pressed changes translation or opacity: composition style.
- Focus ring appears: visual or composition style depending on implementation.
- Expanded/collapsed changes height: layout style target plus optional compositor presentation.

`InputOwnershipState`, `ControlVisualState*`, and the current hit-test service/resolver adapters remain in `Irix.Poc` until the input/control contract is extracted from Counter-specific assumptions.

## Style And Animation

Animation eligibility is determined by the target property category:

| Target property | Preferred animation owner |
|-----------------|---------------------------|
| Transform, opacity, presented scroll offset | Compositor animation. |
| Fill/text color | Compositor if layer/material contract exists; otherwise UI runtime/draw update. |
| Width, height, padding, font size | UI runtime/layout animation target. |
| Text content, font fallback, wrapping | UI runtime/rendering; not compositor-only. |
| Control state transitions | Runtime owns state transition; compositor may own visual interpolation. |

## StyleOnly Dirty Classification

`StyleOnly` remains a planning boundary. Before implementing layout skip, the framework needs:

1. A stable classification of layout vs visual vs composition style.
2. Proof that retained layout/result publication is safe to reuse.
3. A way to update draw commands or composition properties without invalidating layout.
4. Tests proving hit targets, clips, and diagnostics remain consistent.

Until then, style changes may still rebuild layout even if a future system could skip it. Poc partial apply may still reduce retained-frame execution after the rebuild when the guarded selected render-source path accepts the current publication.

The immediate contract is therefore: internal semantic style declarations map into renderer IR, deltas are classified cheaply through metadata, visual color deltas can update draw payloads, and pure composition deltas can be precompiled to existing transform/opacity declarations. None of those facts imply a layout-skip branch, public style API, public transition API, or runtime transition scheduler.

## Cross-Platform Notes

The style model must map to D3D12, Vulkan, and Metal without baking in a single API:

- Composition style should be expressed in platform-neutral value types.
- Backend capability flags decide which composition styles can be updated independently.
- Unsupported compositor properties fall back to draw-command update or explicit degradation.
- Style ownership must not depend on platform-specific immediate-mode rendering.
