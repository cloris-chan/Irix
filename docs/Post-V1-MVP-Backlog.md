# Post-V1 / MVP Backlog

> V1 core is architecture-complete (default-off). This document tracks the next phase of work toward MVP and GA. None of these tasks are part of V1 core.

---

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

## Explicit Non-Goals for This Phase

- Not changing `IDrawingBackend.Execute` signature
- Not adding public API for partial apply
- Not changing CLI diagnostics output
- Not enabling layout skip
- Not extracting unified diagnostics channel
