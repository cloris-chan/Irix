# Default-On Partial Apply Prep

> Pre-design inventory for enabling partial apply by default. Currently all partial apply behavior is internal/default-off. This document identifies what must be validated before flipping the default.

---

## Current State

- `CanHookUpPartialApply = true` (8/8 gates satisfied)
- All partial apply paths are internal/default-off
- `StyleOnlyFastPathOptions.Disabled` is the default
- Default-off behavior is verified equivalent to production by 6 full-parity tests

## Go/No-Go Gates

Each gate must be satisfied before enabling default-on. Current status and blocking work listed below.

### Gate 1: D3D12 Per-Segment Execute Validation

**Status:** Validated and fixed (2026-05-13).

**What to validate:**
- `D3D12DrawingBackend.Execute` called multiple times between `BeginFrame`/`EndFrame` with different `IFrameResourceResolver` instances
- Text rendering with mixed resolvers (old frame text + new frame text) produces correct output
- Background color heuristic (first FillRect) works correctly across segments
- Vertex buffer capacity (1024 quads) is not exceeded when segments are batched
- D2D text interop handles multiple resolvers without stale cache hits

**Findings:**
1. **Resolver misrouting bug (fixed):** `D3D12DrawingBackend._resources` was overwritten each `Execute` call (line 170), so `EndFrame` used the LAST resolver for ALL text runs across all segments. Fixed by eagerly resolving styles in `Execute` and storing per-text-run resolver references in `TextData`. Style resolution uses `TextStyle` value equality (not `ResourceHandle`), so cache keys are safe across resolvers.
2. **Text cache safety (validated):** Text format cache uses `TextStyle` (8-field value equality) as key. Text layout cache uses `TextLayoutCacheKey(TextHash, TextLength, TextStyle, Width, Height)`. Neither cache includes `ResourceHandle` or resolver identity. Different resolvers producing the same `TextStyle` correctly share cache entries.
3. **Background color heuristic (low risk):** `_bgR/_bgG/_bgB/_bgA` set from first `FillRect` across all segments. Consistent across old/replacement segments in practice. No fix needed.
4. **Vertex buffer capacity (N/A):** Batching accumulates all segments into one submission; total quad count same as full frame.

**Remaining validation:** Run D3D12 with enabled partial apply on the Counter PoC and verify visual correctness (manual smoke test).

### Gate 2: Resource Lifecycle Under Segment Ownership

**Status:** Validated in tests with mock backends. Not validated with D3D12.

**What to validate:**
- `FrameDrawingResources` retained by segment ownership are not returned to pool while D3D12 still references them
- `RetainedResourceSnapshot` release timing does not race with D3D12's `EndFrame` text rendering
- Multiple `FrameDrawingResources` instances (old + replacement) coexist without pool corruption

**Current assessment:** The `RetainedRenderFrameSegmentOwnership` retains snapshots until dispose or replacement. The `SegmentedRetainedFrameRuntimeOwner` holds its own segment table. The risk is if a snapshot's `FrameDrawingResources` is returned to the static pool and then rented for a new frame while the D3D12 backend still holds a reference from a previous segment's resolver. The current retain/release model should prevent this, but D3D12 validation is needed.

**Blocking work:** Add D3D12-specific lifecycle test or manual validation.

### Gate 3: Device-Lost Recovery

**Status:** Guard implemented (2026-05-13). Full recovery deferred to GA.

**What to validate:**
- Partial apply path does not introduce new device-lost scenarios
- Segment-local dirty ranges do not cause GPU resource corruption on device-removed
- Fallback from partial to full apply works correctly after device recovery

**Findings (2026-05-13):**
1. **Segmented path (handoff):** `SegmentedBackendExecutionAdapter.Execute` pairs `BeginFrame`/`EndFrame` via try/finally. `ExecuteSelectedHandoffFrame` catches exceptions, sets `BackendThrewBeforeCommit` reason, re-throws. Compositor counters and hit targets only updated after successful return. ✅
2. **Non-handoff path (fixed):** `DrawingBackendCompositor.RenderAsync` direct backend call path previously had NO try/finally. **Fixed (2026-05-13):** Added try/finally to pair `EndFrame` with `BeginFrame`. ✅
3. **Full device-lost recovery (deferred):** Device reconstruction (swapchain, GPU resources) is a GA requirement, not a default-on requirement. Current fail-fast behavior is acceptable for PoC default-on.

**Decision:** Device-lost guard is sufficient for default-on. Full device-lost recovery deferred to GA (POST-003).

### Gate 4: Platform Matrix Validation

**Status:** Not started.

**What to validate:**
- Partial apply on 60Hz, 120Hz, 144Hz, 240Hz displays
- DPI scaling (100%, 125%, 150%, 200%)
- Multi-monitor with different DPI/scale
- Long-run stability (1000+ frames with partial apply)

**Current assessment:** The partial apply path is frame-count and resource-identity based, not timing-based, so refresh rate should not matter. DPI affects layout geometry but not the partial apply decision. Multi-monitor could affect viewport changes which trigger full layout rebuild. Long-run stability needs validation for resource leak or counter drift.

**Blocking work:** Define test matrix and run on available hardware.

### Gate 5: Rollback Strategy

**Status:** Implicit (fallback to full apply exists).

**What to validate:**
- If default-on causes regressions, flipping back to default-off is a single option change
- No persistent state is corrupted by enabling then disabling partial apply
- Compositor counters correctly reflect the mode switch

**Current assessment:** The architecture is designed for clean rollback — `StyleOnlyFastPathOptions.Disabled` disables all partial apply paths. The `RetainedRenderFrameSegmentOwnership` is created fresh per feed instance. No persistent state crosses the enable/disable boundary.

**Blocking work:** Verify rollback by running tests with enabled → disabled transitions.

---

## Go/No-Go Checklist (2026-05-13)

| Gate | Status | Blocking? | Notes |
|------|--------|-----------|-------|
| Gate 1: D3D12 Per-Segment Execute | ✅ Validated + Fixed | Was blocking | Resolver misrouting bug fixed; text cache safety validated |
| Gate 2: Resource Lifecycle | ⚠️ Architecture-validated | Not blocking | Mock-backend tests pass; D3D12-specific lifecycle not yet smoke-tested |
| Gate 3: Device-Lost Guard | ✅ Guard implemented | Was blocking | try/finally on both paths; full recovery deferred to GA |
| Gate 4: Platform Matrix | ❌ Not started | Blocking | At minimum: 60Hz spot check required |
| Gate 5: Rollback Strategy | ✅ Already satisfied | Not blocking | Architecture supports clean rollback |

**Conclusion: NO-GO** — Gate 4 (platform matrix) not yet satisfied. Minimum required: run D3D12 with enabled partial apply on available hardware at 60Hz and verify visual correctness.

**Remaining work before GO:**
1. Manual D3D12 smoke test with enabled partial apply on Counter PoC (Gate 1 completion)
2. 60Hz platform spot check (Gate 4 minimum)

**Not required for default-on:** Full device-lost recovery, multi-monitor validation, 1000-frame soak test, performance profiling, high-refresh validation. These are GA requirements.
