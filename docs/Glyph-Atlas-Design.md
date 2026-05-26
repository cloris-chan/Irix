# Glyph Atlas Renderer Design

> Current D3D12 text renderer contract. DirectWrite and WIC provide source data; final text composition stays in the D3D12 command stream.

## Goal

Keep Windows text final composition in the D3D12 command stream through a glyph atlas/cache while preserving the current public drawing contract. `IDrawingBackend.Execute` does not change in this design. Future portable glyph-resource work can build on this contract after a separate API review.

## Current Renderer Contract

| Area | Contract |
|------|----------|
| Final composition | `D3D12 rect pass -> D3D12 GlyphAtlas text pass -> Present`. |
| Public API | No public backend or drawing API change. |
| Text source | DirectWrite may provide shaping, metrics, fallback font mapping, glyph raster data, and color glyph source data. WIC may decode PNG/JPEG/TIFF glyph image data. |
| Unsupported text | Unsupported or unsafe renderable runs degrade explicitly. They are counted and not drawn. |
| Clip | FillRect scissor and accepted GlyphAtlas text clip are default-on; diagnostic rollback remains available. |
| Shader packaging | D3D12 rectangle and glyph atlas passes use embedded DXBC bytecode. Runtime `D3DCompile` / `d3dcompiler_47.dll` must not return. |
| Text state | Renderer/core paths must not retain source strings. Frame text resolves through `TextSlice` / resolver boundaries; glyph cache keys use value glyph atoms and native glyph identity. |

## Guarded Coverage Expansion

Glyph atlas coverage is guard-gated, not milestone-frozen. New script or glyph-image-format support should move forward when it includes matching shaping oracle, regression matrix, and degradation-policy coverage. New accepted coverage must not be added opportunistically in `D3D12GlyphAtlasTextRenderer`; it needs a matching oracle/regression case first.

Current accepted coverage includes:

- ASCII printable runs.
- Simple Latin Extended, Greek, Cyrillic BMP runs covered by the selected DirectWrite face.
- DirectWrite-shaped runs with nonzero glyph indices.
- DirectWrite fallback-face segmentation for mixed base/fallback runs such as ASCII + CJK.
- Explicit CR/LF line breaks.
- Tab as a four-space advance control segment.
- Minimal whitespace wrapping, unbreakable word clipping, and over-height line-stack clipping.
- LTR complex-script shaped runs that stay within the current accepted projection.
- Single-level RTL `NoWrap`, RTL-base wrapped/mixed lines including leading weak digits, Hebrew/Arabic presentation-form RTL classification, and mixed BiDi resolved-level segment ordering.
- DirectWrite outline/COLR color layers through alpha atlas pages and per-layer color.
- DirectWrite premultiplied BGRA glyph image data and WIC-decoded PNG/JPEG/TIFF image data through BGRA atlas pages.

## Atlas Residency And Cache Policy

The atlas uses fixed 1024x1024 pages with page format metadata:

- `Alpha` pages for alpha masks and outline/COLR color layers.
- `Bgra` pages for DirectWrite premultiplied BGRA and WIC-decoded bitmap color glyph images.

Glyph entries and atlas pages use stable value handles with generations. Page-owned texture/upload/SRV state replaces renderer-level atlas fields. The renderer grows the atlas pool on demand from one page up to the 48-page budget exposed as `AtlasPageBudget`.

Current reuse policy:

- Page-level format-scoped cold reuse only.
- Current-record reuse may use only cold pages not touched in the current record.
- Retained-floor-gated next-record reuse applies only after the triggering record is older than the oldest retained atlas record serial.
- Failed text runs roll back newly cached entries, non-reused page packing changes, and pages created by the failed run.
- Full LRU / entry-level eviction remains deferred.

Diagnostics expose total and format-split page counts, resident CPU shadow bytes, D3D12 upload-buffer bytes, D3D12 texture bytes, used/fragmented pixels, page ages, pending/completed reuse counters, hard full-without-reuse counters, upload bytes, uploaded glyph count, color layer/bitmap run counts, and record/init failure phases.

## Regression Lane

`scripts/glyph-atlas-regression.ps1` is the fixed local gate for guarded coverage changes and is configured in Windows CI as `Glyph atlas regression lane`. GitHub Actions quota is currently exhausted, so the CI lane is configured but not runnable as status. Keep `.github/workflows/ci.yml`, but do not rely on Actions until quota returns.

- `-Mode Smoke`: matrix, 60-frame soak, color-format natural coverage probe, BiDi oracle, and glyph oracle. Run before/after broad changes.
- `-Mode Local`: extends soak to 300 frames. Run after glyph/page/shaping changes.
- `-Mode Nightly`: extends soak to 900 frames. Run manually after page-policy, eviction, or shaping overhauls.

The script writes `TestResults\glyph-atlas-regression-*-*.guard.summary.txt`; that local guard summary is the current status source while Actions quota is unavailable. Matrix actual must keep `degradedRuns=0`, `glyphAtlasInitialized=True`, and `finalComposition=D3D12`. Soak must keep `deviceLost=False`, `syncWaits=0`, `hardFullWithoutReuse=0`, `RecordFailed=0`, and `recordFailurePhase=None`. BiDi/glyph oracle expected and actual probe labels/counts must match with `finalComposition=D3D12`.

## Structural Oracles

The current oracle layer is structural, not a pixel/layout golden system:

- `--diagnose-glyph-atlas-matrix` pins ASCII, Latin Extended, Greek, Cyrillic, CJK, Arabic, Hebrew, mixed BiDi, emoji, wrap, tab, and CRLF coverage in one smoke scene.
- `--diagnose-glyph-atlas-bidi-oracle` uses `IDWriteTextAnalyzer.AnalyzeBidi` and feeds DirectWrite-resolved levels into the atlas visual-order helper.
- `--diagnose-glyph-atlas-glyph-oracle` uses DirectWrite analyzer/font fallback data for glyph count, glyph indices, advances, offsets, resolved bidi levels, line-break flags, and fallback-font segments. It does not by itself expand renderer coverage; coverage expansion needs the matching guarded regression case.
- Pixel/layout oracle remains future work.

## Color Glyph Natural Coverage

Default local Segoe UI Emoji currently exposes DirectWrite-renderable COLR/layer runs but no bitmap glyph image data. Noto Color Emoji Windows-compatible font-file probing naturally covers PNG image data and WIC decode into `32bppPBGRA`. Direct BGRA and TIFF natural font coverage remain unavailable locally, so those branches stay covered by selector/raster/decode/page-format tests and guards until a real font/environment is found.

## D3D12-only Degradation Policy

Accepted atlas runs draw through D3D12. Unsupported/failure cases degrade explicitly and are counted.

Accepted degradation cases:

- SVG and COLR paint-tree-only color glyphs beyond DirectWrite outline/COLR color-run layers and DirectWrite bitmap glyph image data are not supported by the current atlas path.
- BiDi beyond the current resolved-level segment projection.
- AtlasFull after the 48-page budget when no safe current-record or retained-floor-gated page reuse is available.
- Glyph atlas record or initialization failure.
- Full LRU/entry-level eviction is not yet implemented; AtlasFull degrades the current run after scheduling safe format-scoped page reuse when possible.

Degradation must preserve renderer stability, diagnostics, and clip semantics. NonAscii, complex shaping, color glyph, atlas-full safety, and runtime failure cases are either handled by D3D12 atlas or reported as D3D12-only degradation.

Remaining work is reducing `DegradedRuns` by expanding the D3D12 atlas path with matching oracle/regression coverage.

## Entry Eviction

Entry eviction design update: entry-level LRU and a sub-rect free-list remain design-only until retained atlas command ownership exposes the oldest retained atlas record serial. The current implementation must stay on page-level format-scoped cold reuse; any future entry eviction must preserve entry/page generation validation, retained-floor safety, rollback of failed run allocations, and explicit degradation on unsafe reuse.

## Future Work

- Promotion of glyph resource contracts beyond Windows D3D12 only after an ownership/API review.
- Build-time shader asset pipeline if embedded shader bytecode becomes too large to maintain inline.
- Pixel/layout oracle after the structural oracle and regression lane remain stable.
- Entry-level eviction only after retained atlas command ownership and sub-rect ownership are explicit.
