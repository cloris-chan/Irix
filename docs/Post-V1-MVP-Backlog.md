# Post-V1 / MVP Backlog

> V1 core is architecture-complete. This document tracks remaining MVP/GA hardening and post-GA renderer work. None of these tasks reopen V1 core.

---

## Windows Version Boundary

Irix v1 Windows PoC separates target SDK from runtime minimum. Windows-targeted projects inherit `IrixWindowsTargetFramework=net10.0-windows10.0.26100.0` and `IrixWindowsSupportedOSPlatformVersion=10.0.15063.0` from `Directory.Build.props`; CI checks for .NET 10 and Windows SDK 10.0.26100.0 before restore/build. The 10.0.15063.0 runtime floor is intentional for PerMonitorV2 DPI awareness and display scale support.

---

## Priority Tiers

### P0 — Completed Default-On / GA Gates

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-001 | Default-on partial apply | Done (2026-05-13) | No longer blocking; `--no-partial-apply` remains rollback |
| POST-002 | D3D12 segmented ownership | Done for v1 default-on path | Resolver ownership, per-segment execute adapter, and D3D12 smoke validated |
| POST-003 | Device-lost recovery | Done | `D3D12Renderer.TryRecover()` and compositor `IDeviceRecovery` path test-covered |
| POST-004 | Platform matrix minimum | Done for available hardware | 60Hz / 120Hz / 240Hz, 100% / 150% / 200% evidence accepted; 144Hz removed from scope because no hardware is available |
| POST-013 | Sync wait overhead validation | Decision complete | Current `D3D12FenceAfterOverlay` sync wait cost accepted temporarily for correctness; `D3D11Query` remains diagnostic-only; long-term fix moved to POST-017 |
| POST-014 | Windows SDK 26100 CI check | Done | CI fails early if .NET 10 or Windows SDK 26100 is absent |
| POST-015 | Platform matrix CI | Minimal matrix added | Windows 2025 lanes cover tests, headless D3D12, performance baseline, AOT publish |
| POST-016 | Performance regression CI | Done | Mock backend frame-time baseline + warm `FrameDrawingResources` allocation baseline |

### P1 — Remaining GA/MVP Hardening

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-018 | Platform integration checks | Remaining | Validate minimize/restore, occlusion, live DPI change plus resize/scroll/text sync combinations on available hardware |
| POST-019 | GPU memory pressure handling | Not started | Handle `E_OUTOFMEMORY` and memory-pressure failures gracefully where feasible |
| POST-020 | Command allocator reset failure handling | Not started | Retry or escalate to device-lost path |

### P1/P2 — Post-GA Renderer Architecture

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-017 | D3D12-only glyph atlas text renderer | Planned | Replace current D3D12 + D3D11On12 + D2D/DirectWrite overlay composition with D3D12-only atlas text pass; removes the sync wait class of problem |
| POST-011 | Resource cache / stable global handles | Not started | D3D12-specific; can align with glyph atlas/resource cache work |
| POST-009 | StyleOnly layout skip | Design only | Requires default-on partial apply first; not GA-blocking |
| POST-010 | Retained element tree | Draft | Requires stable retained tree + local patch model |

### P2 — Framework Promotion / Deferred Architecture

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-005 | Translator promotion | Internal seam complete | Translator remains in Irix.Poc; promotion requires typed feedback contract |
| POST-006 | Typed id wrappers | Design inventory only | String ActionId/target identity; no public API change yet |
| POST-007 | Scroll extraction | Design inventory only | ScrollController/State/Pump remain in Irix.Poc |
| POST-008 | Settings provider | Decision recorded, postponed | Runtime wiring remains postponed; fallback-only internal provider |
| POST-012 | Unified diagnostics channel | Postponed | Would replace per-component diagnostics; not MVP/GA-blocking |

---

## Dependency Graph

```text
POST-001..004, POST-013..016 complete ──> GA/MVP hardening remainder
                                             └─ POST-018 platform integration

Current D3D11On12/D2D overlay accepted temporarily ──> POST-017 D3D12-only glyph atlas text renderer
                                                    └─ POST-011 D3D12 resource cache / stable handles

POST-005 translator ──> POST-006 typed ids
POST-007 scroll ──────> POST-008 settings provider
```

---

## POST-017: D3D12-only Glyph Atlas Text Renderer

Current text composition path:

```text
D3D12 rect pass -> D3D11On12 / D2D / DirectWrite overlay -> sync wait -> Present
```

Target path:

```text
D3D12 rect pass -> D3D12 glyph atlas text pass -> Present
```

| Work item | Scope | Acceptance criteria |
|-----------|-------|---------------------|
| Atlas design | Define glyph key, atlas page size, eviction, scale/DPI keying, color handling, clipping, and upload lifecycle | Design doc approved; no public backend API change |
| Glyph source | Use DirectWrite only for shaping/raster source if needed; final composition must not use D3D11On12/D2D overlay | Glyph bitmaps can be uploaded to D3D12 textures |
| D3D12 text pipeline | Add glyph quad generation, atlas SRV, pipeline state, blend state, sampler, and scissor support | Text and rectangles are submitted in one D3D12 synchronization domain |
| Diagnostics | Report atlas hit/miss/upload/eviction counts and upload bytes/frame | Diagnostics replace or complement current text cache baseline |
| Migration | Keep current D2D overlay as fallback until atlas path matches correctness and smoke baselines | No regression in text quality, clipping, scale, scroll sync, hit-test, partial apply |

Non-goals:

- Do not implement before current GA/MVP unless explicitly re-scoped.
- Do not introduce Skia.
- Do not change `IDrawingBackend.Execute`.
- Do not expose atlas internals as public API.

---

## Execution Plan

### Batch A: GA/MVP closeout

| Task | Entry File | Acceptance Criteria | Status |
|------|------------|---------------------|--------|
| Accept sync wait budget | `GA-Hardening-Plan.md` | `D3D12FenceAfterOverlay` accepted temporarily; `<2ms avg` removed as GA blocker | ✅ Done |
| Remove 144Hz blocker | `GA-Hardening-Plan.md` | 144Hz validation not listed as current GA blocker | ✅ Done |
| Platform integration checks | `Irix.Poc`, manual smoke | Minimize/restore, occlusion, live DPI, resize + scroll + text sync pass on available hardware | Remaining |
| Keep CI green | GitHub Actions | Tests, D3D12 smoke, performance lane, AOT publish stay green | Ongoing |

### Batch B: Post-GA text renderer replacement

| Task | Entry File | Acceptance Criteria | Status |
|------|------------|---------------------|--------|
| Glyph atlas design doc | New doc TBD | Atlas architecture and migration plan accepted | Planned |
| D3D12-only text prototype | `Irix.Platform.Windows` | Draw basic ASCII/text runs from atlas in D3D12-only pass | Planned |
| Full migration | `D3D12TextRenderer` replacement path | D2D overlay no longer needed for final composition | Planned |

---

## Explicit Non-Goals for Current GA/MVP

- Not changing `IDrawingBackend.Execute` signature
- Not adding public API for partial apply
- Not enabling StyleOnly layout skip
- Not extracting unified diagnostics channel
- Not requiring 144Hz validation without actual hardware
- Not replacing D3D11On12/D2D text overlay before current GA/MVP
- Not introducing Skia
