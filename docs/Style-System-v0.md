# Style System v0

> Design contract for style ownership after the renderer foundation. This file defines style categories and invalidation boundaries. It does not introduce a public styling API or theme system.

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
- No StyleOnly layout skip implementation yet.
- No change to current `VirtualNodeProperty` authoring in this document.

## Style Layers

| Layer | Examples | Owner | Invalidation |
|-------|----------|-------|--------------|
| Layout style | Width, height, min/max size, padding, margin, gap, layout direction, line height, font size when it changes text metrics. | Layout/rendering pipeline. | Layout rebuild, then draw recording and possibly composition update. |
| Visual draw style | Fill color, border color, border width, corner radius, text color, non-animated clip materialization. | Drawing/recording pipeline. | Draw command rebuild or partial draw-command update; no layout unless geometry changes. |
| Text shaping style | Font family, weight, style, stretch, size, fallback policy, shaping flags, color glyph policy. | Rendering text pipeline plus platform glyph source. | Text shaping/glyph cache invalidation; layout may rebuild if metrics change. |
| Composition style | Transform, opacity, layer clip, z-order within a composition parent, presented scroll offset, effect parameters. | Composition layer / platform backend. | Compositor property update; no layout or draw rebuild if source content is unchanged. |
| Control-state style | Hover, pressed, focused, disabled, selected, active gesture state. | App/control runtime. | Projects state into layout/visual/composition style; state itself is not rendering-owned. |
| Diagnostic style | Debug overlays, diagnostic text, guard visualization. | Poc/diagnostics. | Output-boundary only; must not become a core style owner. |

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

Visual style can be either draw-recorded or promoted to composition style if the backend can update it without re-recording the content. The v0 rule is conservative: keep draw-command ownership unless the composition contract explicitly says the property is layer-owned.

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

## Control-State Style

Control-state style is a projection from app/control runtime state to layout, visual, text, or composition style. It is not itself a rendering style layer.

Examples:

- Hover changes button fill color: visual style.
- Pressed changes translation or opacity: composition style.
- Focus ring appears: visual or composition style depending on implementation.
- Expanded/collapsed changes height: layout style target plus optional compositor presentation.

`InputOwnershipState`, `ControlVisualState*`, and `ActionHitTestResolver` remain in `Irix.Poc` until the input/control contract is extracted from Counter-specific assumptions.

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

Until then, style changes may still rebuild layout even if a future system could skip it.

## Cross-Platform Notes

The style model must map to D3D12, Vulkan, and Metal without baking in a single API:

- Composition style should be expressed in platform-neutral value types.
- Backend capability flags decide which composition styles can be updated independently.
- Unsupported compositor properties fall back to draw-command update or explicit degradation.
- Style ownership must not depend on Direct2D, D3D11On12, or platform-specific immediate-mode rendering.
