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
| POST-013 | Sync wait overhead validation | Decision complete | Current `D3D12FenceAfterOverlay` sync wait cost accepted temporarily for correctness; `D3D11Query` remains diagnostic-only; long-term fix moved to POST-017 |
| POST-014 | Windows SDK 26100 CI check | Done | CI fails early if .NET 10 or Windows SDK 26100 is absent |
| POST-015 | Platform matrix CI | Minimal matrix added | Windows 2025 lanes cover tests, headless D3D12, performance baseline, AOT publish |
| POST-016 | Performance regression CI | Done | Mock backend frame-time baseline + split frame-stage allocation baseline + warm `FrameDrawingResources` allocation baseline; latest local per-stage bytes live in `Project_Status_and_Todo.md` |

### P1 — Remaining GA/MVP Hardening

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-018 | Platform integration checks | Done for current hardware | Minimize/restore, occlusion, live DPI change, resize, scroll, click, default, and rollback smokes passed |
| POST-019 | GPU memory pressure handling | Done for V1 scope | Runtime resource recreation failures surface explicit device error reasons, including `E_OUTOFMEMORY`; no full GPU memory manager |
| POST-020 | Command allocator reset failure handling | Done | Retry once after `WaitForGpu`, then escalate to device-lost/recovery |

### P1/P2 — Post-GA Renderer Architecture

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-017 | D3D12-only glyph atlas text renderer | Default-on prototype foundation with expanded mixed fallback and mixed AtlasFull smoke | Post-GA; `GlyphAtlas` is default for the D3D12 PoC path; `Overlay` remains rollback/fallback; narrow ASCII/NoWrap runs have local evidence |
| POST-011 | Resource cache / stable global handles | Not started | D3D12-specific; can align with glyph atlas/resource cache work |
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

Current D3D11On12/D2D overlay accepted temporarily ──> POST-017 D3D12-only glyph atlas text renderer
                                                    └─ POST-011 D3D12 resource cache / stable handles

POST-005 translator ──> POST-006 public API shaping on top of typed ids/property metadata
POST-007 scroll ──────> POST-008 settings provider
```

---

## POST-017: D3D12-only Glyph Atlas Text Renderer

Private GA / overlay fallback text composition path:

```text
D3D12 rect pass -> D3D11On12 / D2D / DirectWrite overlay -> sync wait -> Present
```

Target path:

```text
D3D12 rect pass -> D3D12 glyph atlas text pass -> Present
```

Phase 1 foundation first kept the overlay path as the default runtime behavior and added only an internal composition seam. After opt-in smoke evidence, the post-GA renderer-foundation baseline now defaults to `GlyphAtlas`; `--text-composition overlay` remains the old overlay rollback, and D3D11On12/D2D overlay remains the correctness fallback when atlas composition cannot handle a frame. DirectWrite remains allowed for shaping, glyph metrics, and glyph bitmap source data. This phase does not change public API or `IDrawingBackend.Execute`.

The first atlas execution path records a D3D12 glyph pass for basic single-line ASCII / `NoWrap` runs, uses an `R8_UNORM` atlas, and supports leading/center/trailing alignment plus per-run scissor for accepted runs.
It uses mixed fallback v0 so unsupported renderable text runs go through the overlay renderer instead of forcing the whole frame to overlay.
Expanded smoke now covers `ASCII / NonAscii / clipped ASCII / clipped NonAscii` and default `300 x 3`.
Atlas initialization/upload/record failure still falls back all renderable text runs for that frame.
Full shaping, wrapping, color glyphs, fallback font identity, eviction, command-order-perfect mixed text z-order, and production enablement remain follow-up work.

| Work item | Scope | Acceptance criteria |
|-----------|-------|---------------------|
| Atlas design | Define glyph key, atlas page size, eviction, scale/DPI keying, color handling, clipping, and upload lifecycle | Design doc approved; no public backend API change |
| Glyph source | Use DirectWrite only for shaping/raster source if needed; final composition must not use D3D11On12/D2D overlay | Glyph bitmaps can be uploaded to D3D12 textures |
| D3D12 text pipeline | Add glyph quad generation, atlas SRV, pipeline state, blend state, sampler, and scissor support | Text and rectangles are submitted in one D3D12 synchronization domain |
| Diagnostics | Report atlas hit/miss/upload counts, `AtlasRuns`, `OverlayFallbackRuns`, unsupported runs, and per-run fallback reasons | Diagnostics show whether mixed fallback is actually reducing overlay work |
| Migration | Keep current D2D overlay as fallback until atlas path matches correctness and smoke baselines | No regression in text quality, clipping, scale, scroll sync, hit-test, partial apply |

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
| Accept sync wait budget | `GA-Hardening-Plan.md` | `D3D12FenceAfterOverlay` accepted temporarily; `<2ms avg` removed as GA blocker | ✅ Done |
| Remove 144Hz blocker | `GA-Hardening-Plan.md` | 144Hz validation not listed as current GA blocker | ✅ Done |
| Platform integration checks | `Irix.Poc`, manual smoke | Minimize/restore, occlusion, live DPI, resize + scroll + text sync pass on available hardware | ✅ Done |
| GPU memory pressure handling | `D3D12Renderer.cs` | Resource creation failures produce explicit device error reasons; no undefined pointer continuation | ✅ Done |
| Command allocator reset failure handling | `D3D12Renderer.cs` | Reset retry or device-lost escalation | ✅ Done |
| Keep CI green | GitHub Actions / local CI parity | Tests, D3D12 smoke, performance lane, AOT publish stay green | ✅ Done for Private GA |
| Stop pre-GA performance micro-optimization | `Project_Status_and_Todo.md` | Do not chase the remaining ~2 KB render-request reuse allocation before tag | ✅ Done |
| Private GA tag | Git | Tag committed snapshot as `v1.0-private-ga`; not a public API freeze | ✅ Done |
| Next branch | Git | Open `post-ga-renderer-foundation` after tag | ✅ Done |

### Batch B: Post-GA text renderer replacement

Phase 1 closeout: prototype evidence is captured for default overlay regression, glyph-atlas ASCII smoke, NonAscii/AtlasFull fallback, mixed clipped fallback, mixed AtlasFull fallback, resize, 100% / 150% / 200% scale, and warm allocation baseline.
The post-GA baseline has been switched to default `GlyphAtlas` plus default `Scissor`, with `--text-composition overlay`, `--disable-scissor`, and `--clip-mode diagnostic` rollback paths.
Do not keep expanding the ASCII prototype surface or flip another runtime default in the next step; move to renderer-foundation hardening first.

| Task | Entry File | Acceptance Criteria | Status |
|------|------------|---------------------|--------|
| Glyph atlas design doc | `Glyph-Atlas-Post-GA-Design.md` | Atlas architecture and migration plan accepted | ✅ Drafted |
| D3D12-only text prototype | `Irix.Platform.Windows` | Draw basic ASCII/text runs from atlas in D3D12-only pass | ✅ Default-on prototype with overlay rollback |
| Shader/resource lifetime hardening | `D3D12GlyphAtlasTextRenderer.cs`, `D3D12Renderer2D.cs`, `D3D12Renderer.cs` | Runtime shader compile removed; failure diagnostics split; successful upload maps, swapchain intermediates, and core resource init/release paths are guarded | ✅ First pass done |
| Remove runtime shader compile | `D3D12GlyphAtlasTextRenderer.cs`, `D3D12Renderer2D.cs` | Replace runtime `D3DCompile` / `d3dcompiler_47.dll` dependency with embedded bytecode or build-time compiled shader assets | ✅ Embedded bytecode |
| Attribute warm glyph atlas allocation | `TextCacheAllocationDiagnosticRunner.cs`, diagnostics | Attribute the warm scroll allocation around `6.2 KB/frame` before optimizing | ✅ Attribution added |
| Mixed fallback design | Renderer design | Per-run atlas plus per-run overlay fallback so NonAscii/complex runs do not force whole-frame overlay fallback | ✅ v0 implemented; subset parity pinned; z-order limitation documented |
| Overlay removal gate | Renderer design / smoke evidence | Do not remove D3D11On12/D2D overlay until all fallback cases have a non-overlay path or accepted degradation plus smoke coverage | Drafted; deletion deferred |
| Full migration | `D3D12TextRenderer` replacement path | D2D overlay no longer needed for final composition | Planned |

Known limitations checklist before expanding text coverage:

- Shader bytecode is embedded inline in the renderer sources. Runtime `D3DCompile` / `d3dcompiler_47.dll` dependency is removed; a build-time shader asset pipeline is optional future cleanup if shader source grows.
- Glyph-atlas diagnostics distinguish constructor-time `initFailurePhase` from runtime `recordFailurePhase`; runtime record failures disable atlas and fall back to overlay without implying device lost by themselves.
- D3D12 rectangle and glyph-atlas upload map paths unmap in `finally` after a successful map.
- D3D12 swapchain creation releases the DXGI factory and intermediate `IDXGISwapChain1` in `finally`; constructor and recovery reuse the same helper.
- D3D12 constructor and recovery share core resource initialization, with pointer guards and null-safe cleanup for partially initialized device resources.
- Mixed fallback v0 sends unsupported renderable runs to overlay while accepted ASCII / `NoWrap` runs stay on atlas. Initialization and runtime record failure still fall back all renderable runs for the frame.
- Mixed fallback v0 does not preserve exact relative z-order between overlapping atlas text and overlay fallback text; overlay fallback runs draw above atlas runs.
- No atlas eviction. Mixed AtlasFull fallback is safe for the current prototype; eviction design remains deferred.
- No complex shaping, fallback font identity, color glyphs, SDF/MSDF, or wrapping support in the atlas path.
- Warm glyph-atlas scroll allocation is documented at roughly `6.2 KB/frame`; `--diagnose-text-cache` now prints tree/diff/translate/render attribution. Use that evidence before doing allocation work.
- Overlay removal gate is drafted in `Glyph-Atlas-Post-GA-Design.md`; do not delete D3D11On12/D2D until unsupported text cases, AtlasFull/eviction, z-order, diagnostics, and rollback coverage no longer depend on it.

Next hardening checklist:

- Shader packaging follow-up: decide whether inline embedded DXBC is sufficient or whether to introduce a build-time shader asset pipeline before shaders grow larger.
- Resource lifetime hardening: keep tightening D3D12 resource ownership and failure phases beyond upload-map, swapchain intermediate, and core initialization ownership; glyph-atlas initialization failures must remain overlay fallback-safe.
- Warm allocation attribution: run `--diagnose-text-cache` and optimize only after tree/diff/translate/render attribution identifies the source.
- Mixed fallback follow-up: subset parity, AtlasFull, and record-failure contract evidence are recorded. Eviction and command-order-perfect fallback remain future work before widening atlas text coverage.
- Overlay removal gate: keep D3D11On12/D2D overlay until the drafted gate is satisfied; this is not a deletion task for the next commit.

---

## Explicit Non-Goals for Current GA/MVP

- Not changing `IDrawingBackend.Execute` signature
- Not adding public API for partial apply
- Not enabling StyleOnly layout skip
- Not extracting unified diagnostics channel
- Not requiring 144Hz validation without actual hardware
- Not replacing D3D11On12/D2D text overlay before current GA/MVP
- Not introducing Skia
