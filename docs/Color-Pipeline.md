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

The active implementation is still the SDR/sRGB pipeline:

```text
internal style color
  -> DrawColor / draw command payload
  -> D3D12 rectangle or GlyphAtlas text pass
  -> sRGB SDR presentation
```

`StyleColor`, `DrawColor`, and `WindowColor` currently use compact 8-bit ARGB-like storage on the active path. That storage is not the long-term definition of Irix color. It is the current SDR output representation and compatibility boundary while the renderer remains sRGB-only.

The next color implementation work should first introduce the canonical `Color` value and conversion helpers, then migrate internal style color storage and draw/material payloads toward that canonical value. The current D3D12 output path may continue to downgrade to sRGB until the HDR path is implemented.

## Canonical Color Contract

| Field | Contract |
|-------|----------|
| Primaries | BT.2020 / Rec.2020 |
| Transfer | Linear light inside Irix |
| White point | D65 |
| Alpha | Straight alpha in the `Color` value |
| Precision | High precision value storage. A future implementation may use 16-byte storage with four half-float channels or another explicitly chosen high-precision layout. |
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

For the current implementation stage, this means existing 8-bit `DrawColor` and `WindowColor` payloads remain valid as explicit SDR output payloads. They must not be treated as the canonical Irix color representation.

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

The next code slice is acceptable when:

- A canonical internal color value exists for linear BT.2020 straight-alpha color.
- sRGB authoring converts into that canonical value rather than being interpreted as BT.2020 coordinates.
- Conversion back to sRGB preserves current SDR behavior for existing UI colors within a narrow tolerance.
- `DrawColor` and `WindowColor` are documented and guarded as SDR output payloads, not canonical Irix color.
- HDR policy remains unimplemented but has a clear output mapping owner.
