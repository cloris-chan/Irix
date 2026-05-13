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
| 120Hz / 144Hz / 240Hz | 240Hz current-machine numeric sync wait captured; 120Hz manual no-lag smoke only; 144Hz not yet covered | Validate animation timing and fence behavior | P1 |
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
| Performance regression CI | Mock backend frame timing baseline and warm `FrameDrawingResources` allocation baseline in `Category=Performance`; CI lane runs them separately | Already done | — |
| Text cache hit rate in steady state | `--diagnose-text-cache 180` validates static, scroll, and scale-change phases; current 240Hz / 150% run keeps layout cache hit-rate above 99% with no evictions | Already done for current machine; broaden with platform matrix | P2 |
| DrawCommand recording allocation | `stackalloc` + `ArrayPool`; warm `FrameDrawingResources` allocation baseline in CI; current diagnostic shows text-resource allocation stable after pool warmup | Keep performance lane green and broaden numeric matrix | P2 |
| Sync wait overhead | `--diagnose-sync 300 3` measured on local 240Hz / 150% scale display. Latest 3 samples: avg 3.372-3.584ms, p95 4.033-4.265ms, max 4.277-4.465ms, 274-296/300 waits >2ms. Default sync remains enabled; prior manual smoke found no text lag at 60Hz/120Hz/240Hz. | Collect numeric 60Hz / 120Hz runs after manually switching display modes; decide whether to accept hardware-specific budget or defer a deeper renderer-level optimization | P1 |

### Sync Wait Evidence (2026-05-13)

Command shape for repeatable local runs:

```powershell
dotnet run --project src/Irix.Poc/Irix.Poc.csproj -c Release -- --diagnose-sync 300 3 *> TestResults\diagnose-sync-<refresh>hz-<scale>pct.txt
```

| Refresh | Scale | Frames / sample | Samples | Avg sync wait | P95 sync wait | Max sync wait | Waits >2ms | Evidence |
|---------|-------|-----------------|---------|---------------|---------------|---------------|------------|----------|
| 60Hz | Pending numeric | 300 | Pending | Pending | Pending | Pending | Pending | Requires manually switching display mode; default sync must remain enabled and no text lag is required |
| 120Hz | Pending numeric | 300 | Pending | Pending | Pending | Pending | Pending | Requires manually switching display mode; previous manual smoke found no text lag |
| 144Hz | Pending numeric | 300 | Pending | Pending | Pending | Pending | Pending | Not yet covered |
| 240Hz | 150% | 300 | 3 | 3.372-3.584ms | 4.033-4.265ms | 4.277-4.465ms | 274-296 / 300 | `TestResults/diagnose-sync-240hz-150pct-multisample.txt` |

Interpretation: the current 240Hz variance is no longer classified as a single occasional spike. The latest three-sample run is consistently above the 2ms target, so the remaining question is whether this machine/environment has unusually high D3D11On12/D2D queue completion cost or whether the default sync strategy needs a deeper redesign. Because disabling the wait reintroduces text lag, this is a correctness-first follow-up, not a reason to change the default.

### Sync Strategy Review (2026-05-13)

Current `D3D12Renderer.RenderFrame` already waits only on frames that contain text. Rect-only frames transition to present and skip the sync wait. Text frames render the D3D12 rect pass, run the D3D11On12/D2D overlay, call D3D11 `Flush`, then signal the shared D3D12 direct queue and wait before `Present`.

Within the current backend contract this is the narrowest correctness-preserving fence available: D3D11On12 text work is submitted to the same D3D12 queue, and the renderer does not expose a separate D2D-only completion primitive. Waiting only after `Flush` but before `Present` avoids presenting a back buffer whose text overlay is still pending. Avoiding the wait or moving it past `Present` is not accepted for GA because the known 60Hz text-lag bug returns. Further reduction would require a renderer-level redesign that exposes a more granular overlay completion fence; that is intentionally outside this GA hardening batch.

### Text Cache / Allocation Evidence (2026-05-13)

Command shape:

```powershell
dotnet run --project src/Irix.Poc/Irix.Poc.csproj -c Release -- --diagnose-text-cache 180 *> TestResults\diagnose-text-cache-<refresh>hz-<scale>pct.txt
```

Current local run: 240Hz / 150% scale, 180 frames per scenario.

| Scenario | Format cache | Layout cache | Allocation | FrameDrawingResources pool | Evidence |
|----------|--------------|--------------|------------|----------------------------|----------|
| Static | 2 hits / 1 miss, 66.7%, cached 1, evictions 0 | 537 hits / 3 misses, 99.4%, cached 3, evictions 0 | 586,504 bytes total, 3,258 bytes/frame | 180 rents, 178 reused, 2 created, 0 overflow disposals | `TestResults/diagnose-text-cache-240hz-150pct.txt` |
| Scroll | 1 hit / 0 misses, 100.0%, cached 1, evictions 0 | 539 hits / 1 miss, 99.8%, cached 4, evictions 0 | 1,247,208 bytes total, 6,928 bytes/frame | 180 rents, 180 reused, 0 created, 0 overflow disposals | `TestResults/diagnose-text-cache-240hz-150pct.txt` |
| Scale change | 3 hits / 1 miss, 75.0%, cached 2, evictions 0 | 536 hits / 4 misses, 99.3%, cached 8, evictions 0 | 571,264 bytes total, 3,173 bytes/frame | 180 rents, 180 reused, 0 created, 0 overflow disposals | `TestResults/diagnose-text-cache-240hz-150pct.txt` |

Interpretation: layout caching is stable in the steady-state scenarios and after an internal scale change; cache evictions remain zero. Format hit-rate has a small denominator in these scenarios, so layout hit-rate and eviction count are the better regression signal. The resource pool is warm after the first scenario and reuses every frame afterward.

### Platform Matrix Evidence

This matrix is evidence tracking, not a claim of complete multi-GPU or multi-display GA coverage.

| Refresh | Scale | Runtime | Partial apply mode | Current evidence | Status |
|---------|-------|---------|--------------------|------------------|--------|
| 60Hz | 100% / 150% / 200% | non-AOT | default / `--no-partial-apply` | Manual no-lag smoke only; numeric sync wait pending | Open |
| 120Hz | 100% / 150% / 200% | non-AOT | default / `--no-partial-apply` | Manual no-lag smoke only; numeric sync wait pending | Open |
| 144Hz | 100% / 150% / 200% | non-AOT | default / `--no-partial-apply` | No local evidence yet | Open |
| 240Hz | 150% | non-AOT | diagnostic path | Numeric sync wait + text cache/allocation diagnostics captured | Partial |
| 240Hz | 100% / 200% | non-AOT | default / `--no-partial-apply` | Scale pipeline hand-tested; fresh numeric sync/cache evidence pending | Open |
| Current display | Current scale | AOT | default | AOT publish and runtime scale/refresh verification completed earlier; fresh sync wait numeric pending | Partial |

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
| Sync wait regression baseline | Semi-automatic local diagnostic via `--diagnose-sync <frames> <samples>`; not a hard CI gate because hosted runners do not provide stable refresh/scale/GPU timing | Keep latest local baselines in `TestResults/` and GA plan | P1 |
| Text cache/allocation baseline | Semi-automatic local diagnostic via `--diagnose-text-cache <frames>`; CI guards pool allocation with a hardware-independent performance test | Already done for current machine; broaden matrix | P2 |

---

## GA Readiness Assessment

**Current state:** PoC V1 core architecture-complete. Windows version boundary centralized (Target SDK 26100, runtime minimum 15063). Display scale pipeline complete and hand-tested (100%/150%/200%). AOT mode runtime scale/refresh switching verified. Device-lost recovery complete. GA hardening first batch complete (2026-05-13). D2D text overlay synchronization complete and default-on (2026-05-13). Minimal CI matrix covers normal tests, headless D3D12, performance baseline, and AOT publish. Current-machine text cache/allocation diagnostics are healthy; 240Hz sync wait remains above the provisional 2ms target.

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

**Remaining before GA:** numeric sync wait follow-up for 60Hz / 120Hz modes, 144Hz coverage, broader Windows/platform matrix evidence across scale/runtime/partial-apply modes, a decision on the consistently high 240Hz sync wait budget, and selected platform integration checks. Irix is not GA-ready yet.

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
