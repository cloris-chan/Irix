# Glyph Atlas Post-GA Design

> Design note for issue #2. This is not part of the V1 MVP/GA candidate. The V1 Windows text path remains DirectWrite / Direct2D over D3D11On12.

## Goal

Introduce an explicit glyph atlas/cache after V1 GA only if profiling shows DirectWrite / Direct2D text rendering is the limiting cost or if a future backend needs portable glyph resources. The design must not change the current `IDrawingBackend` public contract until a separate API review accepts it.

## Phase 1 Composition Seam

The first post-GA renderer-foundation change introduced an internal text composition mode seam only. After opt-in smoke evidence, the D3D12 PoC baseline now defaults to `GlyphAtlas`; `--text-composition overlay` remains the old overlay rollback. Mixed fallback v0 keeps accepted atlas runs in the D3D12 text pass and sends unsupported renderable runs to the overlay renderer. Initialization or runtime record failure still falls back through overlay for every renderable text run in that frame. The preserved overlay path is still `D3D12 rect pass -> D3D11On12 / D2D / DirectWrite overlay -> sync wait -> Present`.

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
| Shader packaging | Embedded DXBC bytecode; no runtime `D3DCompile` dependency in the D3D12 rect or glyph-atlas pass |
| Alignment | Leading, center, and trailing are supported for no-wrap line widths |
| Clip | Per-run scissor clip supported for accepted atlas runs |
| Fallback | Overlay renderer for unsupported runs; initialization/record failure falls back all renderable runs for that frame |
| Mixed ordering | Rect pass -> atlas accepted runs -> overlay fallback runs -> overlay sync when used -> Present |
| Diagnostics | `AtlasRuns`, `OverlayFallbackRuns`, fallback frames, unsupported runs, and fallback reason counts |
| Not implemented | Non-ASCII shaping, fallback font identity, color glyphs, wrapping, eviction, command-order-perfect mixed text z-order |

Phase 1 closeout: local evidence has been captured for default overlay regression, opt-in glyph-atlas ASCII smoke, NonAscii and AtlasFull fallback, resize, 100% / 150% / 200% scale, and warm allocation baseline. The post-GA default baseline is now `GlyphAtlas` with overlay rollback. The next phase should focus on renderer-foundation hardening, especially shader bytecode/resource lifetime, rather than expanding the ASCII prototype surface.

P1 hardening update: runtime shader compilation has been removed from the D3D12 rectangle pass and glyph-atlas pass. Both use embedded DXBC bytecode, and `D3DCompile` / `d3dcompiler_47.dll` are no longer part of the renderer source generation list. Glyph-atlas initialization failures remain phase-tagged and fall back to the overlay renderer. Runtime record/upload/map failures disable the atlas instance and fall back to overlay with `recordFailurePhase` diagnostics; they are not reported as device lost unless the renderer observes an actual device-removed condition.

Mixed fallback v0 update: `D3D12GlyphAtlasTextRenderer.TryRecord` is now an internal record result rather than a bool-only gate. It fills a caller-owned fallback run list while recording atlas quads for accepted runs.
`D3D12Renderer` passes that fallback subset to `D3D12TextRenderer.Render(...)`, so NonAscii no longer forces every text run in the frame through overlay.
`IDrawingBackend.Execute` and the public drawing contract remain unchanged. Expanded 2026-05-19 smoke covers mixed `ASCII / NonAscii / clipped ASCII / clipped NonAscii` frames and default `300 x 3` long sync.

## Mixed Fallback v0

Frame ordering is fixed:

```text
D3D12 rect pass -> D3D12 glyph atlas accepted text runs -> D3D11On12 / D2D overlay fallback text runs -> sync if overlay ran -> Present
```

This preserves the current renderer's broad visual model: rectangles are drawn first, then text is drawn over rectangles.
It does not fully preserve original `DrawCommand` order for overlapping text runs split across atlas and overlay.
Overlay fallback runs are always drawn after atlas accepted runs, so a fallback run can appear above an atlas run even if the original command order placed it earlier.
The mixed fallback smoke intentionally uses `atlas -> fallback -> atlas -> fallback` text order to keep this limitation visible.
Removing the limitation would require command-order-aware text pass partitioning or a D3D12-only fallback path, not just a narrower overlay list.

The fallback run list is frame-local and caller-owned. `TryRecord` classifies each renderable text run:

- Accepted ASCII / `NoWrap` runs record atlas vertices and increment `AtlasRuns`.
- Unsupported runs are appended to the fallback list with a per-run fallback reason.
- Empty or zero-size text runs are ignored by both atlas and overlay paths.
- Atlas initialization failure or runtime record failure appends every renderable run to the fallback list for that frame.

`D3D12TextRenderer.Render(...)` remains the overlay drawing primitive, but in `GlyphAtlas` mode it receives only fallback runs. In `Overlay` rollback mode it still receives the full text run span.

Overlay subset correctness depends on preserving the exact `D3D12TextRenderer.TextData` for fallback runs.
The subset must carry the frame resource resolver, resolved physical `TextStyle`, effective clip, clip enablement, color, and scaled coordinates.
Current tests pin that contract, and the 150% mixed smoke exercises one clipped atlas run plus one clipped overlay fallback run.

## Shader Bytecode Provenance

The embedded DXBC blobs in `D3D12Renderer2D.cs` and `D3D12GlyphAtlasTextRenderer.cs` were generated with Windows SDK `fxc.exe` from the HLSL below. They are intentionally embedded to remove runtime `D3DCompile` / `d3dcompiler_47.dll` from the default renderer path. If the shaders grow beyond this small fixed surface, replace inline bytecode with a build-time shader asset pipeline rather than reintroducing runtime compilation.

Rectangle pass HLSL:

```hlsl
struct VS_IN { float2 pos : POSITION; float4 col : COLOR; };
struct VS_OUT { float4 pos : SV_POSITION; float4 col : COLOR; };
VS_OUT VSMain(VS_IN i) { VS_OUT o; o.pos = float4(i.pos, 0, 1); o.col = i.col; return o; }
struct PS_IN { float4 pos : SV_POSITION; float4 col : COLOR; };
float4 PSMain(PS_IN i) : SV_TARGET { return i.col; }
```

Glyph-atlas text pass HLSL:

```hlsl
struct VS_IN { float2 pos : POSITION; float2 uv : TEXCOORD; float4 col : COLOR; };
struct VS_OUT { float4 pos : SV_POSITION; float2 uv : TEXCOORD; float4 col : COLOR; };
VS_OUT VSMain(VS_IN i)
{
    VS_OUT o;
    o.pos = float4(i.pos, 0.0f, 1.0f);
    o.uv = i.uv;
    o.col = i.col;
    return o;
}

Texture2D<float> Atlas : register(t0);
SamplerState AtlasSampler : register(s0);
struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD; float4 col : COLOR; };
float4 PSMain(PS_IN i) : SV_TARGET
{
    float coverage = Atlas.Sample(AtlasSampler, i.uv);
    return float4(i.col.rgb, i.col.a * coverage);
}
```

Update flow:

```powershell
$fxc = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\fxc.exe'
& $fxc /nologo /T vs_5_0 /E VSMain /Fo D3D12Renderer2D.vs.cso D3D12Renderer2D.hlsl
& $fxc /nologo /T ps_5_0 /E PSMain /Fo D3D12Renderer2D.ps.cso D3D12Renderer2D.hlsl
& $fxc /nologo /T vs_5_0 /E VSMain /Fo GlyphAtlasText.vs.cso GlyphAtlasText.hlsl
& $fxc /nologo /T ps_5_0 /E PSMain /Fo GlyphAtlasText.ps.cso GlyphAtlasText.hlsl
[Convert]::ToBase64String([IO.File]::ReadAllBytes('D3D12Renderer2D.vs.cso'))
```

After updating embedded blobs, run the shader bytecode decode tests plus D3D12 smoke and publish. The normal test lane contains bytecode decode guards so malformed base64 does not rely on manual smoke to catch packaging errors.

## Failure Diagnostics Contract

`initFailurePhase` is reserved for constructor-time atlas setup failures: DirectWrite factory, font collection, root signature, shader bytecode decode, PSO, atlas texture, upload buffer, descriptor heap, SRV, or vertex buffer creation. These failures prevent atlas creation and fall back to the overlay renderer.

`recordFailurePhase` is reserved for runtime command recording failures after atlas initialization succeeds. Current phases cover generic record failure plus vertex-buffer map and atlas-upload map failures. A runtime record failure disables the current atlas renderer instance and falls back to overlay for subsequent frames without marking the D3D12 device lost by itself.

Known limitations:

- Shader bytecode is currently embedded inline. A future build-time shader asset pipeline can replace the inline packaging if shader source grows, but the runtime compiler dependency is removed.
- Mixed fallback v0 has text z-order limits: fallback overlay runs draw after atlas runs, regardless of original relative text command order.
- Atlas eviction is not implemented; AtlasFull fallback is the safety behavior.
- Complex shaping, fallback font face identity, color glyphs, and wrapping are still overlay fallback cases.
- Warm glyph-atlas scroll allocation was previously about `6.2 KB/frame`; `--diagnose-text-cache` now prints tree/diff/translate/render allocation attribution. Optimization should wait for the attributed evidence rather than guessing.
- Warm allocation follow-up should start with tree construction and translator attribution. Current local evidence attributes the warm scroll sample mostly to tree construction and translation rather than renderer submit.

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

Direct2D / DirectWrite remains the authoritative fallback. Current default GlyphAtlas fallback is mixed per renderable run: accepted ASCII / `NoWrap` runs draw through the atlas, while unsupported runs draw through overlay. Initialization and runtime record failures remain all-renderable-run overlay fallback for the affected frame because no safe atlas command list should be submitted from a failed record path.

Fallback cases:

- Complex color glyphs not supported by the first atlas format.
- Scripts or shaping features not yet covered by the glyph-run conversion.
- Atlas full and eviction cannot safely free space for the current frame.
- Glyph atlas initialization or upload failure.
- Debug flag forces Direct2D text for A/B comparison.

Fallback must preserve text/rect synchronization and clip behavior. Overlay removal is not part of the next commit: D3D11On12 / D2D remains required for NonAscii, complex shaping, wrapping, atlas-full safety, and runtime failure fallback until mixed fallback has smoke evidence across those cases.

## Overlay Removal Gate Draft

D3D11On12 / D2D overlay can be deleted only after all of these are true:

- NonAscii, font fallback, wrapping, alignment, color glyphs, clipping, and scale cases have a non-overlay D3D12 text path or an explicitly accepted non-rendering degradation.
- Atlas-full and eviction behavior are implemented and smoke-tested without needing whole-frame overlay fallback.
- Mixed fallback no longer depends on overlay for unsupported runs, or command-order-aware D3D12 partitioning proves overlapping atlas/fallback text preserves the intended z-order.
- Local smoke covers default long `300 x 3`, mixed clipped ASCII/NonAscii, 100% / 150% / 200% scale, resize, and AtlasFull/failure paths with no device lost.
- Diagnostics still expose accepted text runs, fallback/degradation runs, per-run reasons, sync/present serials, and failure phases after the overlay path is removed.
- A rollback story exists that does not reintroduce D3D11On12 / D2D as a hidden dependency.

Until that gate is met, the overlay path remains the correctness fallback and `--text-composition overlay` remains the rollback mode.

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
