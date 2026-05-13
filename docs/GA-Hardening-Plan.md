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

Windows version boundary: Irix v1 Windows PoC targets Windows SDK 10.0.26100.0 through `net10.0-windows10.0.26100.0`, while the runtime minimum remains Windows 10 1703 / 10.0.15063.0. This separation is required so the PerMonitorV2 manifest and display scale pipeline have a clear supported OS floor without tying runtime support to the target SDK.

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| 60Hz refresh | Works (default PoC) | Already done | — |
| 120Hz / 144Hz / 240Hz | 60Hz / 120Hz / 240Hz numeric sync wait captured at 150% scale for non-AOT and AOT; 144Hz unavailable on the current display | Validate animation timing and fence behavior | P1 |
| DPI scaling (100%-200%) | Platform-neutral `DisplayScale` model; compositor owns scale boundary; layout in logical units, backend in physical pixels; text/font scaled consistently; `WM_DPICHANGED` runtime handling; app.manifest PerMonitorV2; runtime minimum 10.0.15063.0; hand-tested 100%/150%/200% (2026-05-13) | Validate no bitmap stretch at each DPI; logical→physical conversion correct | P1 |
| Multi-monitor | Single monitor only | Validate viewport change on monitor switch | P2 |
| Fractional DPI (125%, 150%) | Not validated | Validate no rounding artifacts in layout/clip | P2 |
| HDR / wide color gamut | Not applicable | Not required for v1.0 GA | P3 |

## Stability

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| 1000-frame soak test | 3 tests: render count, empty/nonempty interleave, memory stability | Already done | — |
| Long-run memory stability | 1100-frame memory growth test (100 warmup + 1000 soak, <50% growth threshold) | Already done | — |
| Resize stress test | 4 tests: scale consistency (1x/1.5x/2x), extreme sizes, runtime scale change, 1000 rapid resizes | Already done | — |
| Concurrent input + render | 5 tests: sequential scroll render, ScrollFramePump dispatch, rapid coalescing, thread-safe AddPendingPixels, multi-cycle render | Already done | — |
| Exception recovery | Compositor catches backend exceptions; `IDeviceRecovery` interface; 2 tests (recovery succeeds/fails) | Already done | — |
| D2D text overlay sync under scroll | GPU fence wait after D2D text overlay, before Present; default-on with `--no-sync-text-overlay` escape hatch; 4 scroll text-sync regression tests | Already done | — |

## Performance

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Frame time profiling | Compositor-level: `LastFrameTimeUs`, `AverageFrameTimeUs`, `MaxFrameTimeUs` via `Stopwatch` | Already done | — |
| Partial apply overhead measurement | Frame time profiling can compare partial vs. full path; measure via `PartialApplyCount` / `FullApplyCount` | Already done | — |
| Performance regression CI | Mock backend frame timing baseline and warm `FrameDrawingResources` allocation baseline in `Category=Performance`; CI lane runs them separately; ordinary CI also includes soak and resize stress tests | Already done | — |
| Text cache hit rate in steady state | `scripts/ga-baseline.ps1 -Mode TextCache` validates static, scroll, and scale-change phases; 100% / 150% / 200% local runs keep layout cache hit-rate above 99% with no evictions | Already done for current machine; broaden only when new hardware is available | P2 |
| DrawCommand recording allocation | `stackalloc` + `ArrayPool`; warm `FrameDrawingResources` allocation baseline in CI; current text-cache diagnostics show text-resource allocation stable after pool warmup | Keep performance lane green and rerun semi-auto baseline before GA cut | P2 |
| Sync wait overhead | `scripts/ga-baseline.ps1 -Mode Sync` measured 60Hz / 120Hz / 240Hz at 150% scale for non-AOT and AOT. The current full-queue-idle control is correctness-preserving but can exceed the provisional 2ms target. A D3D11 event-query spike is correctness-clean in manual smoke, improves 60Hz / 240Hz, but regresses 120Hz. | Correctness accepted, performance budget not accepted; keep default sync enabled and keep D3D11 query as diagnostic-only pending a more robust renderer-level primitive | P1 |

### Sync Wait Evidence (2026-05-13)

Canonical local runner:

```powershell
.\scripts\ga-baseline.ps1 -Mode Sync -Frames 300 -Samples 3 -RefreshLabel 120Hz -ScalePercent 150
.\scripts\ga-baseline.ps1 -Mode Sync -Frames 300 -Samples 3 -RefreshLabel 120Hz -ScalePercent 150 -SyncStrategy D3D12FenceAfterOverlay
.\scripts\ga-baseline.ps1 -Mode Sync -Frames 300 -Samples 3 -RefreshLabel 120Hz -ScalePercent 150 -Aot -SeparateSamples -SyncStrategy D3D12FenceAfterOverlay
.\scripts\ga-baseline.ps1 -Mode Sync -Frames 300 -Samples 3 -RefreshLabel 120Hz -ScalePercent 150 -SyncStrategy D3D11Query
```

The table below preserves the full-queue-idle control baseline captured before the strategy spike. Strategy-labeled reruns used for the D3D11 query decision are listed in the next section.

| Refresh | Scale | Runtime | Frames / sample | Samples | Avg sync wait | P95 sync wait | Max sync wait | Waits >2ms | Correctness evidence | Evidence |
|---------|-------|---------|-----------------|---------|---------------|---------------|---------------|------------|----------------------|----------|
| 60Hz | 150% | non-AOT | 300 | 3 | 9.136-10.937ms | 10.183-11.410ms | 12.368-13.873ms | 297-300 / 300 | Default partial apply and `--no-partial-apply` manual scroll smokes: no text lag | `TestResults/diagnose-sync-60hz-150pct-non-aot.summary.txt` |
| 60Hz | 150% | AOT | 300 | 3 process-isolated | 7.140-15.262ms | 15.415-16.596ms | 16.185-19.021ms | 137-298 / 300 | Numeric only | `TestResults/diagnose-sync-60hz-150pct-aot-separate.summary.txt` |
| 120Hz | 150% | non-AOT | 300 | 3 | 3.934-4.685ms | 5.735-6.780ms | 7.046-8.122ms | 296-299 / 300 | Default partial apply manual scroll smoke: no text lag | `TestResults/diagnose-sync-120hz-150pct-non-aot.summary.txt` |
| 120Hz | 150% | AOT | 300 | 3 process-isolated | 0.952-4.105ms | 1.819-5.003ms | 1.990-6.063ms | 0-297 / 300 | Numeric only | `TestResults/diagnose-sync-120hz-150pct-aot-separate.summary.txt` |
| 144Hz | 150% | non-AOT / AOT | 300 | Not run | Not run | Not run | Not run | Not run | Current display has no 144Hz mode | Not applicable |
| 240Hz | 150% | non-AOT | 300 | 3 | 3.319-3.589ms | 3.896-4.208ms | 4.326-4.406ms | 292-299 / 300 | Prior manual scroll smoke: no text lag | `TestResults/diagnose-sync-240hz-150pct-non-aot.summary.txt` |
| 240Hz | 150% | AOT | 300 | 3 process-isolated | 1.197-3.223ms | 1.810-3.960ms | 2.279-4.251ms | 2-297 / 300 | Numeric only | `TestResults/diagnose-sync-240hz-150pct-aot-separate.summary.txt` |

Interpretation: sync wait is not acceptable against the provisional `<2ms average` performance target on this machine. The issue is also not isolated to 240Hz: 60Hz and 120Hz non-AOT runs exceed the target, while AOT has high process-to-process variance. Correctness is acceptable for the manually smoked paths because text no longer lags at 60Hz or 120Hz with default `SyncTextOverlay=true`; the high wait cost is therefore an optimization follow-up, not a reason to disable synchronization.

### Text Overlay Sync Correctness Invariant

The 60Hz text-lag regression must not return. `D3D12Renderer.SyncTextOverlay` remains `true` by default, and `--no-sync-text-overlay` is only an A/B diagnostic escape hatch. Irix must not disable default synchronization to hit a performance number; any optimization must still guarantee that the D2D overlay has completed against the presentable back buffer before `Present`.

### Sync Strategy Review (2026-05-13)

Decision: **keep `D3D12FenceAfterOverlay` as the default sync strategy. Do not adopt `D3D11Query` as the default yet.**

Current `D3D12Renderer.RenderFrame` already waits only on frames that contain text. Rect-only frames transition to present and skip the sync wait. Text frames render the D3D12 rect pass, run the D3D11On12/D2D overlay, call D3D11 `Flush`, then synchronize before `Present`. The selected sync strategy is internal diagnostic state, not a public backend contract.

Primitive inventory:

| Candidate | Guarantees D2D overlay completion? | Guarantees presentable back buffer before `Present`? | Decision |
|-----------|-------------------------------------|------------------------------------------------------|----------|
| D3D11 event query after `ReleaseWrappedResources` + `Flush` | Yes for D3D11 immediate-context work queued before the query; manual 60Hz / 120Hz / 240Hz smokes showed no stale text | Yes in the observed D3D11On12 path because the query is waited before `Present` | Correctness-pass, performance-mixed; keep diagnostic-only |
| D3D11 fence | Not adopted; not currently confirmed as a generated/available primitive in this code path | Not proven | Reject for this batch |
| D3D12 fence after overlay (`D3D12FenceAfterOverlay`) | Yes for all work submitted to the shared D3D12 queue after D3D11On12 flush | Yes; this is the current correctness control | Keep default |
| Flush-only | No. `Flush` submits D3D11 work but does not wait for completion | No | Reject |
| Wait after `Present` | Too late; stale overlay can already have been presented | No | Reject |
| Present before overlay wait | No; this reopens the 60Hz text-lag bug | No | Reject |

Strategy spike evidence, non-AOT unless noted:

| Refresh | Scale | Runtime | Strategy | Avg sync wait | P95 sync wait | Max sync wait | Waits >2ms | Manual smoke |
|---------|-------|---------|----------|---------------|---------------|---------------|------------|--------------|
| 60Hz | 150% | non-AOT | `D3D12FenceAfterOverlay` | 13.725-15.207ms | 14.304-15.702ms | 14.804-15.925ms | 298-300 / 300 | Prior default correctness smoke: no lag |
| 60Hz | 150% | non-AOT | `D3D11Query` | 4.541-5.956ms | 4.996-6.403ms | 5.080-6.656ms | 297-299 / 300 | Default and `--no-partial-apply`: no lag |
| 120Hz | 150% | non-AOT | `D3D12FenceAfterOverlay` | 1.435-1.951ms | 1.730-2.213ms | 1.917-2.316ms | 0-159 / 300 | Prior default correctness smoke: no lag |
| 120Hz | 150% | non-AOT | `D3D11Query` | 3.867-4.405ms | 4.198-4.708ms | 4.628-5.068ms | 297-299 / 300 | Default: no lag |
| 240Hz | 150% | non-AOT | `D3D12FenceAfterOverlay` | 2.150-2.388ms | 2.403-2.596ms | 2.653-2.811ms | 85.0%-94.3% | Prior default correctness smoke: no lag |
| 240Hz | 150% | non-AOT | `D3D11Query` | 1.215-1.468ms | 1.407-1.635ms | 1.688-1.890ms | 0 / 300 | Default: no lag |
| 240Hz | 150% | AOT | `D3D12FenceAfterOverlay` | 1.938-2.153ms | 2.219-2.365ms | 2.508-2.782ms | 118-266 / 300 | Numeric only |
| 240Hz | 150% | AOT | `D3D11Query` | 0.878-1.072ms | 1.038-1.265ms | 1.489-1.786ms | 0 / 300 | Numeric only |
| 240Hz | 150% | AOT, process-isolated | `D3D11Query` | 0.711-3.895ms | 0.935-4.124ms | 1.228-4.568ms | 0-295 / 300 | Numeric variance check |

Interpretation: `D3D11Query` is a useful spike and proves the overlay can be waited from the D3D11 side without a public contract change, but it is not stable enough to become the default because it regresses 120Hz on the current machine and still misses the 2ms target at 60Hz. Candidate B does not produce a distinct implementation: the current D3D12 fence is already placed immediately after overlay flush and before `Present`; moving it later violates correctness, and moving it earlier cannot cover the overlay. The follow-up remains a renderer-level sync primitive or an explicit accepted budget, not disabling default sync.

### Text Cache / Allocation Evidence (2026-05-13)

Command shape:

```powershell
.\scripts\ga-baseline.ps1 -Mode TextCache -TextCacheFrames 180 -RefreshLabel current -ScalePercent 200
```

Local runs use 180 frames per scenario.

| Scale | Actual refresh | Static layout / allocation | Scroll layout / allocation | Scale-change layout / allocation | Pool behavior | Evidence |
|-------|----------------|----------------------------|----------------------------|----------------------------------|---------------|----------|
| 100% | 120Hz | 99.4%, 3,154 bytes/frame | 99.8%, 6,824 bytes/frame | 99.3%, 3,121 bytes/frame | Warm pool reuses all frames after initial creation; 0 overflow disposals | `TestResults/diagnose-text-cache-current-100pct-non-aot.summary.txt` |
| 150% | 120Hz | 99.4%, 3,258 bytes/frame | 99.8%, 6,928 bytes/frame | 99.3%, 3,173 bytes/frame | Warm pool reuses all frames after initial creation; 0 overflow disposals | `TestResults/diagnose-text-cache-120hz-150pct-non-aot.summary.txt` |
| 200% | 120Hz | 99.4%, 3,257 bytes/frame | 99.8%, 6,928 bytes/frame | 99.3%, 3,121 bytes/frame | Warm pool reuses all frames after initial creation; 0 overflow disposals | `TestResults/diagnose-text-cache-current-200pct-non-aot.summary.txt` |

Interpretation: layout caching is stable in steady-state, scroll, and scale-change scenarios at 100% / 150% / 200%. Cache evictions remain zero. Format hit-rate has a small denominator in these scenarios, so layout hit-rate and eviction count are the better regression signal. The resource pool is warm after the first scenario and reuses every frame afterward.

### Platform Matrix Evidence

This matrix is evidence tracking, not a claim of complete multi-GPU or multi-display GA coverage.

| Refresh | Scale | Runtime | Partial apply mode | Current evidence | Status |
|---------|-------|---------|--------------------|------------------|--------|
| 60Hz | 150% | non-AOT | default / `--no-partial-apply` | Numeric sync wait captured; both partial modes manually smoked with no text lag | Covered for current display |
| 60Hz | 150% | AOT | default diagnostic path | Process-isolated numeric sync wait captured | Numeric-only |
| 120Hz | 150% | non-AOT | default / D3D11Query A/B | Numeric sync wait captured; manual smokes found no text lag | Covered for current display |
| 120Hz | 150% | AOT | default diagnostic path | Process-isolated numeric sync wait captured | Numeric-only |
| 144Hz | 150% | non-AOT / AOT | default / `--no-partial-apply` | Current display reports no 144Hz mode | Unavailable locally |
| 240Hz | 150% | non-AOT | default / D3D11Query A/B | Numeric sync wait captured; manual smokes found no text lag | Covered for current display |
| 240Hz | 150% | AOT | default diagnostic path | Process-isolated numeric sync wait captured | Numeric-only |
| 120Hz | 100% | non-AOT | default | Text cache/allocation diagnostic captured; manual visual/hit-test/scroll smoke normal | Covered for current display |
| 120Hz | 200% | non-AOT | default | Text cache/allocation diagnostic captured; manual visual/hit-test/scroll smoke normal | Covered for current display |

## Platform Integration

Version boundary regression note: the Windows PoC runtime minimum is 10.0.15063.0, while the framework target SDK is 10.0.26100.0. The CI SDK check guards the target SDK side; GA platform matrix work should validate the runtime side separately.

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
| Windows SDK 26100 CI check | CI verifies .NET 10 SDK and Windows SDK 10.0.26100.0 before restore/build | Already done | — |
| CI test suite | Windows 2025 / SDK 26100 matrix lane runs normal tests | Maintain green | — |
| D3D12-specific tests | Headless D3D12 smoke matrix lane runs `Category=D3D12` with graceful skip when D3D12 unavailable | Already done | — |
| Platform matrix CI | Minimal Windows 2025 / SDK 26100 lanes for tests, headless D3D12, performance baseline, and AOT publish | Already done | — |
| Performance regression CI | `Category=Performance` mock backend frame-time baseline + warm `FrameDrawingResources` allocation baseline | Already done | — |
| Sync wait regression baseline | Semi-automatic local diagnostic via `scripts/ga-baseline.ps1 -Mode Sync`; not a hard CI gate because hosted runners do not provide stable refresh/scale/GPU timing | Keep latest local baselines in `TestResults/` and GA plan; optimization follow-up tracks the >2ms budget miss | P1 |
| Text cache/allocation baseline | Semi-automatic local diagnostic via `scripts/ga-baseline.ps1 -Mode TextCache`; CI guards pool allocation with a hardware-independent performance test | Already done for 100% / 150% / 200% on current machine | P2 |
| Manual smoke baseline | Semi-automatic local runner via `scripts/ga-baseline.ps1 -Mode Smoke`; user verifies scroll/text sync, hit-test, and scale behavior after switching refresh/scale | Latest 60Hz / 120Hz / 100% / 200% smokes passed on current display | P2 |

---

## GA Readiness Assessment

**Current state:** PoC V1 core architecture-complete. Windows version boundary centralized (Target SDK 26100, runtime minimum 15063). Display scale pipeline complete and hand-tested (100%/150%/200%). AOT mode runtime scale/refresh switching verified. Device-lost recovery complete. GA hardening first batch complete (2026-05-13). D2D text overlay synchronization complete and default-on (2026-05-13). Minimal CI matrix covers normal tests, headless D3D12, performance baseline, and AOT publish. Current-machine text cache/allocation diagnostics are healthy at 100% / 150% / 200%; sync wait still needs an accepted budget or a more robust renderer-level optimization. The D3D11 query spike is correctness-clean in manual smoke but not adopted as default because performance is refresh-rate dependent.

**Minimum for GA:**
1. ~~Device-lost recovery (P0)~~ — Done
2. ~~1000-frame soak test (P1)~~ — Done (3 tests: render count, empty/nonempty interleave, memory stability)
3. ~~Resize stress test (P1)~~ — Done (4 tests: scale consistency, extreme sizes, runtime scale change, 1000 resizes)
4. ~~Frame time profiling (P1)~~ — Done (compositor-level: LastFrameTimeUs, AverageFrameTimeUs, MaxFrameTimeUs)
5. ~~D3D12 smoke tests in CI (P1)~~ — Done (7 headless tests, 1s total)
6. ~~D2D text overlay sync (P0)~~ — Done (GPU fence wait, default-on, 4 regression tests)
7. ~~Concurrent input + render (P1)~~ — Done (5 tests: sequential scroll, pump dispatch, coalescing, thread safety, multi-cycle)
8. ~~Windows SDK 26100 CI check (P0)~~ — Done
9. ~~Performance regression CI baseline (P1)~~ — Done (mock backend frame timing + warm resource-pool allocation)

**Remaining before GA:** renderer-level sync wait optimization follow-up or an explicit accepted budget, validation on hardware that actually exposes 144Hz, broader hardware coverage beyond this single display/GPU, and selected platform integration checks. Irix is not GA-ready yet.

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

### Item 6: D2D Text Overlay Synchronization (P0) — DONE

**Entry point:** `src/Irix.Platform.Windows/D3D12Renderer.cs`, `tests/Irix.Core.Tests/ScrollTextSyncTests.cs`

**Problem (2026-05-13):** At 60Hz, button text visibly lags behind rectangles during scrolling. At 120Hz the lag is mild; at 240Hz nearly invisible. Text catches up when scrolling stops. Root cause: D3D12 rect rendering and D3D11on12/D2D text overlay share the same command queue but were not synchronized before Present, causing the text overlay to render one frame behind the rects.

**Implementation (2026-05-13):**
1. `D3D12Renderer.SyncTextOverlay` property (default: `true`) controls GPU fence wait
2. `D3D12Renderer.WaitForQueueIdle()` signals fence on D3D12 queue, waits for completion via `SetEventOnCompletion` + `WaitForSingleObject`
3. Fence inserted in `RenderFrame` after D2D text overlay, before `Present()`
4. CLI escape hatch: `--no-sync-text-overlay` disables the fence for performance comparison
5. 4 scroll text-sync regression tests: rect/text same-frame batch, position tracking, rapid scroll, stop/resume

**Verification:** Manual testing confirmed text-lag eliminated at 60Hz/120Hz/240Hz with sync enabled. Lag returns when sync disabled via `--no-sync-text-overlay`.

**Prohibited scope:** No per-draw-call GPU timing. No D3D12 timestamp queries. No visual regression.

---

**Explicit non-goals for v1.0 GA:**
- HDR / wide color gamut
- Screen reader accessibility
- Multi-monitor hot-plug
- Fractional DPI awareness (physical pixels sufficient for v1.0)
