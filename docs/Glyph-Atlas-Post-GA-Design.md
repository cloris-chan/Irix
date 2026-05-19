# Glyph Atlas Post-GA Design

> Design note for issue #2. This is post-GA renderer work. The explicit overlay rollback still uses DirectWrite / Direct2D over D3D11On12, but the default GlyphAtlas path no longer uses that overlay for unsupported text.

## Goal

Introduce an explicit glyph atlas/cache after V1 GA only if profiling shows DirectWrite / Direct2D text rendering is the limiting cost or if a future backend needs portable glyph resources. The design must not change the current `IDrawingBackend` public contract until a separate API review accepts it.

## Phase 1 Composition Seam

The first post-GA renderer-foundation change introduced an internal text composition mode seam only. After opt-in smoke evidence, the D3D12 PoC baseline now defaults to `GlyphAtlas`; `--text-composition overlay` remains the old overlay rollback. The current default path keeps accepted atlas runs in the D3D12 text pass and explicitly degrades unsupported renderable runs instead of sending them through overlay. Initialization or runtime record failure degrades every renderable text run in that frame. The preserved overlay path is reachable only through explicit `Overlay` mode and remains `D3D12 rect pass -> D3D11On12 / D2D / DirectWrite overlay -> sync wait -> Present`.

DirectWrite is retained as a shaping, metrics, and glyph bitmap source for the atlas path. The near-term goal is to remove D3D11On12 / D2D / DirectWrite from final overlay composition, not to remove DirectWrite from text processing. No public API or `IDrawingBackend.Execute` signature changes are part of this phase.

The first executable atlas path is intentionally narrow but default-on in the post-GA renderer-foundation branch. Basic single-line ASCII / `NoWrap` runs may be rasterized from DirectWrite glyph analysis into a D3D12 `R8_UNORM` atlas texture and drawn as D3D12 glyph quads before command-list close/execute. Per-run scissor clipping is supported for accepted runs. Unsupported runs, including complex shaping, non-ASCII fallback faces, wrapping, missing fonts, atlas-full conditions, vertex/batch limits, and initialization/upload failures, are counted as explicit degradation in default `GlyphAtlas` mode until the atlas path is correctness-complete for those cases.

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
| Fallback | Default `GlyphAtlas` degrades unsupported or failed renderable runs without overlay; `--text-composition overlay` remains explicit rollback |
| Mixed ordering | Rect pass -> atlas accepted runs -> Present; degraded runs are not drawn |
| Diagnostics | `AtlasRuns`, `OverlayFallbackRuns` for explicit overlay legacy visibility, `DegradedRuns`, fallback/degradation frames, unsupported runs, and reason counts |
| Not implemented | Non-ASCII shaping, fallback font identity, color glyphs, wrapping, eviction, and recovery beyond explicit degradation |

Phase 1 closeout: local evidence has been captured for default overlay regression, opt-in glyph-atlas ASCII smoke, NonAscii and AtlasFull fallback, resize, 100% / 150% / 200% scale, and warm allocation baseline. The post-GA default baseline is now `GlyphAtlas` with overlay rollback. The next phase should focus on renderer-foundation hardening, especially shader bytecode/resource lifetime, rather than expanding the ASCII prototype surface.

P1 hardening update: runtime shader compilation has been removed from the D3D12 rectangle pass and glyph-atlas pass. Both use embedded DXBC bytecode, and `D3DCompile` / `d3dcompiler_47.dll` are no longer part of the renderer source generation list.
Glyph-atlas initialization failures remain phase-tagged and degrade renderable text without invoking overlay.
Runtime record/upload/map failures disable the atlas instance and degrade renderable text with `recordFailurePhase` diagnostics; they are not reported as device lost unless the renderer observes an actual device-removed condition.
D3D12 upload map paths now unmap in `finally` after a successful map, covering rectangle vertices, glyph vertices, and atlas uploads.
D3D12 swapchain creation now releases the DXGI factory and intermediate `IDXGISwapChain1` in `finally`; constructor and recovery use the same helper.
D3D12 core device/queue/RTV/command/fence setup is also shared by constructor and recovery, with pointer guards and null-safe cleanup for partial initialization failure.
D3D12 overlay rollback setup guards D3D11On12/D2D/DirectWrite COM creation and releases DXGI/D2D/frame-wrapping intermediates on constructor or resize recreation failure.

Default degradation update: `D3D12GlyphAtlasTextRenderer.TryRecord` is now an internal record result rather than a bool-only gate. It records atlas quads for accepted runs and counts unsupported or failed renderable runs as degradation.
`D3D12Renderer` no longer passes a GlyphAtlas fallback subset to `D3D12TextRenderer.Render(...)`; overlay rendering is reserved for explicit `--text-composition overlay`.
`IDrawingBackend.Execute` and the public drawing contract remain unchanged. Expanded 2026-05-19 smoke covers mixed `ASCII / NonAscii / clipped ASCII / clipped NonAscii` frames and default `300 x 3` long sync.
The same evidence file includes mixed AtlasFull stress from the former overlay fallback behavior. Current unit coverage pins AtlasFull and record-failure diagnostics as `DegradedRuns` with `recordFailurePhase`, and 2026-05-20 short local smoke shows mixed degradation plus MixedAtlasFull with `overlayFallbackRuns=0`, `syncWaits=0`, and nonzero `DegradedRuns`.

## Default Degradation Path

Frame ordering is fixed:

```text
D3D12 rect pass -> D3D12 glyph atlas accepted text runs -> Present
```

This preserves the current renderer's broad visual model: rectangles are drawn first, then text is drawn over rectangles.
It does not draw unsupported text in default `GlyphAtlas` mode, so unsupported text no longer has an overlay z-order problem; it has an explicit non-rendering degradation contract.
The mixed diagnostic scene intentionally uses `atlas -> degraded -> atlas -> degraded` text order to keep this limitation visible.
Removing the limitation requires D3D12 handling for the unsupported cases or an accepted product decision that those cases may remain degraded.

`TryRecord` classifies each renderable text run:

- Accepted ASCII / `NoWrap` runs record atlas vertices and increment `AtlasRuns`.
- Unsupported runs increment `DegradedRuns` with a per-run reason.
- Empty or zero-size text runs are ignored by both atlas and degradation counts.
- Atlas initialization failure or runtime record failure degrades every renderable run for that frame.

`D3D12TextRenderer.Render(...)` remains the explicit overlay rollback drawing primitive and receives the full text run span only in `Overlay` mode.

Overlay subset parity tests remain as rollback coverage only. Default `GlyphAtlas` correctness now depends on degradation diagnostics carrying accepted/degraded run counts, reasons, and failure phases without constructing a D3D11On12 / D2D overlay subset.

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

`initFailurePhase` is reserved for constructor-time atlas setup failures: DirectWrite factory, font collection, root signature, shader bytecode decode, PSO, atlas texture, upload buffer, descriptor heap, SRV, or vertex buffer creation. These failures prevent atlas creation and degrade renderable text without invoking overlay.

`recordFailurePhase` is reserved for runtime command recording failures after atlas initialization succeeds. Current phases cover generic record failure plus vertex-buffer map and atlas-upload map failures. A runtime record failure disables the current atlas renderer instance and degrades renderable text for subsequent frames without marking the D3D12 device lost by itself.

Known limitations:

- Shader bytecode is currently embedded inline. A future build-time shader asset pipeline can replace the inline packaging if shader source grows, but the runtime compiler dependency is removed.
- Default GlyphAtlas has explicit text degradation limits: unsupported runs are not drawn until they get D3D12 handling or an accepted product degradation contract.
- Atlas eviction is not implemented; AtlasFull degradation is the safety behavior for the current no-eviction prototype.
- Complex shaping, fallback font face identity, color glyphs, and wrapping are still degradation cases.
- Warm glyph-atlas scroll allocation was previously about `6.2 KB/frame`; `--diagnose-text-cache` now prints tree/diff/translate/render allocation attribution. Optimization should wait for the attributed evidence rather than guessing.
- Warm allocation follow-up should start with tree construction and translator attribution. Current local evidence attributes the warm scroll sample mostly to tree construction and translation rather than renderer submit.

## Non-Goals

- No implementation before the current GA candidate.
- No renderer rewrite.
- No public API change in the current V1 branch.
- No replacement of DirectWrite shaping in the first spike.
- No promise that glyph atlas renders every script or effect before the removal gate; unsupported cases may require explicit non-rendering degradation instead of Direct2D fallback.

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

Direct2D / DirectWrite remains available only through explicit overlay rollback. Current default GlyphAtlas behavior is mixed per renderable run: accepted ASCII / `NoWrap` runs draw through the atlas, while unsupported or failed runs are explicitly degraded and counted without D3D11On12 / D2D final composition. The active migration target is to replace each degradation case with D3D12 handling where practical and keep only accepted degradation where not. Initialization and runtime record failures currently degrade every renderable run for the affected frame because no safe atlas command list should be submitted from a failed record path.

Fallback cases:

- Complex color glyphs not supported by the first atlas format.
- Scripts or shaping features not yet covered by the glyph-run conversion.
- Atlas full and eviction cannot safely free space for the current frame.
- Glyph atlas initialization or upload failure.
- `--text-composition overlay` forces Direct2D text for A/B comparison and rollback.

Fallback or degradation must preserve renderer stability, diagnostics, and clip semantics. New work should not add more D3D11On12 / D2D dependency. D3D11On12 / D2D is no longer required by default GlyphAtlas for NonAscii, complex shaping, wrapping, atlas-full safety, or runtime failure fallback; those cases are either handled by D3D12 atlas or reported as degradation.

## Overlay Removal Gate Draft

D3D11On12 / D2D overlay can be deleted only after all of these are true:

- NonAscii, font fallback, wrapping, alignment, color glyphs, clipping, and scale cases have a non-overlay D3D12 text path or an explicitly accepted non-rendering degradation.
- Atlas-full and eviction behavior are implemented and smoke-tested without needing whole-frame overlay fallback. Current default behavior degrades AtlasFull; eviction remains unimplemented.
- Mixed default composition no longer depends on overlay for unsupported runs; remaining work is replacing accepted degradation with D3D12 rendering where required.
- Local smoke covers default long `300 x 3`, mixed clipped ASCII/NonAscii, 100% / 150% / 200% scale, resize, and AtlasFull/failure paths with no device lost.
- Diagnostics still expose accepted text runs, fallback/degradation runs, per-run reasons, sync/present serials, and failure phases after the overlay path is removed.
- A rollback story exists that does not reintroduce D3D11On12 / D2D as a hidden dependency.

Until that gate is met, `--text-composition overlay` remains the explicit rollback mode. The default path should keep reducing `DegradedRuns` without reintroducing D3D11On12 / D2D as a hidden dependency.

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
- Direct2D fallback remains available and tested until the removal gate is satisfied; new fallback work should prefer D3D12 handling or explicit degradation.
