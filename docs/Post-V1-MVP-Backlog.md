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
| POST-017 | D3D12-only glyph atlas text renderer | Default-on prototype foundation with overlay renderer removed | Post-GA; `GlyphAtlas` is the only D3D12 PoC text composition path; narrow ASCII/NoWrap runs have local evidence and unsupported cases degrade |
| POST-011 | Resource cache / stable global handles | Entry/page handles, four-page atlas pool, explicit atlas budget diagnostics, page-owned SRV resources, record-serial-gated next-frame cold-page reuse, page usage diagnostics, and atlas touch serials done | D3D12-specific; align with glyph atlas resource ownership and retained-frame-safe eviction work |
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

The first atlas execution path records a D3D12 glyph pass for basic single-line ASCII / `NoWrap` runs, uses an `R8_UNORM` atlas, and supports leading/center/trailing alignment plus per-run scissor for accepted runs.
It now uses non-overlay degradation so unsupported renderable text runs do not force either whole-frame overlay or mixed overlay fallback.
Expanded smoke covers `ASCII / NonAscii / clipped ASCII / clipped NonAscii`, default `300 x 3`, and 2026-05-20 short degradation runs with `syncWaits=0` and nonzero `DegradedRuns`.
Current default GlyphAtlas behavior degrades unsupported and initialization/upload/record-failed renderable text runs without invoking overlay.
Full shaping, wrapping, color glyphs, fallback font identity, eviction, command-order-perfect mixed text z-order, and production enablement remain follow-up work.

| Work item | Scope | Acceptance criteria |
|-----------|-------|---------------------|
| Atlas design | Define glyph key, atlas page size, eviction, scale/DPI keying, color handling, clipping, and upload lifecycle | Design doc approved; no public backend API change |
| Glyph source | Use DirectWrite only for shaping/raster source if needed; final composition must not use D3D11On12/D2D overlay | Glyph bitmaps can be uploaded to D3D12 textures |
| D3D12 text pipeline | Add glyph quad generation, atlas SRV, pipeline state, blend state, sampler, and scissor support | Text and rectangles are submitted in one D3D12 synchronization domain |
| Diagnostics | Report atlas hit/miss/upload counts, `AtlasRuns`, `DegradedRuns`, unsupported runs, and per-run degradation reasons | Diagnostics show whether default composition still depends on degradation |
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
| D3D12-only text prototype | `Irix.Platform.Windows` | Draw basic ASCII/text runs from atlas in D3D12-only pass | ✅ Default-on prototype; overlay renderer removed |
| Shader/resource lifetime hardening | Windows D3D12 renderers | Runtime shader compile removed; failure diagnostics split; upload maps, swapchain/overlay intermediates, and core resource init/release paths are guarded | ✅ First pass done |
| Remove runtime shader compile | `D3D12GlyphAtlasTextRenderer.cs`, `D3D12Renderer2D.cs` | Replace runtime `D3DCompile` / `d3dcompiler_47.dll` dependency with embedded bytecode or build-time compiled shader assets | ✅ Embedded bytecode |
| Attribute warm glyph atlas allocation | `TextCacheAllocationDiagnosticRunner.cs`, diagnostics | Attribute the warm scroll allocation around `6.2 KB/frame` before optimizing | ✅ Attribution added |
| Non-overlay degradation path | Renderer design | Per-run atlas plus explicit degradation so NonAscii/complex/failure cases do not invoke D3D11On12/D2D in default GlyphAtlas | ✅ Default GlyphAtlas no longer records overlay fallback runs |
| Overlay removal | Renderer design / smoke evidence | Remove D3D11On12/D2D source, native generation, sync strategy, and explicit overlay mode | ✅ Active source removed |
| Full migration | Glyph-atlas widening | Reduce accepted degradation by adding D3D12 handling | Planned |

Known limitations checklist before expanding text coverage:

- Shader bytecode is embedded inline in the renderer sources. Runtime `D3DCompile` / `d3dcompiler_47.dll` dependency is removed; a build-time shader asset pipeline is optional future cleanup if shader source grows.
- Glyph-atlas diagnostics distinguish constructor-time `initFailurePhase` from runtime `recordFailurePhase`; runtime record failures disable atlas and degrade renderable text without implying device lost by themselves.
- D3D12 rectangle and glyph-atlas upload map paths unmap in `finally` after a successful map.
- D3D12 swapchain creation releases the DXGI factory and intermediate `IDXGISwapChain1` in `finally`; constructor and recovery reuse the same helper.
- D3D12 constructor and recovery share core resource initialization, with pointer guards and null-safe cleanup for partially initialized device resources.
- D3D12 overlay rollback renderer has been removed with its D3D11On12/D2D native generation entries.
- Default GlyphAtlas degrades unsupported renderable runs while accepted ASCII / `NoWrap` runs stay on atlas. Initialization and runtime record failure degrade all renderable runs for the frame.
- Default GlyphAtlas no longer has mixed overlay z-order risk because degraded runs are not drawn; replacing degradation with D3D12 rendering remains follow-up work.
- No same-frame atlas eviction. AtlasFull degradation is safe for the current prototype; it schedules a record-serial-gated next-frame cold-page reset/reuse request so accepted runs in the current frame cannot sample recycled regions.
- Glyph atlas cache entries, draw batches, and atlas pages now have stable value handles and generations internally; page-owned texture/upload/SRV resources replace renderer-level atlas resource fields. Cache hits and new glyph rasterizations touch glyph entries/pages with a monotonic atlas record serial. The renderer preallocates a four-page atlas pool, switches pages when the active page is full, splits draw batches on page changes, records page reuse requests with the triggering atlas record serial, and removes only entries from a reused page after the request becomes applicable on a later record.
- Glyph atlas diagnostics report page count, fixed page budget, page dimensions, total atlas pixel capacity, next-frame reuse count, used glyph bitmap pixels, a shelf fragmentation estimate, atlas record serial, and oldest/newest page age metrics for future page-size and LRU decisions.
- No complex shaping, fallback font identity, color glyphs, SDF/MSDF, or wrapping support in the atlas path.
- Warm glyph-atlas scroll allocation is documented at roughly `6.2 KB/frame`; `--diagnose-text-cache` now prints tree/diff/translate/render attribution. Use that evidence before doing allocation work.
- Overlay removal is active in source. Do not reintroduce D3D11On12/D2D; widen D3D12 text handling or keep explicit degradation.

Next hardening checklist:

- Resource cache / stable handles: continue POST-011 from the bounded four-page pool and explicit budget diagnostics toward generation-safe retained references before widening non-overlay text coverage.
- Shader packaging follow-up: decide whether inline embedded DXBC is sufficient or whether to introduce a build-time shader asset pipeline before shaders grow larger.
- Resource lifetime hardening: keep tightening D3D12 resource ownership and failure phases beyond upload-map and swapchain/core initialization; glyph-atlas initialization failures must remain degradation-safe.
- Warm allocation attribution: run `--diagnose-text-cache` and optimize only after tree/diff/translate/render attribution identifies the source.
- Degradation follow-up: AtlasFull and record-failure contracts are recorded as degradation. Full LRU eviction and D3D12 rendering for currently unsupported text remain future work before widening atlas text coverage.
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
