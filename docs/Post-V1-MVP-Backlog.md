# Post-V1 / MVP Backlog

> V1 core is architecture-complete (default-off). This document tracks the next phase of work toward MVP and GA. None of these tasks are part of V1 core.

---

## Windows Version Boundary

Irix v1 Windows PoC separates the framework target SDK from the runtime minimum. Windows-targeted projects inherit `IrixWindowsTargetFramework=net10.0-windows10.0.26100.0` and `IrixWindowsSupportedOSPlatformVersion=10.0.15063.0` from `Directory.Build.props`; CI checks for .NET 10 and Windows SDK 10.0.26100.0 before restore/build. The 10.0.15063.0 runtime floor is intentional for PerMonitorV2 DPI awareness and the display scale pipeline, so docs should not imply support below that runtime boundary.

## Priority Tiers

### P0 — Gate for Default-On

These must complete before partial apply can be enabled by default.

| ID | Task | Current status | Blocking condition |
|----|------|---------------|-------------------|
| POST-001 | Default-on partial apply | Prep only | Requires POST-002, POST-003, POST-004 |
| POST-002 | D3D12 segmented ownership | Prep only | Requires D3D12 backend to handle per-segment Execute with real GPU resources |
| POST-003 | Device-lost recovery | Not started | D3D12Renderer currently fail-fast; must reconstruct device/swapchain/resources |
| POST-004 | Platform matrix validation | Not started | 240Hz/120Hz/60Hz, DPI scaling, multi-monitor, long-run stability |

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
POST-002 (D3D12 segmented) ──┐
POST-003 (device-lost)    ───┼──> POST-001 (default-on) ──> POST-009 (layout skip)
POST-004 (platform matrix) ──┘
                              ──> POST-011 (resource cache)

POST-005 (translator) ──> POST-006 (typed ids)
POST-007 (scroll) ──> POST-008 (settings provider)
```

---

## Execution Plan (2026-05-13)

### Batch 1: Default-On Readiness (P0) — In Progress

| Task | Entry File | Acceptance Criteria | Status |
|------|-----------|---------------------|--------|
| POST-002: D3D12 segmented ownership | `D3D12-Segmented-Ownership-Prep.md` | Resolver misrouting fixed; text cache validated; device-removed guard verified | ✅ Code fixes done, manual smoke test pending |
| POST-003: Device-lost guard | `DrawingBackendCompositor.cs` | try/finally on both handoff and non-handoff paths | ✅ Done |
| POST-004: Platform matrix (minimum) | Manual test | 60Hz spot check with enabled partial apply on Counter PoC | ❌ Not started |
| POST-001: Default-on flip | `StyleOnlyFastPathOptions.cs` | All 5 go/no-go gates satisfied; single option change | ❌ Blocked by POST-004 minimum |

**Batch 1 exit criteria:** Manual D3D12 smoke test passes at 60Hz with enabled partial apply. Go/no-go checklist reaches GO.

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
- Not changing CLI diagnostics output
- Not enabling layout skip
- Not extracting unified diagnostics channel
