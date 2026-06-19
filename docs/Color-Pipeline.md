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

- `Color` is a public 16-byte canonical value storing linear BT.2020 / Rec.2020 RGBA with straight alpha.
- `SrgbColor` is a public 4-byte SDR bridge value used for sRGB import/export.
- `StyleColor` wraps canonical `Color` and treats `FromArgb` / `Opaque` as sRGB authoring adapters.
- `Paint` is a public 36-byte semantic value for solid color and two-stop linear-gradient authoring. Linear gradients select one of four element-bounds-relative directions and contain canonical `Color` stops.
- `BorderStroke` is a public 40-byte semantic value that binds one `Paint` to one finite positive logical thickness. Keeping those terms together prevents invalid split border state.
- `PropertyValue.Color` stores canonical foreground color, `PropertyValue.Paint` stores semantic background paint, and `PropertyValue.BorderStroke` stores semantic border paint plus thickness.
- `DrawCommand` stores an internal canonical color/material payload while preserving `DrawColor` as an SDR authoring/output view.
- `DrawMaterial` is an internal material value over canonical `Color`; it currently supports solid color plus a renderer-owned two-stop linear gradient payload. D3D12 FillRect can rasterize clamp-free linear gradients through per-corner SDR vertex-color interpolation and preserves start/end clamp behavior through a bounded segmented fallback, while text, legacy window output, and unsupported material paths still use deterministic SDR fallback. `FrameDrawingResources` has an internal brush resource table for those shapes.
- `DrawMaterialOutputDiagnostics` is the internal diagnostics-only status for the active SDR/sRGB material mapper: it reports selected material kind, backend material capability, fallback reason, and fallback counts without exposing renderer policy or changing backend execute contracts.
- `DrawColor` and `WindowColor` remain SDR/sRGB bridge payloads, not the canonical color value.

The active renderer output is still the SDR/sRGB pipeline:

```text
Color / Paint / PropertyValue: canonical authoring values
  -> DrawCommand canonical color payload
  -> sRGB downgrade at backend/output boundary
  -> DrawColor / WindowColor / D3D12 R8G8B8A8 output payload
  -> D3D12 rectangle or GlyphAtlas text pass
  -> sRGB SDR presentation
```

`DrawColor` and `WindowColor` currently use compact 8-bit ARGB-like storage on the active output path. That storage is not the long-term definition of Irix color. It is the current SDR output representation and compatibility boundary while the renderer remains sRGB-only.

The draw/material migration now includes two public vertical slices: public `Color`, `SrgbColor`, semantic `Paint`, and typed `BorderStroke` values feed `VirtualNodeProperty.Background` and `VirtualNodeProperty.Border`, while draw commands carry internal canonical `DrawMaterial` payloads and the current SDR/sRGB output stage remains an internal `ColorOutputMapping.SdrSrgb` mapper. The public slices support solid or two-stop directional background paint and inward border paint over rectangle/button bounds. Draw recording resolves semantic directions against the actual layout bounds, stores command-local logical start/end points in an internal `DrawMaterial`, retains the same material in the frame brush table, and emits one logical `StrokeRect` for a border. D3D12 expands that stroke into four inward edge rectangles while sampling one continuous gradient coordinate domain over the outer element bounds; horizontal and vertical thickness scale independently for asymmetric DPI. Legacy window output preserves border thickness and collapses non-solid paint through the deterministic canonical-color midpoint fallback. Image materials, radial gradients, shared material tokens, arbitrary stops/transforms, foreground paint, and HDR output mapping remain deferred.

A source guard pins the boundary: `DrawMaterial`, `DrawPoint`, `DrawMaterialBackendCapabilities`, `ResourceHandle` brush handles, and `IFrameBrushResolver` remain internal; `DrawMaterialKind` is limited to `None`, `SolidColor`, and `LinearGradient`; and public authoring exposes semantic `Paint`, typed `BorderStroke`, `VirtualNodeProperty.Background`, and `VirtualNodeProperty.Border` without exposing renderer materials, brush handles, backend capabilities, radial gradients, or images.

## Non-Solid Material Policy Preflight

The first non-solid material policy slices are implemented for public two-stop linear-gradient background and border paint. `DrawMaterialKind` remains limited to `None`, `SolidColor`, and `LinearGradient`; radial gradients, images, shared material resources, arbitrary stop collections, and material transforms remain design-gated.

- Material identity and lifetime: public `Paint` and internal `DrawMaterial` are unmanaged values. Gradient recording adds the material to `FrameDrawingResources`; retained frames retain that resource snapshot, and layer-cache payloads retain the resolved material value. Future image/shared/backend-owned resources still need explicit lifetime and release rules.
- Coordinate space: public directions are relative to the painted element bounds. Draw recording resolves them to command-local logical points: left-to-right `(0,0)->(width,0)`, top-to-bottom `(0,0)->(0,height)`, and the two corner diagonals. Border edge rectangles sample that same outer-bounds coordinate domain instead of restarting the gradient per edge. Display scale and layer transforms remain backend/compositor operations over that command-local payload.
- Color interpolation: the current linear gradient stores two canonical Irix `Color` stops. D3D12 FillRect and StrokeRect sample those stops in command-space and emit per-corner SDR vertex colors through `ColorOutputMapping.SdrSrgb`; the existing shader interpolates those vertex colors. StrokeRect is expanded into four inward rectangles while retaining continuous outer-bounds sampling. The supported single-rect fill path reports its internal representative color from the fill-local center sample, keeping background/diagnostic color aligned with the rendered gradient rather than the unsupported-path fallback midpoint. Degenerate D3D12 FillRect gradients whose start and end points are equal rasterize as the start stop on the supported single-rect path. When a clamp boundary crosses a fill or border edge, D3D12 keeps a bounded segmented vertex-color fallback so the active path does not flatten clamp plateaus into a single unconstrained ramp. The active SDR/sRGB backend path may collapse unsupported non-solid materials through a deterministic `DrawMaterial.FallbackColor` linear BT.2020 midpoint before sRGB output; broader gradient rasterization must later name interpolation, alpha handling, gamut clipping, and backend approximation policy.
- Invalidation: background paint and border stroke are visual properties. Their changes require draw-command update and naturally change command/resource identity for retained and layer-cache invalidation, without changing layout, hit targets, or text shaping.
- Backend capability and fallback: the D3D12-first FillRect/StrokeRect path currently exposes solid-color plus linear-gradient material backend capability using per-corner interpolation when clamp-free and bounded segmented fallback when clamp boundaries cross the painted geometry. Stroke thickness is inward and scales independently on each axis. D3D12 text, legacy window output, image/radial/future materials, and unsupported non-solid paths must map through deterministic fallback before Vulkan/Metal, richer rasterization, or public authoring names are introduced.
- Diagnostics and tests: selected material kind, backend material capability, fallback reason, fallback counts, linear-gradient single-rect versus segmented rasterization counters, layer-cache reuse/invalidations, and SDR output mapping must be observable enough to protect the active SDR/sRGB path.

The implemented slices store background paint in `StyleValue`, `PropertyValue.Paint`, and `VirtualPropertyKey.Background`, and border intent in `StyleValue`, `PropertyValue.BorderStroke`, and `VirtualPropertyKey.Border`. Foreground remains a canonical color property. Image handles and `DrawingResourceKind.Image` remain low-level drawing/resource placeholders, not public style materials.

The public material authoring policy is now implemented for background paint and typed border stroke: UI authoring describes semantic `Paint` or `BorderStroke`, never `DrawMaterial`, backend capabilities, output mapping kinds, brush `ResourceHandle` values, or resolver interfaces. Richer material resources still require dedicated slices.

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

For the current implementation stage, this means existing 8-bit `DrawColor` and `WindowColor` payloads remain valid as explicit SDR authoring/output bridges. They must not be treated as the canonical Irix color representation. `DrawCommand.Color` is an SDR view for compatibility and diagnostics; the retained draw payload uses canonical `Color` and an internal `DrawMaterial` shape. D3D12 layer content cache stores material payloads, while D3D12 and legacy window output currently convert through `ColorOutputMapping.SdrSrgb`, which is an internal current-stage mapper and not a public API. D3D12 FillRect linear-gradient rasterization emits a single rect with per-corner SDR vertex colors when clamp-free, uses the fill-local center sample as its representative SDR color, treats degenerate equal-point gradients as a start-color single rect, and emits bounded segmented vertex-color rects when clamp boundaries cross the fill; these report `backendCapabilities=SolidColor, LinearGradient`, `selectedMaterialKind=LinearGradient`, `fallbackReason=None`, and zero fallback commands. The material output line also reports `linearGradientSingleRectCommands`, `linearGradientSegmentedCommands`, and `linearGradientSegmentRects` so the active D3D12 path can distinguish clamp-free and bounded clamp-fallback rasterization without exposing material authoring publicly. D3D12 text and legacy window output still use the material fallback color for linear gradients and report `fallbackReason=UnsupportedNonSolidMaterial` when that path is used.

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

## HDR Output Mapping Preflight

HDR output mapping is design-gated, not implemented. `ColorOutputKind` must stay limited to `SdrSrgb`, the active D3D12 swapchain/render-target formats must stay on the current SDR path, and `Color` must not grow output-mode metadata until a dedicated output mapping slice selects and guards all of these contracts:

- Output mapping context: a value or service owned by the compositor/backend boundary, not by `Color`, style declarations, draw commands, or Poc diagnostics.
- Screen-region policy: how a window spanning multiple screens chooses a dominant mapping or splits regions, and how that decision interacts with retained composition, hit testing, and presentation state.
- SDR reference white and tone mapping: how system SDR brightness, SDR reference white, peak luminance assumptions, clipping, and tone-mapping policy are read and represented.
- Swapchain and color-space capability: how the backend discovers support for sRGB SDR, scRGB FP16, Rec.2100 HLG/PQ, HDR metadata, and required fallback paths.
- Backend payload format: whether the active output uses sRGB bytes, scRGB/FP16, or another HDR payload; how glyph atlas and rectangle passes share that format.
- Diagnostics and fallback: selected output kind, monitor/screen decision, swapchain format, SDR white, tone-mapping policy, and fallback reason must be observable before the active renderer changes output mode.

Until that slice lands, HDR output stays out of `ColorOutputMapping`, `DrawColor`, `WindowColor`, `DrawCommand`, D3D12 swapchain selection, D3D12 PSO render-target formats, and public style/color authoring. Platform screen metadata may report available color spaces such as HDR10, but that information is not yet consumed by renderer output mapping.

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
- No public radial-gradient, image-paint, shared material-resource, or arbitrary-stop implementation in the current stage.
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
- Public canonical `Color`/`SrgbColor`, semantic `Paint`, and typed `BorderStroke` authoring exist without exposing renderer-owned materials.
- The first internal linear-gradient material payload preserves identity through draw commands, frame brush resources, and D3D12 layer-cache payloads; D3D12 FillRect now rasterizes clamp-free fills through per-corner SDR vertex-color interpolation with center-sample representative color, treats degenerate equal-point gradients as start-color single rects, uses bounded segmented fallback for clamp-crossing fills, and reports the selected rasterization shape while unsupported paths use deterministic fallback.
- Material output diagnostics report selected material kind, backend capability, fallback reason, and fallback counts for direct and D3D12 layer-cache execution paths.
- Source guards allow semantic solid/two-stop linear-gradient background and border paint while keeping brush/material/radial-gradient/image factories and renderer-owned payloads out of the public API.
- A non-solid material policy preflight documents the required lifetime, coordinate, interpolation, invalidation, backend fallback, and diagnostics contracts before broader gradient/image material implementation can start.
- The public material authoring policy restricts UI authoring to semantic paint intent and excludes renderer-owned `DrawMaterial`, brush resource handles, backend capability flags, and output mapping policy.
- An HDR output mapping preflight documents the required output mapping context, screen-region policy, SDR white/tone mapping, swapchain capability, backend payload format, diagnostics, and fallback contracts before HDR output implementation can start.
- `DrawColor` and `WindowColor` are documented and guarded as SDR bridge/output payloads, not canonical Irix color.
- HDR policy remains unimplemented but has a clear output mapping owner.

Current status: implemented for public canonical color values, public semantic solid/two-stop linear-gradient background paint, typed inward semantic border authoring, style/property storage, retained frame brush resources, the draw-command payload bridge, D3D12 FillRect/StrokeRect rasterization, deterministic legacy fallback with retained border thickness, the current SDR/sRGB output mapper, material diagnostics, and the HDR output mapping preflight. Richer gradient/image/shared material authoring, foreground paint, and HDR backend output mapping remain future work.
