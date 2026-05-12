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

**Status:** Not validated on real D3D12 backend.

**What to validate:**
- `D3D12DrawingBackend.Execute` called multiple times between `BeginFrame`/`EndFrame` with different `IFrameResourceResolver` instances
- Text rendering with mixed resolvers (old frame text + new frame text) produces correct output
- Background color heuristic (first FillRect) works correctly across segments
- Vertex buffer capacity (1024 quads) is not exceeded when segments are batched
- D2D text interop handles multiple resolvers without stale cache hits

**Current assessment:** The D3D12 backend's accumulate-in-Execute/submit-in-EndFrame architecture naturally supports multiple Execute calls. The main risk is the text renderer's format/layout cache potentially serving stale results when the resolver changes between segments. The cache keys include the text content and style handle, but not the resolver identity — this should be safe because different resolvers with the same text/style would produce the same layout, but needs validation.

**Blocking work:** Run D3D12 with enabled partial apply on the Counter PoC and verify visual correctness.

### Gate 2: Resource Lifecycle Under Segment Ownership

**Status:** Validated in tests with mock backends. Not validated with D3D12.

**What to validate:**
- `FrameDrawingResources` retained by segment ownership are not returned to pool while D3D12 still references them
- `RetainedResourceSnapshot` release timing does not race with D3D12's `EndFrame` text rendering
- Multiple `FrameDrawingResources` instances (old + replacement) coexist without pool corruption

**Current assessment:** The `RetainedRenderFrameSegmentOwnership` retains snapshots until dispose or replacement. The `SegmentedRetainedFrameRuntimeOwner` holds its own segment table. The risk is if a snapshot's `FrameDrawingResources` is returned to the static pool and then rented for a new frame while the D3D12 backend still holds a reference from a previous segment's resolver. The current retain/release model should prevent this, but D3D12 validation is needed.

**Blocking work:** Add D3D12-specific lifecycle test or manual validation.

### Gate 3: Device-Lost Recovery

**Status:** Not implemented. Current behavior is fail-fast.

**What to validate:**
- Partial apply path does not introduce new device-lost scenarios
- Segment-local dirty ranges do not cause GPU resource corruption on device-removed
- Fallback from partial to full apply works correctly after device recovery

**Current assessment:** Device-lost recovery is a prerequisite for GA but not strictly for default-on in a PoC. However, enabling partial apply by default without device-lost recovery means a device-lost event during a segmented frame could leave the compositor in an inconsistent state (selected source committed but production frame not updated).

**Blocking work:** Decide whether device-lost recovery is required for default-on or can be deferred. If deferred, add explicit guard to fall back to full apply on device-removed.

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

## Recommendation

Default-on partial apply can be pursued after:
1. D3D12 per-segment execute validation (Gate 1) — manual smoke test
2. Resource lifecycle validation (Gate 2) — manual smoke test or D3D12-specific test
3. Device-lost guard (Gate 3) — add explicit fallback on device-removed, full recovery can be deferred
4. Platform matrix spot check (Gate 4) — at least 60Hz + one high-refresh display

Gate 5 (rollback) is already satisfied by architecture.

**Not required for default-on:** Full device-lost recovery, multi-monitor validation, 1000-frame soak test, performance profiling. These are GA requirements.
