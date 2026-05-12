# GA Hardening Plan

> Checklist of hardening requirements before Irix can be considered GA-ready. None of these are V1 core requirements.

---

## Device Resilience

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Device-lost detection | `_deviceRemoved` flag + `DeviceErrorReason` string + `DeviceLost` event | Already done | — |
| Device-lost recovery | `D3D12Renderer.TryRecover()` reconstructs all GPU resources; compositor catches backend exceptions, checks `IDeviceRecovery`, attempts recovery; 2 tests (recovery succeeds/fails) | Already done | — |
| Device-removed during segmented frame | Compositor catches exceptions in both standard and handoff paths; recovery attempted via `IDeviceRecovery`; test-verified | Already done | — |
| GPU memory pressure | No tracking | Monitor and handle `E_OUTOFMEMORY` gracefully | P1 |
| Command allocator reset failure | Not handled | Add retry or device-lost escalation | P2 |

## Display Matrix

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| 60Hz refresh | Works (default PoC) | Already done | — |
| 120Hz / 144Hz / 240Hz | Not validated | Validate animation timing and fence behavior | P1 |
| DPI scaling (100%-200%) | Platform-neutral `DisplayScale` model; compositor owns scale boundary; layout in logical units, backend in physical pixels; text/font scaled consistently; `WM_DPICHANGED` runtime handling; app.manifest PerMonitorV2; hand-tested 100%/150%/200% (2026-05-13) | Validate no bitmap stretch at each DPI; logical→physical conversion correct | P1 |
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
| CI test suite | 478 tests, all passing | Maintain green | — |
| D3D12-specific tests | None (PoC-only) | Add smoke tests for D3D12 backend integration | P1 |
| Platform matrix CI | Single Windows runner | Add matrix for Windows versions | P2 |
| Performance regression CI | None | Add frame time regression check | P2 |

---

## GA Readiness Assessment

**Current state:** PoC V1 core architecture-complete. Display scale pipeline complete and hand-tested (100%/150%/200%). Device-lost recovery implemented (2026-05-13). GA hardening first batch in progress.

**Minimum for GA:**
1. ~~Device-lost recovery (P0)~~ — Done
2. 1000-frame soak test (P1)
3. Resize stress test (P1)
4. Frame time profiling (P1)
5. D3D12 smoke tests in CI (P1)

**Estimated scope:** 5-10 focused work items, primarily in `Irix.Platform.Windows` and `Irix.Poc`.

---

## First Batch Implementation Plan (2026-05-13)

### Item 1: Device-Lost Recovery (P0) — DONE

**Entry point:** `src/Irix.Platform.Windows/D3D12Renderer.cs`, `src/Irix.Poc/D3D12DrawingBackend.cs`, `src/Irix.Rendering/DrawingBackendCompositor.cs`

**Implementation (2026-05-13):**
1. `D3D12Renderer.DeviceLost` event fires on first device-removed detection (in `HandleDeviceError`, `SucceededOrMarkDeviceRemoved`, text renderer failure)
2. `D3D12Renderer.TryRecover()` releases all GPU resources and reinitializes from scratch (device, queue, swapchain, RTV heap, render targets, allocators, command list, fence, renderer2D, textRenderer)
3. `IDeviceRecovery` internal interface in `Irix.Rendering` (`IsDeviceRemoved`, `TryRecover()`)
4. `D3D12DrawingBackend` implements `IDeviceRecovery`, delegates to `D3D12Renderer`
5. `DrawingBackendCompositor` catches backend exceptions, checks `IDeviceRecovery`, attempts recovery; skips frame on success, re-throws on failure
6. Handoff path also covered: `ExecuteSelectedHandoffFrame` catches and attempts recovery
7. 2 tests: recovery succeeds (frame skipped, next frame renders), recovery fails (exception propagates)

**Prohibited scope:** No multi-GPU support. No adapter enumeration. No DXGI debug layer integration.

### Item 2: D3D12 Smoke Tests in CI (P1)

**Entry point:** `tests/Irix.Core.Tests/` (new test class)

**Current state:** 435 tests pass, all mock-backend. No D3D12-specific tests.

**Implementation steps:**
1. Create `D3D12SmokeTests` test class with `[Trait("Category", "D3D12")]`
2. Add headless D3D12 device creation test (no window, no swapchain)
3. Add command list recording test (record + close, no execution)
4. Add resource creation test (vertex buffer, texture, descriptor heap)
5. Add device-removed detection test (force via debug layer if available)

**Acceptance criteria:**
- CI runs D3D12 smoke tests on Windows runner
- Tests pass without a display (headless)
- Tests are fast (<5s total)

**Prohibited scope:** No swapchain present tests (require window). No visual regression. No GPU timing.

### Item 3: 1000-Frame Soak Test (P1)

**Entry point:** New test or diagnostic mode

**Current state:** Not run. `FrameDrawingResources` pool rent/return not validated over long runs.

**Implementation steps:**
1. Add `--soak-frames N` CLI flag to Counter PoC
2. Run N frames with deterministic content, check for:
   - Resource leak (pool count stable)
   - Counter drift (render count == frame count)
   - Memory growth (GC.GetTotalMemory stable ±10%)
3. Add automated test: 1000 frames with mock backend, assert pool stability

**Acceptance criteria:**
- 1000 frames complete without crash
- `FrameDrawingResources` pool rent/return count balanced
- No counter drift (render count == frame count)
- Memory stable (no monotonic growth)

**Prohibited scope:** No GPU memory tracking. No DXGI debug leak reporting. No performance profiling.

### Item 4: Resize Stress Test (P1)

**Entry point:** Existing `--diagnose-resize` flag

**Current state:** `--diagnose-resize` exists but not run for extended duration.

**Implementation steps:**
1. Extend `--diagnose-resize` to accept duration parameter (e.g., `--diagnose-resize 60s`)
2. Run 60s continuous resize, check for:
   - Swapchain recreation success rate
   - Resource leak after resize
   - Backend exception count
3. Add assertion: zero backend exceptions during resize stress

**Acceptance criteria:**
- 60s continuous resize completes without crash
- Zero backend exceptions
- Swapchain recreation succeeds every time

**Prohibited scope:** No DPI change simulation. No multi-monitor testing.

### Item 5: Frame Time Profiling (P1)

**Entry point:** New diagnostic mode or `Stopwatch` instrumentation

**Current state:** No frame time measurement.

**Implementation steps:**
1. Add `Stopwatch` to `DrawingBackendCompositor.RenderAsync` measuring total frame time
2. Add `Stopwatch` to `D3D12DrawingBackend.EndFrame` measuring GPU submission time
3. Expose via diagnostic properties: `LastFrameTimeMs`, `AverageFrameTimeMs`
4. Add CLI flag `--profile-frames N` that prints frame time stats after N frames

**Acceptance criteria:**
- Frame time measurable at microsecond precision
- GPU submission time separable from total frame time
- CLI output shows min/max/avg/p95 frame times

**Prohibited scope:** No GPU timing queries (D3D12 timestamp queries). No per-draw-call profiling. No visual timeline.

---

**Explicit non-goals for v1.0 GA:**
- HDR / wide color gamut
- Screen reader accessibility
- Multi-monitor hot-plug
- Fractional DPI awareness (physical pixels sufficient for v1.0)
