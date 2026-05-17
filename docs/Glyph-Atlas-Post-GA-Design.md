# Glyph Atlas Post-GA Design

> Design note for issue #2. This is not part of the V1 MVP/GA candidate. The V1 Windows text path remains DirectWrite / Direct2D over D3D11On12.

## Goal

Introduce an explicit glyph atlas/cache after V1 GA only if profiling shows DirectWrite / Direct2D text rendering is the limiting cost or if a future backend needs portable glyph resources. The design must not change the current `IDrawingBackend` public contract until a separate API review accepts it.

## Phase 1 Composition Seam

The first post-GA renderer-foundation change introduced an internal text composition mode seam only. After opt-in smoke evidence, the D3D12 PoC baseline now defaults to `GlyphAtlas`; `--text-composition overlay` remains the old overlay rollback and unsupported atlas frames fall back to the overlay renderer. The preserved overlay path is still `D3D12 rect pass -> D3D11On12 / D2D / DirectWrite overlay -> sync wait -> Present`.

DirectWrite is retained as a shaping, metrics, and glyph bitmap source for the atlas path. The near-term goal is to remove D3D11On12 / D2D / DirectWrite from final overlay composition, not to remove DirectWrite from text processing. No public API or `IDrawingBackend.Execute` signature changes are part of this phase.

The first executable atlas path is intentionally narrow but default-on in the post-GA renderer-foundation branch. Basic single-line ASCII / `NoWrap` runs may be rasterized from DirectWrite glyph analysis into a D3D12 `R8_UNORM` atlas texture and drawn as D3D12 glyph quads before command-list close/execute. Per-run scissor clipping is supported for accepted runs. Unsupported runs, including complex shaping, non-ASCII fallback faces, wrapping, missing fonts, atlas-full conditions, vertex/batch limits, and initialization/upload failures, must fall back to the existing overlay renderer until the atlas path is correctness-complete for those cases.

Current implementation status:

| Area | Current status |
|------|----------------|
| Public API | No change; `IDrawingBackend.Execute` remains unchanged |
| Default composition | `GlyphAtlas`; `--text-composition overlay` remains rollback |
| Supported atlas text | ASCII printable characters, single-line `NoWrap`, DirectWrite glyph metrics/raster source |
| Atlas format | Single `R8_UNORM` alpha atlas |
| Draw model | D3D12 glyph quads recorded before command-list close/execute |
| Alignment | Leading, center, and trailing are supported for no-wrap line widths |
| Clip | Per-run scissor clip supported for accepted atlas runs |
| Fallback | Overlay renderer for unsupported/failed atlas frames; fallback reasons are diagnostic output |
| Not implemented | Non-ASCII shaping, fallback font identity, color glyphs, wrapping, eviction, mixed per-run atlas/overlay composition |

Phase 1 closeout: local evidence has been captured for default overlay regression, opt-in glyph-atlas ASCII smoke, NonAscii and AtlasFull fallback, resize, 100% / 150% / 200% scale, and warm allocation baseline. The post-GA default baseline is now `GlyphAtlas` with overlay rollback. The next phase should focus on renderer-foundation hardening, especially shader bytecode/resource lifetime, rather than expanding the ASCII prototype surface.

Known limitations:

- The atlas PSO still uses runtime shader compilation through `d3dcompiler_47.dll`. AOT/self-contained publish currently succeeds, but build-time compiled or embedded bytecode is the preferred hardening direction.
- Fallback is whole-frame fallback. If any text run is unsupported, text composition falls back to overlay for the frame.
- Atlas eviction is not implemented; AtlasFull fallback is the safety behavior.
- Complex shaping, fallback font face identity, color glyphs, wrapping, and mixed atlas/overlay composition are deferred.
- Warm glyph-atlas scroll allocation is still about `6.2 KB/frame`; attribution should precede optimization.

## Non-Goals

- No implementation before the current GA candidate.
- No renderer rewrite.
- No public API change in the current V1 branch.
- No replacement of DirectWrite shaping in the first spike.
- No promise that glyph atlas replaces the Direct2D fallback for all scripts or effects.

## Glyph Key

A glyph cache key should be value-based and independent of frame-local text arenas.

Candidate fields:

| Field | Purpose |
|-------|---------|
| Font family / face identity | Separates typefaces and fallback faces |
| Font weight / style / stretch | Matches `TextStyle` shaping inputs |
| Em size in physical pixels | Keeps DPI-specific raster output explicit |
| Glyph index | Identifies the shaped glyph inside the face |
| Subpixel mode / pixel snapping mode | Prevents mixing incompatible raster outputs |
| Render mode / antialias mode | Separates grayscale, ClearType, and future SDF modes |
| Locale / script fallback face | Required when DirectWrite selects a fallback font |
| Color glyph layer identity | Required for emoji and color-font paths |

The key should not include source string content. Text shaping produces positioned glyph runs; the atlas stores glyph bitmaps or masks.

## Atlas Page Model

Use fixed-size pages with stable page handles.

Initial candidate:

| Property | Candidate |
|----------|-----------|
| Format | `R8_UNORM` for alpha masks; future `BGRA8` for color glyph layers |
| Page size | 1024x1024 or 2048x2048, selected after upload/profile data |
| Packing | Skyline or shelf allocator for simple predictable behavior |
| Padding | 1-2 physical pixels to prevent bilinear bleeding |
| Coordinates | Normalized UVs plus physical-pixel bounds |
| Lifetime | Page survives across frames until cache pressure evicts it |

Each cached glyph maps to `GlyphAtlasEntry { PageId, PixelRect, UvRect, Bearing, Advance, Version }`.

## Eviction

Use bounded memory with LRU at page or entry level.

Rules:

- Prefer page-level eviction for simplicity if fragmentation is acceptable.
- Never evict entries referenced by the frame currently being recorded or submitted.
- Maintain a generation/version number so stale retained commands cannot sample recycled atlas regions.
- Evict cold pages first; pin fallback/system UI glyph pages only if profiling proves churn.
- Emit diagnostics: page count, used pixels, fragmentation estimate, upload bytes/frame, evictions/frame, misses/frame.

## DPI And Scale

Glyph raster output is DPI-specific. A scale change invalidates or partitions glyph entries by physical em size.

Rules:

- Keep logical text measurement in the existing layout pipeline.
- Rasterize glyphs at physical pixel size after `DisplayScale` is applied.
- Do not reuse a 100% glyph bitmap at 150% or 200% by stretching.
- Runtime DPI changes may keep old pages alive briefly for in-flight frames, but new frames must request entries from the new scale partition.

## Upload Path

The first spike should avoid per-glyph GPU synchronization.

Candidate upload flow:

1. Shape text using DirectWrite into glyph runs.
2. Rasterize missing glyphs into CPU staging memory or a DirectWrite-compatible bitmap path.
3. Pack missing glyphs into atlas pages.
4. Batch page updates into one upload list per frame.
5. Draw glyph quads after rectangle pass, using scissor/clip state compatible with current text clipping.
6. Keep Direct2D overlay as fallback while atlas path is incomplete.

Upload diagnostics must include bytes uploaded per frame and number of new glyph entries.

## Clip And Layout Interaction

Clipping remains per draw/run, not baked into glyph entries.

Rules:

- Glyph atlas entries contain glyph images only.
- Draw-time quads apply the current effective clip/scissor.
- Text layout still computes line breaks, glyph positions, and run bounds before draw recording.
- Existing `DrawTextRun` clip semantics must remain observable in diagnostics.

## Fallback Strategy

Direct2D / DirectWrite remains the authoritative fallback. Current opt-in glyph atlas fallback is frame-level: if any text run in the frame is unsupported, the frame uses overlay text composition. A future mixed fallback design should allow accepted ASCII runs to draw through atlas while unsupported runs draw through overlay, but that requires careful ordering, clip, and sync semantics before implementation.

Fallback cases:

- Complex color glyphs not supported by the first atlas format.
- Scripts or shaping features not yet covered by the glyph-run conversion.
- Atlas full and eviction cannot safely free space for the current frame.
- Glyph atlas initialization or upload failure.
- Debug flag forces Direct2D text for A/B comparison.

Fallback must preserve text/rect synchronization and clip behavior.

## Open Questions

- Should first implementation be alpha-mask atlas or signed-distance-field atlas?
- Should glyph rasterization use DirectWrite glyph run analysis or a separate rasterizer?
- What memory budget should be used per monitor/adapter?
- How should retained frames reference atlas entries across page eviction?
- Should color glyphs use separate BGRA pages or always fallback to Direct2D initially?

## Acceptance Criteria For A Future Spike

- No public API change without a design review.
- Text cache diagnostics show reduced overlay cost on the target workload.
- 100% / 150% / 200% scale smokes show no stretched text and correct hit-test/clip behavior.
- Scroll text remains synchronized at 60Hz with default sync enabled.
- Atlas eviction cannot produce stale glyphs in retained or partial frames.
- Direct2D fallback remains available and tested.
