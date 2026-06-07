# Irix Color Pipeline

> Design contract for the future Irix color value and output mapping pipeline. This document fixes the color-space direction without implementing the HDR backend path yet.

## Decision

The standard Irix `Color` value represents an ideal BT.2020 / Rec.2020 color value in linear light with straight alpha.

The `Color` value itself is canonical after construction:

- It does not store the source color space.
- It does not store the source transfer function.
- It does not store tone-mapping policy.
- It does not store the target backend format.
- It does not distinguish SDR or HDR output mode.

Construction methods such as `Color.FromSrgb`, `Color.FromDisplayP3`, and `Color.FromRec2020` convert authoring values into the internal linear BT.2020 representation. Export methods such as `ToSrgb`, `ToDisplayP3`, and `ToRec2020` convert from that canonical value to the requested color space.

This keeps color values as values. Runtime output policy belongs to the material/style/compositor/output context, not to each `Color` instance.

## Current Implementation Stage

The active implementation now has the first internal SDR/sRGB color slice:

- `Color` is an internal 16-byte canonical value storing linear BT.2020 / Rec.2020 RGBA with straight alpha.
- `SrgbColor` is an internal 4-byte SDR bridge value used for sRGB import/export.
- `StyleColor` wraps canonical `Color` and treats `FromArgb` / `Opaque` as sRGB authoring adapters.
- `PropertyValue.Color` stores the canonical `Color` value instead of only a `uint ARGB` payload.
- `DrawCommand` stores an internal canonical color/material payload while preserving `DrawColor` as an SDR authoring/output view.
- `DrawMaterial` is an internal solid-color material value over canonical `Color`; `FrameDrawingResources` has an internal brush resource table for that shape.
- `DrawColor` and `WindowColor` remain SDR/sRGB bridge payloads, not the canonical color value.

The active renderer output is still the SDR/sRGB pipeline:

```text
StyleColor / PropertyValue.Color: canonical Irix Color
  -> DrawCommand canonical color payload
  -> sRGB downgrade at backend/output boundary
  -> DrawColor / WindowColor / D3D12 R8G8B8A8 output payload
  -> D3D12 rectangle or GlyphAtlas text pass
  -> sRGB SDR presentation
```

`DrawColor` and `WindowColor` currently use compact 8-bit ARGB-like storage on the active output path. That storage is not the long-term definition of Irix color. It is the current SDR output representation and compatibility boundary while the renderer remains sRGB-only.

The draw/material migration decision is now partially selected: draw commands carry canonical `Color` internally, the current SDR/sRGB output stage is named by an internal `ColorOutputMapping.SdrSrgb` mapper, and the current material/resource preflight has an internal solid-color `DrawMaterial` plus brush table. Gradient/image materials, public material authoring, and HDR output mapping remain deferred. The current D3D12 output path still maps to sRGB until the HDR path is implemented.

A source guard now pins that deferral: `DrawMaterial` and `IFrameBrushResolver` remain internal, `DrawMaterialKind` is limited to `None` and `SolidColor`, and style/property authoring does not expose brush, material, gradient, or image value factories. This guard is an architecture preflight only; it does not choose the future non-solid material model.

## Non-Solid Material Policy Preflight

Non-solid materials are design-gated, not implemented. `DrawMaterialKind` must not grow beyond `None` and `SolidColor`, and public/style authoring must not expose brush, material, gradient, or image factories, until a dedicated material policy slice selects and guards all of these contracts:

- Material identity and lifetime: whether non-solid material resources are frame-scoped, retained-frame-scoped, app-owned, shared, or backend-owned; how `ResourceHandle` identity survives retained partial apply; and when image/gradient data can be released.
- Coordinate space: whether gradients, image sampling rects, tiling, transforms, and clips are expressed in local node space, command space, layer space, or physical backend pixels.
- Color interpolation: gradient stops use canonical Irix `Color`; interpolation must name linear BT.2020 vs output-space behavior, alpha handling, gamut clipping, and whether any backend approximation is acceptable.
- Invalidation: material changes must classify whether they require layout, text shaping, draw command update, composition property update, layer-cache invalidation, or full fallback.
- Backend capability and fallback: the D3D12-first path must expose capability checks and deterministic fallback before Vulkan/Metal or public authoring names are introduced.
- Diagnostics and tests: selected material kind, resource owner, fallback reason, layer-cache reuse/invalidations, and SDR output mapping must be observable enough to protect the active SDR/sRGB path.

Until that slice lands, non-solid materials stay out of `DrawMaterial`, `StyleValue`, `PropertyValue`, `VirtualPropertyKey`, and public UI style authoring. Image handles and `DrawingResourceKind.Image` may remain low-level drawing/resource placeholders, but they are not style materials.

## Canonical Color Contract

| Field | Contract |
|-------|----------|
| Primaries | BT.2020 / Rec.2020 |
| Transfer | Linear light inside Irix |
| White point | D65 |
| Alpha | Straight alpha in the `Color` value |
| Precision | Current internal implementation uses 16-byte storage with four floating-point channels. |
| Source metadata | Not stored in `Color` after construction |
| Output metadata | Not stored in `Color`; owned by output mapping context |

The important invariant is that `Color` is a canonical value type. `Color.FromSrgb(255, 0, 0)` means "the sRGB red authoring value converted into Irix's linear BT.2020 space", not "the BT.2020 red primary".

## Authoring Boundary

Public or UI-facing color authoring should remain semantic and explicit about input color space:

| Authoring entry | Meaning |
|-----------------|---------|
| `FromSrgb` | Interpret bytes/floats as sRGB authoring values, then canonicalize to Irix linear BT.2020. |
| `FromDisplayP3` | Interpret values as Display P3 authoring values, then canonicalize to Irix linear BT.2020. |
| `FromRec2020` | Interpret values as Rec.2020 values, then canonicalize to Irix linear BT.2020. |
| `FromScRgb` | Interpret values as scRGB input, then canonicalize to Irix linear BT.2020. |

After construction, the original authoring space is intentionally forgotten. If a caller needs to preserve where a color token came from, that is token/resource metadata, not `Color` value metadata.

## SDR Output Path

The current selected output path is SDR/sRGB:

```text
Irix Color: linear BT.2020
  -> map to linear sRGB
  -> clamp or gamut-map according to the active SDR policy
  -> apply sRGB transfer
  -> emit sRGB draw/backend payload
```

For the current implementation stage, this means existing 8-bit `DrawColor` and `WindowColor` payloads remain valid as explicit SDR authoring/output bridges. They must not be treated as the canonical Irix color representation. `DrawCommand.Color` is an SDR view for compatibility and diagnostics; the retained draw payload uses canonical `Color` and an internal solid-color `DrawMaterial` shape. D3D12 layer content cache stores material payloads, while D3D12 and legacy window output currently convert through `ColorOutputMapping.SdrSrgb`, which is an internal current-stage mapper and not a public API.

The first implementation target should preserve current sRGB visual behavior for existing colors while changing the internal contract so that future wide-gamut and HDR support does not require redefining style color semantics again.

## HDR Output Path

HDR output is deferred until HDR support is implemented as a single coordinated backend/compositor feature. The design direction is:

```text
Irix Color: linear BT.2020
  -> output mapping context reads monitor/system state
  -> apply SDR white and tone-mapping policy
  -> map to backend-preferred HDR output
```

On Windows, future backend support may prefer:

- scRGB / FP16 swapchain output when that is the most practical Advanced Color path.
- Rec.2100 HLG or PQ output when the backend, swapchain, metadata, and display path support it.

The HDR implementation must be introduced with an explicit output mapping context that owns:

- SDR reference white / system SDR brightness.
- Monitor or region color information.
- Swapchain format and color-space capability.
- Tone-mapping policy.
- Gamut mapping policy.

None of those policies belong inside the `Color` value.

## Compositor And Screen Regions

The compositor/output boundary owns final mapping from Irix color to the screen/backend payload. This keeps color management aligned with multi-monitor and future per-region output:

```text
UI/style/material color
  -> canonical Irix Color
  -> draw/material/composition payload
  -> compositor output mapping for the target screen region
  -> backend swapchain format and color space
```

When a window spans multiple screens, the first implementation may choose a dominant output profile. More precise per-screen-region mapping should wait until composition region ownership, swapchain policy, and hit-test/presentation semantics are explicit.

## Non-Goals

- No HDR swapchain implementation in the current stage.
- No Rec.2100 HLG/PQ output implementation in the current stage.
- No per-monitor color-profile mapper in the current stage.
- No public color API migration in this document.
- No tone-mapping policy stored inside `Color`.
- No source color-space metadata stored inside `Color`.
- No replacement of the D3D12 rectangle/GlyphAtlas passes as part of this design note.

## Acceptance For The Next Implementation Slice

The current SDR/sRGB code slice is acceptable when:

- A canonical internal color value exists for linear BT.2020 straight-alpha color.
- sRGB authoring converts into that canonical value rather than being interpreted as BT.2020 coordinates.
- Conversion back to sRGB preserves current SDR behavior for existing UI colors within a narrow tolerance.
- Draw commands retain canonical color payload internally while preserving existing SDR output behavior.
- Current backend/window output converts canonical colors through the internal `ColorOutputMapping.SdrSrgb` stage.
- Internal solid-color material/resource shape exists without exposing public material authoring.
- Source guards keep public style/property authoring free of brush/material/gradient/image factories while the internal material shape remains solid-color only.
- A non-solid material policy preflight documents the required lifetime, coordinate, interpolation, invalidation, backend fallback, and diagnostics contracts before gradient/image material implementation can start.
- `DrawColor` and `WindowColor` are documented and guarded as SDR bridge/output payloads, not canonical Irix color.
- HDR policy remains unimplemented but has a clear output mapping owner.

Current status: implemented for internal style/property color, the draw-command payload bridge, the current SDR/sRGB output mapper, the internal solid-color material/resource preflight, the public material-authoring deferral guard, and the non-solid material policy preflight. Non-solid material implementation and HDR backend output mapping remain future work.
