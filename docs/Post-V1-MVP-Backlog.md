# Post-V1 / MVP Backlog

> V1 core is architecture-complete. This document tracks remaining MVP/GA hardening and post-GA renderer work. None of these tasks reopen V1 core.

---

## Windows Version Boundary

Irix v1 Windows PoC separates target SDK from runtime minimum. Windows-targeted projects inherit `IrixWindowsTargetFramework=net10.0-windows10.0.26100.0` and `IrixWindowsSupportedOSPlatformVersion=10.0.15063.0` from `Directory.Build.props`; CI checks for .NET 10 and Windows SDK 10.0.26100.0 before restore/build. The 10.0.15063.0 runtime floor is intentional for PerMonitorV2 DPI awareness and display scale support.

---

## Priority Tiers

### P0 — Completed Partial-Apply / GA Gates

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-001 | Default-on partial apply | Done (2026-05-13) | No longer blocking; `--no-partial-apply` remains rollback |
| POST-002 | D3D12 segmented ownership | Done for v1 default-on path | Resolver ownership, per-segment execute adapter, and D3D12 smoke validated |
| POST-003 | Device-lost recovery | Done | `D3D12Renderer.TryRecover()` and compositor `IDeviceRecovery` path test-covered |
| POST-004 | Platform matrix minimum | Done for available hardware | 60Hz / 120Hz / 240Hz, 100% / 150% / 200% evidence accepted; 144Hz removed from scope because no hardware is available |
| POST-013 | Sync wait overhead validation | Retired by overlay removal | D3D11On12/D2D overlay sync path removed; residual sync wait counters remain diagnostic-only |
| POST-014 | Windows SDK 26100 CI check | Done | CI fails early if .NET 10 or Windows SDK 26100 is absent |
| POST-015 | Platform matrix CI | Minimal matrix added | Windows 2025 lanes cover tests, headless D3D12, performance baseline, AOT publish |
| POST-016 | Performance regression CI | Done | Mock backend frame-time baseline + split frame-stage allocation baseline + warm `FrameDrawingResources` allocation baseline; latest local per-stage bytes live in `Project_Status_and_Todo.md` |

### P1 — Remaining GA/MVP Hardening

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-018 | Platform integration checks | Done for current hardware | Minimize/restore, occlusion, live DPI change, resize, scroll, click, default, and rollback smokes passed |
| POST-019 | GPU memory pressure handling | Done for V1 scope | Runtime resource recreation failures surface typed device diagnostics, including `E_OUTOFMEMORY`; no full GPU memory manager |
| POST-020 | Command allocator reset failure handling | Done | Retry once after `WaitForGpu`, then escalate to device-lost/recovery |

### P1/P2 — Post-GA Renderer Architecture

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-017 | D3D12-only glyph atlas text renderer | Default-on prototype foundation with overlay renderer removed; shaped atlas drawing with DirectWrite fallback-face segmentation, explicit CR/LF/tab/wrap support, over-height line-stack scissor clipping, DirectWrite script/bidi analysis for complex scripts, single-level RTL `NoWrap` and wrapped lines, mixed BiDi segment ordering, DirectWrite outline/COLR color-glyph layer drawing, DirectWrite premultiplied BGRA glyph-image-data drawing, WIC-decoded PNG/JPEG/TIFF glyph-image drawing, returned-ppem scaling for bitmap glyph image data, and format-specific remaining color guards added | Post-GA; `GlyphAtlas` is the only D3D12 PoC text composition path; simple runs, shaped/fallback runs, LTR complex-script runs, single-level RTL lines, mixed BiDi resolved-level runs, COLR-style color layer runs, BGRA-only color glyph image data, and encoded bitmap color glyph image data have local coverage, while SVG/COLR paint-tree color glyph rendering and deeper BiDi shaping cases still degrade explicitly |
| POST-011 | Resource cache / stable global handles | Entry/page handles, on-demand 48-page atlas pool, explicit atlas budget diagnostics, page-owned SRV resources, format-scoped retained-floor-gated next-record cold-page reuse, page usage diagnostics, and atlas touch serials done | D3D12-specific; continue from retained-safe page reuse toward full LRU/entry-level eviction only after resource lifetime remains stable |
| POST-009 | StyleOnly layout skip | Design only | Requires default-on partial apply first; not GA-blocking |
| POST-010 | Retained element tree | Draft | Requires stable retained tree + local patch model |

### P2 — Framework Promotion / Deferred Architecture

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-005 | Translator promotion | Internal seam complete | Translator remains in Irix.Poc; promotion requires typed feedback contract |
| POST-006 | Typed id wrappers / property metadata | Core complete through Round 14 | Typed IDs and typed property metadata are in core; framework promotion and public API shaping remain separate |
| POST-007 | Scroll extraction | Design inventory only | ScrollController/State/Pump remain in Irix.Poc |
| POST-008 | Settings provider | Decision recorded, postponed | Runtime wiring remains postponed; fallback-only internal provider |
| POST-012 | Unified diagnostics channel | Postponed | Would replace per-component diagnostics; not MVP/GA-blocking |

---

## Dependency Graph

```text
POST-001..004, POST-013..020 complete ──> v1.0-private-ga tag

POST-017 D3D12-only glyph atlas text renderer ──> reduce DegradedRuns / widen text coverage
                                             └─ POST-011 D3D12 resource cache / stable handles

POST-005 translator ──> POST-006 public API shaping on top of typed ids/property metadata
POST-007 scroll ──────> POST-008 settings provider
```

---

## POST-017: D3D12-only Glyph Atlas Text Renderer

Retired Private GA overlay fallback text composition path:

```text
D3D12 rect pass -> D3D11On12 / D2D / DirectWrite overlay -> sync wait -> Present
```

Target path:

```text
D3D12 rect pass -> D3D12 glyph atlas text pass -> Present
```

Phase 1 foundation first kept the overlay path as the default runtime behavior and added only an internal composition seam. After opt-in smoke evidence, the post-GA renderer-foundation baseline now defaults to `GlyphAtlas`; the D3D11On12/D2D overlay renderer and explicit overlay mode have now been removed. DirectWrite remains allowed for shaping, glyph metrics, and glyph bitmap source data. This phase does not change public API or `IDrawingBackend.Execute`.

The first atlas execution path records a D3D12 glyph pass for ASCII plus simple Latin Extended / Greek / Cyrillic BMP runs, uses an `R8_UNORM` atlas, and supports leading/center/trailing alignment plus per-run scissor for accepted runs. `NoWrap` clips over-wide line segments and accepts explicit CR/LF line breaks plus tab spacing; `Wrap` accepts minimal whitespace-based multi-line layout and clips unbreakable over-wide words plus over-height line stacks through the text scissor.
It now uses non-overlay degradation so unsupported renderable text runs do not force either whole-frame overlay or mixed overlay fallback.
Expanded smoke covers `ASCII / NonAscii / clipped ASCII / clipped NonAscii`, explicit ASCII line breaks, ASCII tab spacing, simple BMP atlas acceptance, minimal wrap acceptance plus hard-word clipping, default `300 x 3`, and 2026-05-20 short degradation runs with `syncWaits=0` and nonzero `DegradedRuns`.
Current default GlyphAtlas behavior degrades unsupported and initialization/upload/record-failed renderable text runs without invoking overlay. `IDWriteTextAnalyzer` is now created for the atlas path and handles `NonAscii` single-face `NoWrap` shaped runs through pointer-based `GetGlyphs` / `GetGlyphPlacements`, projecting shaped glyph output into local `ShapedGlyph` scratch and a synchronous `ShapedGlyphRun` span view without retaining source strings. Accepted shaped glyphs reuse the existing D3D12 atlas raster/cache/draw path. DirectWrite color-glyph candidates retain the D3D12-only path by translating surrogate/variation-selector segments through `IDWriteFactory4.TranslateColorGlyphRun` and `IDWriteColorGlyphRunEnumerator1` with only D3D12-renderable layer/bitmap formats requested. Outline/COLR layer runs rasterize into the existing `R8_UNORM` atlas with a separate color-layer glyph atom and draw with per-layer vertex color; BGRA color runs call `IDWriteFontFace4.GetGlyphImageData(PREMULTIPLIED_B8G8R8A8)`, upload raw pixels into `Bgra` atlas pages, and draw through a BGRA pixel shader. PNG/JPEG/TIFF color glyphs call `GetGlyphImageData`, decode through WIC into `32bppPBGRA`, upload into the same `Bgra` atlas pages, and draw through the same BGRA shader. SVG and COLR paint-tree-only glyphs remain explicit `ColorGlyph` degradation with per-format diagnostics.
Atlas page resources now have an explicit `Alpha` / `Bgra` format split, so upload buffer sizing, row pitch, texture/SRV DXGI format, writable page selection, draw PSO selection, and cold-page reuse are no longer hard-coded to R8 alpha.
Shaped explicit CR/LF line breaks, tab controls, minimal whitespace wrapping, unbreakable wrap-word and over-height line-stack scissor clipping, LTR complex-script runs after DirectWrite `AnalyzeScript` / `AnalyzeBidi`, single-level RTL `NoWrap` segments, RTL-base wrapped/mixed lines including leading weak digits before the first RTL strong character, Hebrew/Arabic presentation-form RTL classification, and LTR-base mixed BiDi resolved-level segment ordering now remain on the atlas path. SVG/COLR paint-tree color glyph rendering, deeper BiDi shaping cases, eviction, command-order-perfect mixed text z-order for still-degraded text, and production enablement remain follow-up work.

| Work item | Scope | Acceptance criteria |
|-----------|-------|---------------------|
| Atlas design | Define glyph key, atlas page size, eviction, scale/DPI keying, color handling, clipping, and upload lifecycle | Design doc approved; no public backend API change |
| Glyph source | Use DirectWrite only for shaping/raster source if needed; final composition must not use D3D11On12/D2D overlay | Glyph bitmaps can be uploaded to D3D12 textures |
| D3D12 text pipeline | Add glyph quad generation, atlas SRV, pipeline state, blend state, sampler, and scissor support | Text and rectangles are submitted in one D3D12 synchronization domain |
| Diagnostics | Report atlas hit/miss/upload bytes/new glyph counts, `AtlasRuns`, `DegradedRuns`, unsupported runs, and per-run degradation reasons | Diagnostics show whether default composition still depends on degradation |
| Migration | Keep default GlyphAtlas on D3D12 atlas or explicit degradation without D3D11On12/D2D overlay | No regression in renderer stability, clipping, scale, scroll sync, hit-test, partial apply |

Non-goals:

- Do not implement before current GA/MVP unless explicitly re-scoped.
- Do not introduce Skia.
- Do not change `IDrawingBackend.Execute`.
- Do not expose atlas internals as public API.

---

## Execution Plan

### Batch A: Private GA closeout

| Task | Entry File | Acceptance Criteria | Status |
|------|------------|---------------------|--------|
| Accept sync wait budget | `GA-Hardening-Plan.md` | Historical overlay sync budget accepted for Private GA; overlay path now removed post-GA | ✅ Retired by overlay removal |
| Remove 144Hz blocker | `GA-Hardening-Plan.md` | 144Hz validation not listed as current GA blocker | ✅ Done |
| Platform integration checks | `Irix.Poc`, manual smoke | Minimize/restore, occlusion, live DPI, resize + scroll + text sync pass on available hardware | ✅ Done |
| GPU memory pressure handling | `D3D12Renderer.cs` | Resource creation failures produce typed device diagnostics; no undefined pointer continuation | ✅ Done |
| Command allocator reset failure handling | `D3D12Renderer.cs` | Reset retry or device-lost escalation | ✅ Done |
| Keep CI green | GitHub Actions / local CI parity | Tests, D3D12 smoke, performance lane, AOT publish stay green | ✅ Done for Private GA |
| Stop pre-GA performance micro-optimization | `Project_Status_and_Todo.md` | Do not chase the remaining ~2 KB render-request reuse allocation before tag | ✅ Done |
| Private GA tag | Git | Tag committed snapshot as `v1.0-private-ga`; not a public API freeze | ✅ Done |
| Next branch | Git | Open `post-ga-renderer-foundation` after tag | ✅ Done |

### Batch B: Post-GA text renderer replacement

Phase 1 closeout: prototype evidence is captured for default overlay regression, glyph-atlas ASCII smoke, NonAscii/AtlasFull fallback/degradation, mixed clipped degradation, mixed AtlasFull degradation, resize, 100% / 150% / 200% scale, and warm allocation baseline.
The post-GA baseline has been switched to default `GlyphAtlas` plus default `Scissor`, with `--disable-scissor` and `--clip-mode diagnostic` rollback paths.
Do not keep expanding the ASCII prototype surface or flip another runtime default in the next step; move to renderer-foundation hardening first.

| Task | Entry File | Acceptance Criteria | Status |
|------|------------|---------------------|--------|
| Glyph atlas design doc | `Glyph-Atlas-Post-GA-Design.md` | Atlas architecture and migration plan accepted | ✅ Drafted |
| D3D12-only text prototype | `Irix.Platform.Windows` | Draw basic ASCII/simple BMP text runs from atlas in D3D12-only pass | ✅ Default-on prototype; overlay renderer removed |
| Shader/resource lifetime hardening | Windows D3D12 renderers | Runtime shader compile removed; failure diagnostics split; upload maps, swapchain/overlay intermediates, core resource init/release paths, and frame-slot upload buffers are guarded | ✅ First pass done |
| Remove runtime shader compile | `D3D12GlyphAtlasTextRenderer.cs`, `D3D12Renderer2D.cs` | Replace runtime `D3DCompile` / `d3dcompiler_47.dll` dependency with embedded bytecode or build-time compiled shader assets | ✅ Embedded bytecode |
| Attribute warm glyph atlas allocation | `TextCacheAllocationDiagnosticRunner.cs`, diagnostics | Attribute the corrected frame-scoped warm scroll allocation before optimizing | ✅ Attribution added; latest scroll sample about `2.8 KB/frame` |
| Non-overlay degradation path | Renderer design | Per-run atlas plus explicit degradation so NonAscii/complex/failure cases do not invoke D3D11On12/D2D in default GlyphAtlas | ✅ Default GlyphAtlas no longer records overlay fallback runs |
| Overlay removal | Renderer design / smoke evidence | Remove D3D11On12/D2D source, native generation, sync strategy, explicit overlay mode, and legacy CLI alias | ✅ Active source removed; `--text-composition overlay` is rejected |
| Full migration | Glyph-atlas widening | Reduce accepted degradation by adding D3D12 handling | Planned |

Known limitations checklist before expanding text coverage:

- Shader bytecode is embedded inline in the renderer sources. Runtime `D3DCompile` / `d3dcompiler_47.dll` dependency is removed; a build-time shader asset pipeline is optional future cleanup if shader source grows.
- Glyph-atlas diagnostics distinguish constructor-time `initFailurePhase` from runtime `recordFailurePhase`; DirectWrite runtime failures now use their own `DirectWrite` record phase, and runtime record failures disable atlas and degrade renderable text without implying device lost by themselves.
- Stale glyph-atlas page handles in active-page, pending-reuse, and draw-batch resolution classify as runtime record failures.
- D3D12 rectangle and glyph-atlas upload map paths unmap in `finally` after a successful map. Glyph-atlas command-list input, DirectWrite factory/font/analysis resources, vertex upload, page upload, pipeline binding, and draw paths guard missing runtime resources as typed record failures.
- D3D12 rectangle vertex, glyph vertex, and per-page atlas upload resources are frame-slot owned. `BeginFrame` no longer performs a coarse last-submitted-frame upload wait; normal swapchain backbuffer fence ownership protects the upload slot before it is reused.
- D3D12 swapchain creation releases the DXGI factory and intermediate `IDXGISwapChain1` in `finally`; constructor and recovery reuse the same helper.
- D3D12 constructor and recovery share core resource initialization, with pointer guards and null-safe cleanup for partially initialized device resources.
- D3D12 overlay rollback renderer has been removed with its D3D11On12/D2D native generation entries, and `--text-composition overlay` is rejected instead of mapped to a fallback.
- Default GlyphAtlas degrades unsupported renderable runs while accepted ASCII/simple BMP/shaped fallback-face runs stay on atlas. Explicit CR/LF line breaks and tab spacing are supported; minimal `Wrap` support breaks at ASCII spaces/tabs only; initialization and runtime record failure degrade all renderable runs for the frame.
- Default GlyphAtlas no longer has mixed overlay z-order risk because degraded runs are not drawn; replacing degradation with D3D12 rendering remains follow-up work.
- No same-frame atlas eviction. AtlasFull degradation is safe for the current prototype; it schedules tested record-serial- and retained-floor-gated next-record cold-page reset/reuse requests so accepted runs retained from the triggering record cannot sample recycled regions.
- Glyph atlas cache entries, draw batches, and atlas pages now have stable value handles and generations internally; page-owned texture/upload/SRV resources replace renderer-level atlas resource fields. Cache hits and new glyph rasterizations touch glyph entries/pages with a monotonic atlas record serial. The renderer grows the atlas pool on demand from one page up to a bounded 48-page budget, switches pages when the active page is full, uses strict-oldest cold-page selection for reuse, selects writable pages by remaining packing capacity, splits draw batches on page changes, records format-scoped Alpha/Bgra page reuse requests with the triggering atlas record serial, generation-guards reused-page cache cleanup after the request becomes applicable on a later record whose retained-frame floor has advanced beyond the triggering record, resets reused pages to a full-page dirty upload state, and rolls back newly cached glyph entries plus non-reuse page packing mutations and pages created by failed runs when a text run degrades before draw.
- Glyph atlas diagnostics report page count, fixed page budget, page dimensions, total atlas pixel capacity, completed and pending next-frame reuse counts, scheduled page reuse requests, hard AtlasFull-without-reuse counts, used glyph bitmap pixels, a shelf fragmentation estimate, atlas record serial, oldest/newest page age metrics, upload bytes, uploaded glyph count, and split color/complex degradation reasons for future page-size, LRU, and unsupported-text policy decisions.
- SDF/MSDF, SVG/COLR paint-tree color glyph rendering and deeper BiDi shaping cases remain outside the atlas path. Surrogate pairs and variation selectors now shape far enough to draw DirectWrite outline/COLR color layers through D3D12 when available, BGRA glyph image data uses the D3D12 `Bgra` atlas path, and PNG/JPEG/TIFF glyph image data decodes through WIC into the same BGRA atlas path; `IDWriteFontFace4` format checks prevent remaining unsupported formats from being misaccepted as layer glyphs. Complex-script candidates run through DirectWrite script/bidi analysis, even-level LTR runs are accepted, odd-level `NoWrap` segments draw from the segment's right edge, RTL-base wrapped/mixed lines draw from the line's right edge even when weak digits precede the first RTL strong character, and LTR-base mixed-level shaped lines apply segment visual ordering before glyph placement. Shaped drawing covers nonzero glyph runs with DirectWrite fallback-face segmentation, explicit line spans, tab control advances, minimal whitespace wrapping when DirectWrite cluster maps are monotonic enough to project per-character advances, unbreakable wrap-word and over-height line-stack scissor clipping, LTR complex-script runs, single-level RTL lines, mixed BiDi resolved-level runs, Factory4 `IDWriteColorGlyphRunEnumerator1` layer/bitmap runs, BGRA glyph image data returned by `GetGlyphImageData`, and WIC-decoded encoded bitmap glyph image data.
- Warm glyph-atlas scroll allocation was previously documented at roughly `6.2 KB/frame`; corrected frame-scoped `--diagnose-text-cache 30` now reports the scroll sample at roughly `2.8 KB/frame` with tree/diff/translate/render attribution. Use that evidence before doing allocation work.
- Overlay removal is active in source. Do not reintroduce D3D11On12/D2D or a hidden overlay CLI alias; widen D3D12 text handling or keep explicit degradation.

Next hardening checklist:

- Resource cache / stable handles: POST-011 now has generation-safe retained-floor page reuse with separate Alpha/Bgra pending slots. Continue with resource lifetime hardening and only move to full LRU/entry-level eviction after the retained atlas command ownership story is explicit.
- Shader packaging follow-up: decide whether inline embedded DXBC is sufficient or whether to introduce a build-time shader asset pipeline before shaders grow larger.
- Resource lifetime hardening: keep tightening D3D12 resource ownership and failure phases beyond upload-map, frame-slot upload ownership, swapchain/core initialization, and centralized WIC factory dispose; glyph-atlas initialization failures must remain degradation-safe, and WIC COM uninitialize remains same-thread only.
- Warm allocation attribution: latest corrected scroll sample is mostly layout result and retained snapshot boundaries plus tree cost, not renderer submit. Translate breakdown shows the scroll sample's translate allocation is all pipeline build; pipeline breakdown is `layout=811 B/frame`, `hitTargets=273 B/frame`, `snapshot=273 B/frame`, `retainedFrame=273 B/frame`, and `record=0 B/frame`. Optimize from those measured boundaries.
- Degradation follow-up: The current MixedAtlasFull stress fits within the on-demand 48-page atlas budget with `AtlasFull=0`. Workloads beyond that budget still use the safe retained-floor page-reuse/degradation contract; SVG/COLR paint-tree color glyph rendering, deeper BiDi shaping cases, and record-failure contracts remain explicit degradation/future-work areas.
- Overlay removal path: D3D11On12/D2D source is gone. Each fallback case should move toward D3D12 handling or an explicit degradation contract.

---

## Explicit Non-Goals for Current GA/MVP

- Not changing `IDrawingBackend.Execute` signature
- Not adding public API for partial apply
- Not enabling StyleOnly layout skip
- Not extracting unified diagnostics channel
- Not requiring 144Hz validation without actual hardware
- Not reintroducing D3D11On12/D2D text overlay
- Not introducing Skia
