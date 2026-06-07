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
- `DrawMaterial` is an internal material value over canonical `Color`; it currently supports solid color plus a renderer-owned two-stop linear gradient payload. D3D12 FillRect can rasterize clamp-free linear gradients through per-corner SDR vertex-color interpolation and preserves start/end clamp behavior through a bounded segmented fallback, while text, legacy window output, and unsupported material paths still use deterministic SDR fallback. `FrameDrawingResources` has an internal brush resource table for those shapes.
- `DrawMaterialOutputDiagnostics` is the internal diagnostics-only status for the active SDR/sRGB material mapper: it reports selected material kind, backend material capability, fallback reason, and fallback counts without adding public material authoring or changing backend execute contracts.
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

The draw/material migration decision is now partially selected: draw commands carry canonical `Color` internally, the current SDR/sRGB output stage is named by an internal `ColorOutputMapping.SdrSrgb` mapper, and the current material/resource path has internal `DrawMaterial` payloads plus a brush table. Solid color and D3D12 FillRect linear gradient are the active D3D12 material capabilities. The first non-solid internal payload is a two-stop linear gradient value that preserves material identity for retained/layer-cache payloads; D3D12 FillRect rasterizes clamp-free fills as one rect with per-corner SDR vertex colors and uses a bounded segmented vertex-color fallback when start/end clamp boundaries cross the fill, while D3D12 text, legacy window output, and unsupported material paths collapse through deterministic SDR fallback. That capability/fallback split is diagnostics-visible through `DrawMaterialOutputDiagnostics` and the `Material output status` line. Image materials, public material authoring, and HDR output mapping remain deferred. The current D3D12 output path still maps to sRGB until the HDR path is implemented.

A source guard now pins that deferral: `DrawMaterial`, `DrawPoint`, `DrawMaterialBackendCapabilities`, `ResourceHandle` brush handles, and `IFrameBrushResolver` remain internal; `DrawMaterialKind` is limited to `None`, `SolidColor`, and `LinearGradient`; and style/property authoring does not expose brush, material, gradient, or image value factories. The public material authoring policy preflight in [Style-System.md](Style-System.md) fixes the future UI boundary as semantic paint intent rather than renderer-owned material objects. This guard chooses only the first internal value shape, not the future public material model.

## Non-Solid Material Policy Preflight

Non-solid material authoring remains design-gated; the first internal non-solid payload is implemented only as a renderer-owned linear gradient shape. `DrawMaterialKind` must not grow beyond `None`, `SolidColor`, and `LinearGradient`, and public/style authoring must not expose brush, material, gradient, or image factories, until a dedicated material policy slice selects and guards all of these contracts:

- Material identity and lifetime: the current linear gradient payload is value-typed and frame/retained-frame scoped through `DrawMaterial` and `FrameDrawingResources`; future image/shared/backend-owned resources still need explicit lifetime and release rules.
- Coordinate space: the current linear gradient stores command-space logical start/end points and composes with the command/layer transform by preserving payload identity; future image sampling rects, tiling, material transforms, and physical-pixel backend policies remain unselected.
- Color interpolation: the current linear gradient stores two canonical Irix `Color` stops. D3D12 FillRect samples those stops at rect corners in fill-local command-space and emits per-corner SDR vertex colors through `ColorOutputMapping.SdrSrgb`; the existing shader interpolates those vertex colors. When the start or end clamp boundary crosses the fill rect, D3D12 keeps a bounded segmented vertex-color fallback so the active path does not flatten clamp plateaus into a single unconstrained ramp. The active SDR/sRGB backend path may collapse unsupported non-solid materials through a deterministic `DrawMaterial.FallbackColor` linear BT.2020 midpoint before sRGB output; broader gradient rasterization must later name interpolation, alpha handling, gamut clipping, and backend approximation policy.
- Invalidation: material changes must classify whether they require layout, text shaping, draw command update, composition property update, layer-cache invalidation, or full fallback.
- Backend capability and fallback: the D3D12-first FillRect path currently exposes solid-color plus linear-gradient material backend capability using single-rect per-corner interpolation when clamp-free and bounded segmented fallback when clamp boundaries cross the fill. D3D12 text, legacy window output, image/radial/future materials, and unsupported non-solid paths must map through deterministic fallback before Vulkan/Metal, richer rasterization, or public authoring names are introduced.
- Diagnostics and tests: selected material kind, backend material capability, fallback reason, fallback counts, layer-cache reuse/invalidations, and SDR output mapping must be observable enough to protect the active SDR/sRGB path.

Until public material authoring lands, non-solid materials stay out of `StyleValue`, `PropertyValue`, `VirtualPropertyKey`, and public UI style authoring. Image handles and `DrawingResourceKind.Image` may remain low-level drawing/resource placeholders, but they are not style materials.

The public material authoring policy preflight is now selected at the style boundary: future UI authoring may describe semantic paint slots and material tokens, but it must not accept `DrawMaterial`, backend capabilities, output mapping kinds, brush `ResourceHandle` values, or resolver interfaces. Public material authoring still requires dedicated implementation after resource lifetime, coordinate/sampling, invalidation, backend fallback, diagnostics, and SDR/HDR output separation are guarded.

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

For the current implementation stage, this means existing 8-bit `DrawColor` and `WindowColor` payloads remain valid as explicit SDR authoring/output bridges. They must not be treated as the canonical Irix color representation. `DrawCommand.Color` is an SDR view for compatibility and diagnostics; the retained draw payload uses canonical `Color` and an internal `DrawMaterial` shape. D3D12 layer content cache stores material payloads, while D3D12 and legacy window output currently convert through `ColorOutputMapping.SdrSrgb`, which is an internal current-stage mapper and not a public API. D3D12 FillRect linear-gradient rasterization emits a single rect with per-corner SDR vertex colors when clamp-free and bounded segmented vertex-color rects when clamp boundaries cross the fill; both report `backendCapabilities=SolidColor, LinearGradient`, `selectedMaterialKind=LinearGradient`, `fallbackReason=None`, and zero fallback commands. D3D12 text and legacy window output still use the material fallback color for linear gradients and report `fallbackReason=UnsupportedNonSolidMaterial` when that path is used.

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
- The first internal linear-gradient material payload preserves identity through draw commands, frame brush resources, and D3D12 layer-cache payloads; D3D12 FillRect now rasterizes clamp-free fills through per-corner SDR vertex-color interpolation and uses bounded segmented fallback for clamp-crossing fills while unsupported paths use deterministic fallback.
- Material output diagnostics report selected material kind, backend capability, fallback reason, and fallback counts for direct and D3D12 layer-cache execution paths.
- Source guards keep public style/property authoring free of brush/material/gradient/image factories while the internal material shape remains limited to solid color and linear gradient.
- A non-solid material policy preflight documents the required lifetime, coordinate, interpolation, invalidation, backend fallback, and diagnostics contracts before broader gradient/image material implementation can start.
- A public material authoring policy preflight documents that future UI authoring must describe semantic paint intent rather than expose renderer-owned `DrawMaterial`, brush resource handles, backend capability flags, or output mapping policy.
- An HDR output mapping preflight documents the required output mapping context, screen-region policy, SDR white/tone mapping, swapchain capability, backend payload format, diagnostics, and fallback contracts before HDR output implementation can start.
- `DrawColor` and `WindowColor` are documented and guarded as SDR bridge/output payloads, not canonical Irix color.
- HDR policy remains unimplemented but has a clear output mapping owner.

Current status: implemented for internal style/property color, the draw-command payload bridge, the current SDR/sRGB output mapper, the internal solid-color material/resource path, the first internal linear-gradient material payload, D3D12 FillRect linear-gradient per-corner SDR rasterization with bounded clamp fallback, material output diagnostics/backend capability visibility for the active D3D12 SDR path, the public material-authoring deferral guard and policy preflight, the non-solid material policy preflight, and the HDR output mapping preflight. Public material authoring implementation, richer gradient/image material implementation, and HDR backend output mapping remain future work.
