# Post-V1 / MVP Backlog

> V1 core is architecture-complete (default-off). This document tracks the next phase of work toward MVP and GA. None of these tasks are part of V1 core.

---

## Windows Version Boundary

Irix v1 Windows PoC separates the framework target SDK from the runtime minimum. Windows-targeted projects inherit `IrixWindowsTargetFramework=net10.0-windows10.0.26100.0` and `IrixWindowsSupportedOSPlatformVersion=10.0.15063.0` from `Directory.Build.props`; CI checks for .NET 10 and Windows SDK 10.0.26100.0 before restore/build. The 10.0.15063.0 runtime floor is intentional for PerMonitorV2 DPI awareness and the display scale pipeline, so docs should not imply support below that runtime boundary.

## Priority Tiers

### P0 — Completed Default-On Gates

These default-on gates are complete. Remaining work belongs to GA CI/matrix/performance hardening, not partial-apply default-on enablement.

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-001 | Default-on partial apply | Done (2026-05-13) | No longer blocking; `--no-partial-apply` remains rollback |
| POST-002 | D3D12 segmented ownership | Done for v1 default-on path | Resolver ownership, per-segment execute adapter, and D3D12 smoke validated |
| POST-003 | Device-lost recovery | Done | `D3D12Renderer.TryRecover()` and compositor `IDeviceRecovery` path test-covered |
| POST-004 | Platform matrix validation | Minimum done; GA matrix remains | Display scale, PerMonitorV2, multi-refresh manual smoke done; broader CI matrix below |

### P0/P1 — GA Hardening Remainder

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-013 | Sync wait overhead validation | 60Hz / 120Hz / 240Hz measured via `scripts/ga-baseline.ps1 -Mode Sync` at 150% scale for non-AOT and AOT; `D3D11Query` spike is correctness-clean in manual smoke and improves 60Hz / 240Hz, but regresses 120Hz and still misses 2ms at 60Hz | Correctness-first decision documented: keep default sync/full queue idle for now; retain query as diagnostic-only and track renderer-level optimization or accepted budget follow-up without disabling default sync |
| POST-014 | Windows SDK 26100 CI check | Done | CI fails early if .NET 10 or Windows SDK 26100 is absent |
| POST-015 | Platform matrix CI | Minimal matrix added | Windows 2025 lanes cover tests, headless D3D12, performance baseline, AOT publish |
| POST-016 | Performance regression CI | Mock backend frame-time baseline + warm `FrameDrawingResources` allocation baseline added; `scripts/ga-baseline.ps1` provides semi-auto sync/text-cache/smoke reports | CI catches obvious frame-time/allocation regressions; sync wait remains semi-auto because hosted runners do not provide stable refresh/scale/GPU timing |

### P1 — Framework Promotion

These advance the framework architecture but are independent of partial apply.

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-005 | Translator promotion | Internal seam complete | Translator remains in Irix.Poc; promotion requires typed feedback contract |
| POST-006 | Typed id wrappers | Design inventory only | String ActionId/target identity; no public API change yet |
| POST-007 | Scroll extraction | Design inventory only | ScrollController/State/Pump remain in Irix.Poc |
| POST-008 | Settings provider | Decision recorded (postponed) | Runtime wiring remains postponed; fallback-only internal provider |

### P2 — Future Architecture

These are post-MVP or require significant new design work.

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-009 | StyleOnly layout skip | Design only (LayoutDirtyV1-Design.md) | Requires default-on partial apply first |
| POST-010 | Retained element tree | Draft (RetainedElementTree-Design.md) | Requires stable retained tree + local patch model |
| POST-011 | Resource cache / stable global handles | Not started | D3D12-specific; requires segmented ownership |
| POST-012 | Unified diagnostics channel | Postponed | Replaces per-component diagnostics; event bus/registry |

---

## Dependency Graph

```
POST-001..004 (default-on gates complete) ──> POST-009 (layout skip)
                                           ──> POST-011 (resource cache)

POST-013 (sync wait measurements) ──┐
POST-015 (platform matrix CI)    ───┼──> GA readiness evidence
POST-016 (performance CI)       ────┘

POST-005 (translator) ──> POST-006 (typed ids)
POST-007 (scroll) ──> POST-008 (settings provider)
```

---

## Execution Plan (2026-05-13)

### Batch 1: Default-On Readiness (P0) — Done

| Task | Entry File | Acceptance Criteria | Status |
|------|-----------|---------------------|--------|
| POST-002: D3D12 segmented ownership | `D3D12-Segmented-Ownership-Prep.md` | Resolver misrouting fixed; text cache validated; device-removed guard verified | ✅ Done |
| POST-003: Device-lost guard | `DrawingBackendCompositor.cs` | try/finally on both handoff and non-handoff paths | ✅ Done |
| POST-004: Platform matrix (minimum) | Manual test | Multi-refresh + HiDPI/display scale spot check with default partial apply | ✅ Done |
| POST-001: Default-on flip | `Program.cs` | All 5 go/no-go gates satisfied; rollback via `--no-partial-apply` | ✅ Done |

**Batch 1 exit criteria:** Met. Default-on partial apply is no longer a blocker, but GA readiness still requires CI/matrix/perf evidence.

### Batch 2: Translator & Typed IDs (P1) — Not Started

| Task | Entry File | Acceptance Criteria | Blocked By |
|------|-----------|---------------------|------------|
| POST-005: Translator promotion | `Irix.Poc/Translator*` | Translator moved to `Irix.Rendering` or `Irix.Core`; typed feedback contract | None |
| POST-006: Typed id wrappers | Design inventory | `ActionId` wrapper replaces string; `TargetIdentity` typed | POST-005 |

**Batch 2 exit criteria:** Translator promoted with typed feedback. Public API uses typed id wrappers.

### Batch 3: Scroll & Settings (P1) — Not Started

| Task | Entry File | Acceptance Criteria | Blocked By |
|------|-----------|---------------------|------------|
| POST-007: Scroll extraction | `Irix.Poc/Scroll*` | ScrollController/State/Pump moved to framework layer | None |
| POST-008: Settings provider | Decision postponed | Runtime wiring if/when needed | POST-007 |

**Batch 3 exit criteria:** Scroll extraction complete. Settings provider decision revisited.

### Batch 4: Post-Default-On Architecture (P2) — Not Started

| Task | Entry File | Acceptance Criteria | Blocked By |
|------|-----------|---------------------|------------|
| POST-009: StyleOnly layout skip | `LayoutDirtyV1-Design.md` | Layout skipped when only style changed; validated by test | POST-001 |
| POST-011: Resource cache / stable handles | Design TBD | D3D12 resource cache with stable global handles | POST-001, POST-002 |
| POST-010: Retained element tree | `RetainedElementTree-Design.md` | Stable retained tree with local patch model | Design complete |
| POST-012: Unified diagnostics channel | Postponed | Event bus/registry replaces per-component diagnostics | Not blocking |

**Batch 4 exit criteria:** Layout skip validated. Resource cache designed. Retained element tree prototyped.

### Batch Sequencing

```
Batch 1 (P0) ──> POST-001 (default-on) ──> Batch 4 (P2 architecture)
                                             POST-009 (layout skip)
                                             POST-011 (resource cache)

Batch 2 (P1 translator) ──> POST-006 (typed ids)  [independent of Batch 1]
Batch 3 (P1 scroll) ──> POST-008 (settings)        [independent of Batch 1]
```

---

## Explicit Non-Goals for This Phase

- Not changing `IDrawingBackend.Execute` signature
- Not adding public API for partial apply
- Not changing public rendering/backend API for diagnostics; CLI diagnostics may include strategy fields for GA baselines
- Not enabling layout skip
- Not extracting unified diagnostics channel
