# GA Hardening Plan

> Checklist of hardening requirements before Irix can be considered GA-ready. None of these are V1 core requirements.

---

## Device Resilience

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Device-lost detection | `_deviceRemoved` flag + `DeviceErrorReason` string | Already done | — |
| Device-lost recovery | Fail-fast only; no reconstruction | Must reconstruct device, swapchain, all GPU resources | P0 |
| Device-removed during segmented frame | Exception path preserves compositor state | Confirm guard works; add explicit test | P0 |
| GPU memory pressure | No tracking | Monitor and handle `E_OUTOFMEMORY` gracefully | P1 |
| Command allocator reset failure | Not handled | Add retry or device-lost escalation | P2 |

## Display Matrix

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| 60Hz refresh | Works (default PoC) | Already done | — |
| 120Hz / 144Hz / 240Hz | Not validated | Validate animation timing and fence behavior | P1 |
| DPI scaling (100%-200%) | Physical pixels only; no logical DPI | Validate physical pixel rendering at each DPI | P1 |
| Multi-monitor | Single monitor only | Validate viewport change on monitor switch | P2 |
| Fractional DPI (125%, 150%) | Not validated | Validate no rounding artifacts in layout/clip | P2 |
| HDR / wide color gamut | Not applicable | Not required for v1.0 GA | P3 |

## Stability

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| 1000-frame soak test | Not run | Run and check for resource leaks, counter drift | P1 |
| Long-run memory stability | Not validated | `FrameDrawingResources` pool rent/return over 10k+ frames | P1 |
| Resize stress test | `--diagnose-resize` exists | Run for 60s+ continuous resize | P1 |
| Concurrent input + render | Works in PoC | Validate no deadlocks or race conditions | P1 |
| Exception recovery | Partial | Compositor catches backend exceptions; validate full recovery | P1 |

## Performance

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Frame time profiling | Not done | Profile hot path allocation and GPU submission time | P1 |
| Partial apply overhead measurement | Not done | Measure overhead of segment ownership + validation vs. full apply | P1 |
| Text cache hit rate in steady state | Diagnostic only | Validate >90% hit rate after warmup | P2 |
| DrawCommand recording allocation | `stackalloc` + `ArrayPool` | Confirm zero GC allocation in steady state | P2 |

## Platform Integration

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Window minimize/restore | Not validated | Validate D3D12 handles minimize without device-lost | P1 |
| Window occlusion | Not validated | Validate behavior when window is fully occluded | P2 |
| System DPI change (live) | Not validated | Validate resize + relayout on DPI change | P2 |
| High-contrast mode | Not validated | Validate text readability | P3 |
| Screen reader accessibility | Not applicable | Not required for v1.0 GA | P3 |

## Testing Infrastructure

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| CI test suite | 435 tests, all passing | Maintain green | — |
| D3D12-specific tests | None (PoC-only) | Add smoke tests for D3D12 backend integration | P1 |
| Platform matrix CI | Single Windows runner | Add matrix for Windows versions | P2 |
| Performance regression CI | None | Add frame time regression check | P2 |

---

## GA Readiness Assessment

**Current state:** PoC V1 core architecture-complete. No GA hardening has started.

**Minimum for GA:**
1. Device-lost recovery (P0)
2. 1000-frame soak test (P1)
3. Resize stress test (P1)
4. Frame time profiling (P1)
5. D3D12 smoke tests in CI (P1)

**Estimated scope:** 5-10 focused work items, primarily in `Irix.Platform.Windows` and `Irix.Poc`.

**Explicit non-goals for v1.0 GA:**
- HDR / wide color gamut
- Screen reader accessibility
- Multi-monitor hot-plug
- Fractional DPI awareness (physical pixels sufficient for v1.0)
