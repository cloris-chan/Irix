# Glyph Atlas Post-GA Design

> Design note for issue #2. This is post-GA renderer work. D3D11On12 / Direct2D final text overlay has been removed from the active Windows renderer; DirectWrite remains only as the glyph metrics/raster source for the atlas path.

## Goal

Introduce an explicit glyph atlas/cache after V1 GA for D3D12 text composition and future portable glyph-resource work. The design must not change the current `IDrawingBackend` public contract until a separate API review accepts it.

## Phase 1 Composition Seam

The first post-GA renderer-foundation change introduced an internal text composition seam. After opt-in smoke evidence, the D3D12 PoC baseline switched to `GlyphAtlas`, then the D3D11On12 / Direct2D overlay rollback was removed. The current path keeps accepted atlas runs in the D3D12 text pass and explicitly degrades unsupported renderable runs. Initialization or runtime record failure degrades every renderable text run in that frame.

DirectWrite is retained as a shaping, metrics, and glyph bitmap source for the atlas path. The near-term goal is to reduce `DegradedRuns` by adding D3D12 handling for more text cases, not to reintroduce Direct2D final composition. No public API or `IDrawingBackend.Execute` signature changes are part of this phase.

coverage freeze until oracle/regression split: New script or glyph-image-format support is frozen until the shaping oracle, regression matrix lane, and degradation-policy tests are split and stable. Allowed changes during this freeze are bug fixes, source guards, diagnostics, tests, local evidence updates, renderer structure cleanup, and documentation. New accepted coverage must not be added opportunistically in `D3D12GlyphAtlasTextRenderer`; it needs a matching oracle/regression case first.

The first executable atlas path is intentionally narrow but default-on in the post-GA renderer-foundation branch. ASCII, Latin Extended, Greek, and Cyrillic BMP runs may be rasterized from DirectWrite glyph analysis into a D3D12 `R8_UNORM` atlas texture and drawn as D3D12 glyph quads before command-list close/execute when the selected DirectWrite face provides direct glyph indices. A first shaped-run path also accepts shaped runs when DirectWrite returns nonzero glyph indices, including system fallback font segmentation for mixed base/fallback runs such as ASCII+CJK text, LTR complex-script runs after DirectWrite script/bidi analysis, single-level RTL `NoWrap` segments, RTL-base wrapped/mixed lines drawn from the line's right edge including leading weak digits before the first RTL strong character, and mixed BiDi spans split into resolved-level runs with nested visual ordering for LTR and RTL-base lines. `NoWrap` still clips over-wide line segments; explicit CR/LF creates multiple line segments. Tab is treated as a fixed four-space advance control token and is not rasterized as a glyph, including inside accepted shaped runs. `Wrap` supports minimal whitespace-based multi-line layout, including shaped runs whose cluster map is monotonic enough to project per-character advances; unbreakable over-wide wrap words and over-height line stacks stay accepted and are clipped by the per-run text scissor. Per-run scissor clipping is supported for accepted runs. Unsupported runs, including BiDi cases beyond current resolved-level segment projection, missing glyphs, missing fonts, atlas-full conditions, vertex/batch limits, and initialization/upload failures, are counted as explicit degradation in default `GlyphAtlas` mode until the atlas path is correctness-complete for those cases.

Current implementation status:

| Area | Current status |
|------|----------------|
| Public API | No change; `IDrawingBackend.Execute` remains unchanged |
| Default composition | `GlyphAtlas`; removed `Overlay` runtime composition |
| Supported atlas text | ASCII printable characters, simple Latin Extended / Greek / Cyrillic BMP characters covered by the selected DirectWrite face, shaped runs with nonzero glyph indices, system fallback font shaping segmented by DirectWrite `MapCharacters`, explicit CR/LF line breaks, tab as four-space advance control, no-wrap line segments that fit, minimal `Wrap` runs that can break at whitespace, over-height line stacks clipped by scissor, DirectWrite glyph metrics/raster source |
| Atlas format | `R8_UNORM` alpha atlas pages for alpha masks and outline/COLR color layers; `B8G8R8A8_UNORM` pages for DirectWrite premultiplied BGRA glyph image data and WIC-decoded PNG/JPEG/TIFF glyph images. Bitmap glyph entries keep atlas pixel UVs separate from draw dimensions so non-scalable image data is scaled by the DirectWrite-returned `pixelsPerEm`. |
| Draw model | D3D12 glyph quads recorded before command-list close/execute |
| Shader packaging | Embedded DXBC bytecode; no runtime `D3DCompile` dependency in the D3D12 rect or glyph-atlas pass |
| Alignment | Leading, center, and trailing are supported per accepted line |
| Clip | Per-run scissor clip supported for accepted atlas runs |
| Fallback | Unsupported or failed renderable runs degrade without overlay |
| Mixed ordering | Rect pass -> atlas accepted runs -> Present; degraded runs are not drawn |
| Shaped text | DirectWrite `IDWriteTextAnalyzer` and system `IDWriteFontFallback` are initialized for the atlas renderer. `NonAscii` runs shape through pinned `ReadOnlySpan<char>` input after DirectWrite `AnalyzeScript` / `AnalyzeBidi` fill renderer-owned script and bidi scratch; selected-face missing glyphs can retry through DirectWrite `MapCharacters` segments where each segment carries its own cached font face and effective em size. Output is projected into renderer-owned `ShapedGlyph`, segment, and explicit-line scratch and exposed only as a synchronous `ShapedGlyphRun` span view. Tabs are represented as zero-glyph control segments with measured four-space advance. Unbreakable wrap words and over-height line stacks are accepted and clipped by text scissor. Surrogate pairs and variation selectors shape into segments and, where DirectWrite exposes outline/COLR color glyph runs, render through D3D12 atlas layer quads with per-layer vertex color; BGRA glyphs use `IDWriteFontFace4.GetGlyphImageData` and `Bgra` atlas pages, while PNG/JPEG/TIFF glyph images decode through WIC into the same BGRA atlas path. BGRA and encoded bitmap glyphs scale their draw bounds/origins from returned glyph-image `pixelsPerEm` instead of assuming the returned pixel size exactly matches the requested em size, and draw with source image color preserved while applying only the text alpha. SVG and COLR paint-tree-only glyph formats remain aggregate `ColorGlyph` degradation with split counters. LTR complex-script runs draw through the atlas; odd-level `NoWrap` shaped spans draw from the segment's right edge; RTL-base shaped lines whose first strong character is RTL draw from the line's right edge, including leading weak digits and Hebrew/Arabic presentation-form ranges; mixed-level shaped lines apply nested resolved-level visual ordering before glyph placement. Probe/raster failure stays degradation-only and does not disable the atlas. |
| Diagnostics | `atlasPages`, `atlasAlphaPages`, `atlasBgraPages`, `atlasBudgetPages`, `atlasPage`, `atlasCapacity`, resident CPU shadow bytes, resident D3D12 upload-buffer bytes, resident D3D12 atlas texture bytes, total and Alpha/Bgra-split `atlasEvictions`, `atlasPendingPageReuses`, `atlasPageReuseRequests`, and `atlasFullWithoutPageReuse`, total and Alpha/Bgra-split `atlasUsed` / `atlasFragmented`, `atlasRecordSerial`, total and Alpha/Bgra-split atlas page age metrics, `AtlasRuns`, `DegradedRuns`, upload bytes/new glyphs, shaped probe run/glyph counts, accepted color layer/bitmap run counts, fallback/degradation frames, unsupported runs, aggregate `NonAscii`, split `ColorGlyph` / `ComplexScript`, color-glyph image format counters for SVG/PNG/JPEG/TIFF/premultiplied BGRA/COLR paint-tree, and other reason counts |
| Not implemented | SVG and COLR paint-tree color glyph rendering, BiDi cases beyond current resolved-level segment projection, full LRU/entry-level eviction, and recovery beyond explicit degradation |

Phase 1 closeout: local evidence has been captured for default overlay regression, opt-in glyph-atlas ASCII smoke, NonAscii and AtlasFull fallback/degradation, resize, 100% / 150% / 200% scale, and warm allocation baseline. The post-GA default baseline is now `GlyphAtlas` without D3D11On12 / Direct2D final composition. The next phase should focus on resource ownership and reducing accepted degradation, rather than expanding the ASCII prototype surface blindly.

Regression matrix update: `--diagnose-glyph-atlas-matrix` now records a fixed broad smoke scene for ASCII, Latin Extended, Greek, Cyrillic, CJK fallback-face shaping, Arabic, Hebrew, mixed BiDi, emoji/COLR layer candidates, wrap, tab, and CRLF. The runner emits both expected per-frame classification and the current explicit degradation contract: SVG color glyphs, COLR paint-tree-only color glyphs, BiDi cases beyond current resolved-level projection, AtlasFull after the bounded page budget, record failure, and initialization failure may degrade, but `overlayFallback=False` remains fixed.

POST-011 resource-handle update: glyph cache entries and atlas pages are now referenced through stable value handles with generations. Glyph entries and draw batches bind to atlas page handles, page-owned texture/upload/SRV/pixel/packing state replaces naked renderer-level atlas resource fields, and glyph/page cache touches carry a monotonic atlas record serial. The renderer grows a bounded 1024x1024 atlas pool on demand from one page up to a 48-page budget, exposes the page budget and total pixel capacity in diagnostics, and switches to a cold page when the active page cannot fit a glyph. When all pages of a requested format are full, AtlasFull records a format-scoped page reuse request with the triggering atlas record serial; Alpha and Bgra pages use separate pending slots. The tested reuse gate can only apply on a later glyph record and only after the retained-frame floor has advanced beyond the triggering record, with a page generation bump, so retained or current-record accepted glyph quads cannot sample recycled regions. Per-text-run atlas mutations are scoped: if a run degrades before draw, newly cached entries are removed and page packing/dirty/touch state is restored, and pages created by the failed run are released. If a page generation was reused, the reused page remains generation-reset and failed-run entries are still removed. The current D3D12 glyph-atlas implementation does not retain glyph draw batches across `TryRecord`, so it passes the current record serial as that floor. Any future retained atlas command cache must pass its actual oldest retained atlas record serial or refuse page reuse. Glyph cache keys now use a `GlyphAtom` value rather than a raw UTF-16 `char`, with the current simple-codepoint atom carrying both Unicode code point and DirectWrite glyph index; this leaves the cache ready for a future shaped-glyph atom without storing strings in the renderer core path.
Atlas page format split update: page resources now carry `Alpha` / `Bgra` format metadata, derive row pitch and upload buffer size from bytes per pixel, create SRV/texture footprints from the page DXGI format, and scope writable-page selection plus cold-page reuse scheduling to the requested format. Alpha masks and DirectWrite outline/COLR layers allocate `Alpha` pages; premultiplied BGRA glyph image data allocates `Bgra` pages.
Soak/page-policy update: `--diagnose-glyph-atlas-soak [frames] --pressure-every N` alternates fixed regression-matrix frames, wrap frames, reuse frames, and pressure frames, then reports peak page counts, resident CPU/GPU bytes, peak used/fragmented pixels, pending and completed reuse counters, split Alpha/Bgra full-without-reuse counters, and max degradation. The policy line reads the atlas page budget from the renderer's `AtlasPageBudget` constant rather than duplicating a diagnostic-side literal, and is intentionally explicit: `pageReuse=FormatScopedColdPage`, `retainedFloorGate=True`, `currentRecordColdReuse=True`, `sameRecordTouchedReuse=False`, `entryLru=False`, and `subRectFreeList=False`. Current-record reuse is allowed only for cold pages whose last touch serial is older than the current record; retained-floor-gated next-record reuse remains the safety path when no immediate cold page is available.
Regression lane update: `scripts/glyph-atlas-regression.ps1` runs the fixed matrix, soak threshold, BiDi oracle, and glyph oracle diagnostics into `TestResults` with summary files. This is the local smoke lane for the coverage freeze; it monitors page policy, resident bytes, fragmentation/reuse counters, degradation policy, and DirectWrite structural oracle output without adding renderer coverage.
Entry eviction design update: entry-level LRU and a sub-rect free-list remain design-only until retained atlas command ownership exposes the oldest retained atlas record serial. The current implementation must stay on page-level format-scoped cold reuse; any future entry eviction must preserve entry/page generation validation, retained-floor safety, rollback of failed run allocations, and explicit degradation on unsafe reuse.
Premultiplied BGRA update: BGRA-capable color glyph segments query `IDWriteFontFace4.GetGlyphImageData(PREMULTIPLIED_B8G8R8A8)`, copy the returned raw pixel data into `B8G8R8A8_UNORM` atlas pages, release the DirectWrite image-data context in `finally`, cache entries through `GlyphAtom.BgraGlyph`, and draw with a separate embedded BGRA pixel shader selected per draw batch page format. If DirectWrite reports a bitmap format flag combination, BGRA is selected before encoded PNG/TIFF/JPEG so the direct premultiplied source path wins. The BGRA pixel shader converts premultiplied texture samples to the existing straight-alpha blend contract, so it does not introduce a separate D3D12 blend state or any D3D11On12/D2D fallback.
Encoded bitmap glyph update: PNG/JPEG/TIFF color glyph image data now stays D3D12-only by calling `IDWriteFontFace4.GetGlyphImageData`, decoding the encoded bytes through WIC into `32bppPBGRA`, copying the decoded pixels into `Bgra` atlas pages, and caching entries through `GlyphAtom.EncodedBitmapGlyph`. WIC is used only as a CPU decoder/source-data step; final composition remains the D3D12 atlas pass, and decode/WIC failures degrade explicitly with the existing PNG/JPEG/TIFF counters. BGRA and encoded bitmap entries store atlas UVs from the returned pixel dimensions but scale draw bounds and origins by `fontEmSize / glyphData.pixelsPerEm`, which handles non-scalable raster strikes when DirectWrite returns a different ppem than requested.
Color-glyph format diagnostic update: `--diagnose-glyph-atlas-color-formats [ppem]` probes the selected DirectWrite emoji/color font family, defaulting to Segoe UI Emoji, through both `IDWriteFontFace4.GetGlyphImageFormats` and `IDWriteFactory4.TranslateColorGlyphRun`. `--diagnose-color-glyph-font-file <path>` can also create a DirectWrite face directly from a font file so natural bitmap coverage does not depend on Windows font collection cache refresh. The diagnostic reports direct layer, BGRA, PNG/JPEG/TIFF, SVG, COLR paint-tree, translated color-run count/formats, selected D3D12 bitmap route, `GetGlyphImageData` byte/ppem/size data, and WIC `32bppPBGRA` decode size for encoded bitmap data. This is diagnostic evidence for whether a local machine can naturally produce `atlasBgraPages > 0`; if the installed default font exposes DirectWrite-renderable COLR/layer runs but no bitmap glyph image data, ordinary default-font wrap smoke can remain `atlasBgraPages=0` without implying that the BGRA atlas code path was removed.
Natural color coverage update: the same diagnostic now emits a machine-readable natural coverage line. The default local Segoe UI Emoji result is `status=LayerOnly` with `naturalBgraSmoke=False`, while the downloaded Noto Color Emoji Windows-compatible font-file probe is `status=BitmapRenderableAvailable` with `encodedBitmapRoute=True`, `imageDataRoute=True`, and `decodedBitmapRoute=True`; all eight probes expose PNG image data and decode through WIC to `32bppPBGRA`. Direct BGRA and TIFF natural font coverage remain unobserved locally, but the shared encoded bitmap route now has natural PNG coverage in addition to selector, raster/decode, page-format, diagnostics, and source guards.
Shaped-run update: the renderer now owns an `IDWriteTextAnalyzer`, `IDWriteFactory4`, and `IDWriteFontFallback` alongside the DirectWrite factory/font collection and can use them for `NonAscii` runs. The probe pins resolved frame text as a span, runs DirectWrite script/bidi analysis into scratch, calls the pointer overloads of `GetGlyphs` and `GetGlyphPlacements`, projects glyph index, advance, offset, cluster-start, diacritic, and zero-width flags into local `ShapedGlyph` scratch, and exposes that data through a non-retained `ShapedGlyphRun` span view. Missing-glyph runs can now be segmented through DirectWrite `MapCharacters`; each shaped segment carries its cached font face, effective em size, and resolved bidi level, so mixed ASCII/CJK and single-level RTL runs rasterize through the existing atlas glyph cache and draw path without retaining source strings. Shaped text accepts explicit CR/LF line breaks through renderer-owned line scratch, tabs through zero-glyph control segments, minimal whitespace wrapping by projecting monotonic DirectWrite cluster maps into per-character advances, unbreakable wrap words and over-height line stacks clipped by the existing text scissor, LTR complex-script runs, uniform odd-level `NoWrap` shaped spans drawn from the segment's right edge, RTL-base wrapped/mixed lines whose first strong character is RTL, leading weak-digit RTL lines, and mixed BiDi spans split by resolved bidi level with nested visual ordering. Emoji/VS16 color-glyph candidates now shape far enough to translate `IDWriteColorGlyphRunEnumerator1` runs through `IDWriteFactory4.TranslateColorGlyphRun`, while non-emoji supplementary pairs can remain on the regular shaped glyph route; outline/COLR layer glyphs rasterize into the existing `R8_UNORM` atlas through a distinct color-layer glyph atom, while Factory4 bitmap color runs route into the D3D12 BGRA atlas path and use a white vertex color with text alpha so source bitmap colors are not tinted by the text brush. The Factory4 request is limited to D3D12-renderable layer/bitmap formats; `IDWriteFontFace4` format queries keep SVG/COLR paint-tree-only glyphs in explicit `ColorGlyph` degradation rather than misclassifying them as layer glyphs. Diagnostics preserve aggregate `NonAscii` while splitting remaining unsupported cases into `ColorGlyph` and `ComplexScript`.
BiDi oracle update: unit coverage now pins the current resolved-level visual-order projection with nested LTR/RTL examples. This is not a pixel oracle against DirectWrite layout yet; it is the structural oracle for the atlas helper that applies resolved-level segment ordering before glyph placement.
DirectWrite BiDi oracle update: `--diagnose-glyph-atlas-bidi-oracle` runs fixed LTR/RTL mixed samples through `IDWriteTextAnalyzer.AnalyzeBidi`, then feeds the DirectWrite-resolved levels into the same atlas `ApplyBidiVisualOrder` helper. The diagnostic reports per-character resolved levels, logical level runs, helper-projected visual runs, and character visual order. It intentionally does not use `IDWriteTextLayout`, Direct2D, D3D11On12, or overlay fallback; a full DirectWrite-layout pixel/glyph-run oracle remains future correctness work beyond the current resolved-level projection.
DirectWrite glyph oracle update: `--diagnose-glyph-atlas-glyph-oracle` is a diagnostic-only shaping/layout-data oracle. It uses DirectWrite analyzer and font fallback output for fixed ASCII, CJK fallback, Arabic RTL, mixed BiDi, and tab/CRLF probes, then reports glyph count, glyph indices, advances, offsets, resolved bidi levels, line-break flags, and fallback-font segments. It does not use `IDWriteTextLayout`, Direct2D rendering, D3D11On12, or overlay fallback, and it does not expand renderer coverage during the coverage freeze.
Atlas residency diagnostic update: glyph atlas summaries now report resident CPU shadow bytes, resident D3D12 upload-buffer bytes, and resident D3D12 texture bytes derived from live `Alpha`/`Bgra` atlas page counts. These bytes make page pressure visible separately from `atlasUsed` glyph pixels and per-frame uploaded bytes; the cache remains a CPU-shadow plus D3D12 upload/texture design, with final composition in D3D12.

P1 hardening update: runtime shader compilation has been removed from the D3D12 rectangle pass and glyph-atlas pass. Both use embedded DXBC bytecode, and `D3DCompile` / `d3dcompiler_47.dll` are no longer part of the renderer source generation list.
Glyph-atlas initialization failures remain phase-tagged and degrade renderable text without invoking overlay.
Runtime record/upload/map failures disable the atlas instance and degrade renderable text with `recordFailurePhase` diagnostics; they are not reported as device lost unless the renderer observes an actual device-removed condition.
D3D12 upload map paths now unmap in `finally` after a successful map, covering rectangle vertices, glyph vertices, and atlas uploads.
D3D12 swapchain creation now releases the DXGI factory and intermediate `IDXGISwapChain1` in `finally`; constructor and recovery use the same helper.
D3D12 core device/queue/RTV/command/fence setup is also shared by constructor and recovery, with pointer guards and null-safe cleanup for partial initialization failure.
D3D12 rectangle vertex, glyph vertex, and per-page atlas upload resources are now frame-slot owned. `BeginFrame` no longer needs a coarse last-submitted-frame upload wait; normal swapchain frame-slot fencing protects each upload resource before reuse.
Glyph-atlas record-time resource guards classify missing command list, DirectWrite factory/Factory4/font collection/text analyzer/font fallback/font face/glyph-run-analysis resources, glyph pipeline state/root signature, glyph vertex upload buffer, atlas texture/upload buffer, and atlas SRV resources as typed record failures instead of allowing null GPU resource binding.
D3D11On12 / Direct2D overlay renderer sources, sync strategy, native method generation entries, and diagnostics have been removed from the active source tree.

Default degradation update: `D3D12GlyphAtlasTextRenderer.TryRecord` is now an internal record result rather than a bool-only gate. It records atlas quads for accepted runs and counts unsupported or failed renderable runs as degradation.
`D3D12Renderer` no longer passes a GlyphAtlas fallback subset to an overlay renderer; `D3D12TextRun` is the platform text-run IR consumed by the glyph-atlas path.
`IDrawingBackend.Execute` and the public drawing contract remain unchanged. Expanded 2026-05-19 smoke covers mixed `ASCII / NonAscii / clipped ASCII / clipped NonAscii` frames and default `300 x 3` long sync.
The same evidence file includes mixed AtlasFull stress from the former overlay fallback behavior. Current unit coverage pins AtlasFull and record-failure diagnostics as `DegradedRuns` with `recordFailurePhase`, and 2026-05-20 short local smoke shows mixed degradation plus MixedAtlasFull with `syncWaits=0` and nonzero `DegradedRuns`.

## Default Degradation Path

Frame ordering is fixed:

```text
D3D12 rect pass -> D3D12 glyph atlas accepted text runs -> Present
```

This preserves the current renderer's broad visual model: rectangles are drawn first, then text is drawn over rectangles.
It does not draw unsupported text in default `GlyphAtlas` mode, so unsupported text no longer has an overlay z-order problem; it has an explicit non-rendering degradation contract.
The mixed fallback diagnostic now covers `ASCII -> mixed ASCII/CJK fallback-face -> clipped ASCII -> mixed CJK/ASCII fallback-face` and is expected to remain atlas-only.
Removing the remaining limitation requires D3D12 handling for unsupported glyph/color cases, BiDi cases beyond current resolved-level segment projection, or an accepted product decision that those cases may remain degraded.

`TryRecord` classifies each renderable text run:

- Accepted ASCII/simple BMP no-wrap runs, explicit-line-break/tab runs, and whitespace-wrapped simple runs record atlas vertices and increment `AtlasRuns`.
- Unsupported runs increment `DegradedRuns` with a per-run reason.
- Empty or zero-size text runs are ignored by both atlas and degradation counts.
- Atlas initialization failure or runtime record failure degrades every renderable run for that frame.

Default `GlyphAtlas` correctness depends on degradation diagnostics carrying accepted/degraded run counts, reasons, and failure phases without constructing a D3D11On12 / D2D overlay subset.

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

`recordFailurePhase` is reserved for runtime command recording failures after atlas initialization succeeds. Current phases cover generic record failure, missing command-list input, DirectWrite runtime lookup/measurement/rasterization/fallback-resource failure, atlas page handle resolution, pipeline binding prerequisites, vertex-buffer map failure, atlas-upload map failure, and atlas draw resource binding. A runtime record failure disables the current atlas renderer instance and degrades renderable text for subsequent frames without marking the D3D12 device lost by itself.
Stale atlas page handles in active-page, pending-reuse, or draw-batch resolution are classified as `AtlasPage` record failures rather than leaking ordinary invalid-operation exceptions.

Known limitations:

- Shader bytecode is currently embedded inline. A future build-time shader asset pipeline can replace the inline packaging if shader source grows, but the runtime compiler dependency is removed.
- Default GlyphAtlas has explicit text degradation limits: unsupported runs are not drawn until they get D3D12 handling or an accepted product degradation contract.
- Full LRU/entry-level eviction is not implemented; AtlasFull degradation plus retained-floor-gated next-record page reuse is the current bounded safety behavior.
- SVG and COLR paint-tree color glyph formats plus BiDi cases beyond current resolved-level segment projection are still degradation cases. DirectWrite premultiplied BGRA glyph image data and WIC-decoded PNG/JPEG/TIFF glyph images are handled by the D3D12 `Bgra` atlas path.
- Warm glyph-atlas scroll allocation was previously reported at about `6.2 KB/frame`; the corrected frame-scoped `--diagnose-text-cache 30` scroll sample is about `2.8 KB/frame`.
- Warm allocation follow-up should start with layout result and retained snapshot boundaries. Current local scroll evidence is `tree=953 B/frame`, `diff=273 B/frame`, `translate=1631 B/frame`, and `render=273 B/frame`; the translate breakdown is `pipeline=1631 B/frame` with retained apply, viewport, and feedback at `0 B/frame`. The pipeline breakdown is `layout=811 B/frame`, `hitTargets=273 B/frame`, `snapshot=273 B/frame`, `retainedFrame=273 B/frame`, and `record=0 B/frame`, so renderer submit, translator shell work, and draw recording are not the primary remaining costs.

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
| Format | `R8_UNORM` for alpha masks and DirectWrite color-run layers via per-layer vertex color; `BGRA8` pages for DirectWrite premultiplied BGRA glyph image data |
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
- Emit diagnostics: total plus per-format page count, used pixels, fragmentation estimate, page age, upload bytes/frame, evictions/frame, misses/frame.

Current POST-011 implementation is a conservative bounded multi-page subset of this policy: glyph allocation starts with one page and grows on demand up to a 48-page budget, draw batches split on page changes, writable page selection prefers remaining capacity, and strict-oldest selection chooses cold pages for reuse. AtlasFull marks the coldest page of the requested format for reuse with a record-serial-gated request when every page of that format is full; Alpha and Bgra requests are tracked in separate pending slots. The current frame continues with explicit `AtlasFull` degradation only after the bounded page budget is exhausted. Failed runs roll back newly cached glyphs, non-reuse page mutations, and pages created by the failed run. A later glyph pass removes only live entries from the exact reused page generation, bumps that page generation, resets packing/usage, marks the full page dirty, and uploads the reused page before drawing new accepted runs.

The retained-frame-safe reuse contract is explicit: a pending page reuse may apply only when there is a pending request, `currentRecordSerial > requestedRecordSerial`, and `oldestRetainedRecordSerial > requestedRecordSerial`. The last condition is the retained command boundary. If future renderer work stores atlas draw batches beyond a single `TryRecord`, the retained batch owner must advance `oldestRetainedRecordSerial` only after all batches that can reference the old page generation are gone. The current renderer's atlas draw batches are private, record-local arrays, so the safe floor is the current record serial.

Page diagnostics now expose total plus Alpha/Bgra page counts, the fixed page budget, page dimensions, total atlas pixel capacity, total plus Alpha/Bgra pending/completed page reuse state, scheduled page reuse request count, hard total plus Alpha/Bgra AtlasFull-without-reuse count, used glyph bitmap pixels, a shelf-allocation fragmentation estimate, the atlas record serial, and oldest/newest page age metrics. These are observation fields for deciding page size, multi-page thresholds, BGRA color-glyph pressure, and LRU policy; they do not change current draw or degradation behavior.

## DPI And Scale

Glyph raster output is DPI-specific. A scale change invalidates or partitions glyph entries by physical em size.

Rules:

- Keep logical text measurement in the existing layout pipeline.
- Rasterize glyphs at physical pixel size after `DisplayScale` is applied.
- Do not reuse a 100% glyph bitmap at 150% or 200% by stretching.
- Runtime DPI changes may keep old pages alive briefly for in-flight frames, but new frames must request entries from the new scale partition.

## Upload Path

The first spike should avoid per-glyph GPU synchronization.
Current renderer-foundation code uses frame-slot upload resources for rectangle vertices, glyph vertices, and per-page atlas texture uploads. A future upload allocator can still compact this into a shared ring, but it must preserve per-frame ownership so uploads do not map memory still referenced by the GPU.

Candidate upload flow:

1. Shape text using DirectWrite into glyph runs.
2. Rasterize missing glyphs into CPU staging memory or a DirectWrite-compatible bitmap path.
3. Pack missing glyphs into atlas pages.
4. Batch page updates into one upload list per frame.
5. Draw glyph quads after rectangle pass, using scissor/clip state compatible with current text clipping.
6. Degrade unsupported text explicitly while atlas coverage is incomplete.

Upload diagnostics include bytes uploaded and number of new glyph entries uploaded into the atlas.

## Clip And Layout Interaction

Clipping remains per draw/run, not baked into glyph entries.

Rules:

- Glyph atlas entries contain glyph images only.
- Draw-time quads apply the current effective clip/scissor.
- Text layout still computes line breaks, glyph positions, and run bounds before draw recording.
- Existing `DrawTextRun` clip semantics must remain observable in diagnostics.

## D3D12-only Degradation Policy

DirectWrite remains available as the glyph metrics/raster source. Current default GlyphAtlas behavior is mixed per renderable run: accepted ASCII, simple BMP, shaped fallback-face, color-layer, LTR complex-script, single-level RTL `NoWrap`, RTL-base wrapped/mixed-line including leading weak digits, and mixed BiDi resolved-level segment-ordered runs, including explicit line breaks, tab spacing, minimal whitespace-wrapped lines, unbreakable wrap words, and over-height line stacks clipped by scissor, draw through the atlas, while unsupported or failed runs are explicitly degraded and counted without D3D11On12 / D2D final composition. The active migration target is to replace each degradation case with D3D12 handling where practical and keep only accepted degradation where not. Initialization and runtime record failures currently degrade every renderable run for the affected frame because no safe atlas command list should be submitted from a failed record path.

Accepted degradation cases:

- SVG and COLR paint-tree-only color glyphs beyond DirectWrite outline/COLR color-run layers and DirectWrite bitmap glyph image data are not supported by the current atlas path.
- BiDi beyond the current resolved-level segment projection.
- AtlasFull after the 48-page budget when no safe current-record or retained-floor-gated page reuse is available.
- Glyph atlas record or initialization failure.
- Full LRU/entry-level eviction is not yet implemented; AtlasFull degrades the current run after scheduling safe format-scoped page reuse when possible.

This is not an overlay fallback contract. Degradation must preserve renderer stability, diagnostics, and clip semantics, and must not reintroduce D3D11On12, Direct2D final composition, IDWriteTextLayout, or hidden overlay fallback. NonAscii, complex shaping, color glyph, atlas-full safety, and runtime failure cases are either handled by D3D12 atlas or reported as D3D12-only degradation.

## Overlay Removal Status

D3D11On12 / D2D overlay deletion is active in the renderer source:

- `D3D12TextRenderer`, `TextOverlaySyncStrategy`, and D3D11 query extensions are removed.
- `NativeMethods.txt` no longer requests D3D11On12 or Direct2D generation.
- The only remaining Direct2D-named active symbol is the `Direct2D.Common` `D2D_POINT_2F` value type required by `IDWriteFactory4.TranslateColorGlyphRun`; no Direct2D factory/device/context or D3D11On12 overlay API is generated or used.
- `TextCompositionMode` has no overlay mode; `--text-composition overlay` is rejected, and `GlyphAtlas` is the only active text composition mode.
- Diagnostics expose `atlasPages`, `atlasAlphaPages`, `atlasBgraPages`, `atlasBudgetPages`, `atlasPage`, `atlasCapacity`, resident CPU shadow bytes, resident D3D12 upload-buffer bytes, resident D3D12 atlas texture bytes, total and format-split `atlasEvictions`, `atlasPendingPageReuses`, `atlasPageReuseRequests`, `atlasFullWithoutPageReuse`, `atlasUsed`, `atlasFragmented`, `atlasRecordSerial`, atlas page ages, upload bytes/new glyphs, accepted text runs, accepted color layer/bitmap runs, degradation runs, aggregate `NonAscii`, split `ColorGlyph` / `ComplexScript`, SVG/PNG/JPEG/TIFF/premultiplied BGRA/COLR paint-tree color glyph counters, other per-run reasons, sync/present serials, and failure phases.

Remaining work is reducing `DegradedRuns` without reintroducing D3D11On12 / D2D as a hidden dependency.

## Open Questions

- Should first implementation be alpha-mask atlas or signed-distance-field atlas?
- Should glyph rasterization use DirectWrite glyph run analysis or a separate rasterizer?
- Should the current fixed four-page 1024x1024 atlas budget become adapter/monitor dependent?
- How should retained frames reference atlas entries across page eviction?
- DirectWrite color-run layers use the existing `R8_UNORM` atlas plus per-layer vertex color; DirectWrite premultiplied BGRA glyph image data uses separate `Bgra` atlas pages.

## Acceptance Criteria For A Future Spike

- No public API change without a design review.
- Text diagnostics show reduced degradation on the target workload.
- 100% / 150% / 200% scale smokes show no stretched text and correct hit-test/clip behavior.
- Scroll text remains synchronized at 60Hz without overlay sync.
- Atlas eviction cannot produce stale glyphs in retained or partial frames.
- New fallback work should prefer D3D12 handling or explicit degradation.
