# GA Hardening Plan

> Current GA/MVP hardening state for Irix Windows PoC. V1 core is architecture-complete; this document tracks only GA readiness and post-GA renderer work.

---

## Current decision summary

| Area | Decision |
|------|----------|
| V1 core | Complete / regression-only. Do not reopen V1 core feature work for GA hardening. |
| Partial apply | Default-on; `--no-partial-apply` remains rollback. |
| Display scale | Complete / regression-only; 100% / 150% / 200% local evidence accepted. |
| Refresh matrix | GA evidence is limited to actually available local modes: 60Hz / 120Hz / 240Hz. 144Hz validation is removed from GA scope because no 144Hz hardware is available in the current environment. |
| Text overlay sync | Keep `SyncTextOverlay=true` and default `D3D12FenceAfterOverlay`. `D3D11Query` remains diagnostic-only. |
| Sync wait budget | Temporarily accepted for GA/MVP correctness. The current wait cost is documented and no longer blocks GA by itself. |
| Long-term text renderer direction | Replace the current D3D12 + D3D11On12 + D2D/DirectWrite overlay with a D3D12-only text path using an internal glyph atlas, similar in spirit to Impeller-style glyph atlas rendering. |

---

## Device Resilience

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Device-lost detection | `_deviceRemoved` flag + `DeviceErrorReason` string + `DeviceLost` event | Already done | — |
| Device-lost recovery | `D3D12Renderer.TryRecover()` reconstructs all GPU resources; compositor catches backend exceptions, checks `IDeviceRecovery`, attempts recovery; tests cover recovery succeeds/fails | Already done | — |
| Device-removed during segmented frame | Compositor catches exceptions in standard and handoff paths; recovery attempted through `IDeviceRecovery` | Already done | — |
| GPU memory pressure | Runtime resize/recovery resource recreation failures surface explicit device error reasons, including `E_OUTOFMEMORY` | Accepted for current GA/MVP; no silent fail or undefined pointer continuation | — |
| Command allocator reset failure | `BeginFrame` command allocator/list reset retries once after `WaitForGpu`; persistent failure escalates through device-lost/recovery | Already done | — |

---

## Display Matrix

Windows version boundary: Irix v1 Windows PoC targets Windows SDK 10.0.26100.0 through `net10.0-windows10.0.26100.0`, while the runtime minimum remains Windows 10 1703 / 10.0.15063.0. This keeps the PerMonitorV2 manifest and display scale pipeline on a clear runtime OS floor without tying runtime support to the target SDK.

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| 60Hz refresh | Numeric sync wait captured; manual smoke passed | Already done for current display | — |
| 120Hz refresh | Numeric sync wait captured; manual smoke passed | Already done for current display | — |
| 240Hz refresh | Numeric sync wait captured; manual smoke passed | Already done for current display | — |
| 144Hz refresh | Removed from GA scope: no current hardware coverage | Not required for current GA/MVP evidence | — |
| DPI scaling 100% / 150% / 200% | Platform-neutral `DisplayScale`; compositor owns scale boundary; layout in logical units; backend in physical pixels; text/font scaled consistently; `WM_DPICHANGED` runtime handling; PerMonitorV2 manifest | Already done for current display | — |
| Multi-monitor | Single monitor only | Broader hardware follow-up, not current GA blocker | P2 |
| Fractional DPI | 150% covered; 125% not validated | Follow-up only if hardware/settings available | P2 |
| HDR / wide color gamut | Not applicable | Not required for v1.0 GA | P3 |

---

## Stability

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| 1000-frame soak test | Render count, empty/nonempty interleave, memory stability | Already done | — |
| Long-run memory stability | 1100-frame memory growth test, 100 warmup + 1000 soak, <50% growth threshold | Already done | — |
| Resize stress test | Scale consistency, extreme sizes, runtime scale change, 1000 rapid resizes | Already done | — |
| Concurrent input + render | Sequential scroll render, ScrollFramePump dispatch, rapid coalescing, thread-safe AddPendingPixels, multi-cycle render | Already done | — |
| Exception recovery | Compositor catches backend exceptions; `IDeviceRecovery` interface; recovery success/fail tests | Already done | — |
| D2D text overlay sync under scroll | Default-on sync after D2D text overlay and before `Present`; `--no-sync-text-overlay` remains diagnostic only | Already done | — |
| Startup / resize background flicker | Fixed 2026-05-14: `DrawCommandBatch.Memory` now exposes only logical `Count`, preventing pooled backing-buffer tail data from being retained as random `FillRect` commands | Already done | — |

---

## Performance

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Frame time profiling | Compositor-level `LastFrameTimeUs`, `AverageFrameTimeUs`, `MaxFrameTimeUs` via `Stopwatch` | Already done | — |
| Partial apply overhead measurement | Frame time profiling can compare partial vs full path; measure through `PartialApplyCount` / `FullApplyCount` | Already done | — |
| Performance regression CI | `Category=Performance` covers mock backend frame timing, warm `FrameDrawingResources`, split frame-stage allocation baseline, and D3D12 `ExecuteCore` 100% / 150% scale allocation guards. Latest local per-stage byte output is tracked in `Project_Status_and_Todo.md`. | Already done | — |
| Text cache hit rate in steady state | `scripts/ga-baseline.ps1 -Mode TextCache` validates static, scroll, and scale-change phases; current-machine 100% / 150% / 200% runs remain healthy | Already done for current machine | — |
| DrawCommand recording allocation | `stackalloc` + `ArrayPool`; record full/dirty stages are covered by the split allocation baseline, with warm `FrameDrawingResources` guarded in CI | Keep performance lane green | P2 |
| Hot-path string allocation | Round 13/14 complete (2026-05-16): text content uses `TextNodeContent` / `TextBufferSnapshot` plus frame-local `TextSlice`; `ActionId`, `TargetId`, `ElementId`, and `NodeKey` are typed value ids; style/property changes use `VirtualPropertyKey` and metadata effects instead of string property names. `VirtualPropertyKey` has no public constructor and no primitive `ToString()`. Diagnostics strings remain exempt. | Keep source guards green; next work is allocation baseline tightening, not string-key redesign | P2 |
| Sync wait overhead | Current default `D3D12FenceAfterOverlay` is correctness-preserving but can exceed the old provisional `<2ms avg` target. `D3D11Query` was correctness-clean but refresh-rate dependent and therefore diagnostic-only. | Accepted temporarily for GA/MVP; long-term fix is D3D12-only glyph atlas text renderer | — |

### Sync Wait Evidence and Decision (2026-05-13)

Canonical local runner:

```powershell
.\scripts\ga-baseline.ps1 -Mode Sync -Frames 300 -Samples 3 -RefreshLabel 60Hz -ScalePercent 150 -SyncStrategy D3D12FenceAfterOverlay
.\scripts\ga-baseline.ps1 -Mode Sync -Frames 300 -Samples 3 -RefreshLabel 120Hz -ScalePercent 150 -SyncStrategy D3D12FenceAfterOverlay
.\scripts\ga-baseline.ps1 -Mode Sync -Frames 300 -Samples 3 -RefreshLabel 240Hz -ScalePercent 150 -SyncStrategy D3D12FenceAfterOverlay
.\scripts\ga-baseline.ps1 -Mode Sync -Frames 300 -Samples 3 -RefreshLabel 120Hz -ScalePercent 150 -SyncStrategy D3D11Query
```

| Refresh | Scale | Runtime | Strategy | Avg sync wait | P95 sync wait | Max sync wait | Waits >2ms | Manual smoke |
|---------|-------|---------|----------|---------------|---------------|---------------|------------|--------------|
| 60Hz | 150% | non-AOT | `D3D12FenceAfterOverlay` | 13.725-15.207ms | 14.304-15.702ms | 14.804-15.925ms | 298-300 / 300 | No text lag |
| 60Hz | 150% | non-AOT | `D3D11Query` | 4.541-5.956ms | 4.996-6.403ms | 5.080-6.656ms | 297-299 / 300 | No text lag |
| 120Hz | 150% | non-AOT | `D3D12FenceAfterOverlay` | 1.435-1.951ms | 1.730-2.213ms | 1.917-2.316ms | 0-159 / 300 | No text lag |
| 120Hz | 150% | non-AOT | `D3D11Query` | 3.867-4.405ms | 4.198-4.708ms | 4.628-5.068ms | 297-299 / 300 | No text lag |
| 240Hz | 150% | non-AOT | `D3D12FenceAfterOverlay` | 2.150-2.388ms | 2.403-2.596ms | 2.653-2.811ms | 85.0%-94.3% | No text lag |
| 240Hz | 150% | non-AOT | `D3D11Query` | 1.215-1.468ms | 1.407-1.635ms | 1.688-1.890ms | 0 / 300 | No text lag |
| 240Hz | 150% | AOT | `D3D12FenceAfterOverlay` | 1.938-2.153ms | 2.219-2.365ms | 2.508-2.782ms | 118-266 / 300 | Numeric only |
| 240Hz | 150% | AOT | `D3D11Query` | 0.878-1.072ms | 1.038-1.265ms | 1.489-1.786ms | 0 / 300 | Numeric only |

Interpretation:

- The old provisional `<2ms avg` target is no longer a GA blocker.
- Correctness has priority: 60Hz text lag must not return.
- `D3D11Query` proves the overlay can be waited from the D3D11 side, but it is not stable enough to become the default because it regresses 120Hz on this machine and still misses 2ms at 60Hz.
- `D3D12FenceAfterOverlay` remains default because it is the current correctness control.
- The accepted long-term path is not further tuning D3D11On12/D2D overlay synchronization. The long-term fix is a D3D12-only glyph atlas text renderer that removes the D3D11On12 + D2D/DirectWrite overlay path.

### Text Overlay Sync Correctness Invariant

The 60Hz text-lag regression must not return. `D3D12Renderer.SyncTextOverlay` remains `true` by default, and `--no-sync-text-overlay` is only an A/B diagnostic escape hatch. Irix must not disable default synchronization to hit a performance number.

### Render-Core No-String Allocation Invariant

The render hot path (`RenderPipeline.Build`, `DrawCommandRecorder`, `DrawingBackendCompositor.RenderAsync`, `LayoutTreeBuilder`) must not allocate `string` instances per frame. Current compliance:

| Area | Status | Detail |
|------|--------|--------|
| DrawCommand text content | Compliant | `TextSlice` over frame-local pooled `char[]` arena; zero string allocation |
| VirtualNode text content | Compliant | `TextNodeContent` indexes `VirtualTextArena`; layout/draw resolves through `TextBufferSnapshot.ResolveRequired` |
| TextStyle.FontFamily | Compliant | String flows through but is not allocated per frame; set once at style creation |
| Layout rebuild reason | Compliant | `LayoutRebuildReason` is already a `byte` enum |
| Dirty classification | Compliant | Property changes accumulate `PropertyChangeSet` and classify through `StyleEffect` / `InvalidationKind`; no string property-name set |
| Property name lookup | Compliant | `VirtualPropertyKey` is a pure value key; public keys come from static readonly fields and metadata |
| HitTestTarget.ActionId | Compliant | `ActionId` is a typed value id from VirtualNode property through layout and compositor hit-test |
| Style/property string values | Compliant by exclusion | No public `FontFamily` string property, no `PropertyValue.Text`, no string hex color path |
| Diagnostics output | Exempt | Diagnostic strings are not on the per-frame render path |

Source guards cover primitive `ActionId.ToString()`, primitive `VirtualPropertyKey.ToString()`, missing property metadata, key/value mismatches, global layout key reintroduction, and style string value factories. Future allocation work should measure and tighten baselines without reopening string style keys.

---

## Future Renderer Work: D3D12-only Glyph Atlas Text Renderer

Current text path:

```text
D3D12 rect pass -> D3D11On12 / D2D / DirectWrite overlay -> sync wait -> Present
```

Accepted future direction:

```text
D3D12 rect pass -> D3D12 glyph atlas text pass -> Present
```

| Task | Scope | Acceptance Criteria | Priority |
|------|-------|---------------------|----------|
| Glyph atlas design | Define atlas ownership, glyph key, eviction, scale handling, subpixel/antialiasing policy, and fallback font strategy | Design doc accepted; no public API change | P1 post-GA/MVP |
| CPU glyph raster source | Use DirectWrite only for glyph shaping/raster source if needed; do not use D3D11On12/D2D overlay for final composition | Glyph bitmaps uploadable to D3D12 atlas | P1 post-GA/MVP |
| D3D12 text pass | Render text quads from atlas in the same D3D12 frame path as rectangles | Text and rects are in the same D3D12 synchronization domain | P1 post-GA/MVP |
| Cache diagnostics | Track atlas hits/misses, uploads, evictions, bytes, and frame upload cost | Diagnostics comparable to current text-cache baseline | P2 |
| Migration guard | Keep current D2D overlay path until atlas path passes correctness and baseline smokes | No regression in text quality, clipping, scale, hit-test, scroll sync | P1 post-GA/MVP |

Non-goals for this GA batch:

- Do not rewrite the text renderer now.
- Do not introduce Skia.
- Do not change `IDrawingBackend.Execute`.
- Do not expose glyph atlas details as public API.

---

## Platform Integration

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Window minimize/restore | Manual smoke passed on current machine (2026-05-13): minimize, restore, resize, scroll, click | Already done for current display | — |
| Window occlusion | Manual smoke passed on current machine (2026-05-13): full occlusion, restore, continued scroll/click, no stale frame | Already done for current display | — |
| System DPI change (live) | Manual smoke passed on current machine (2026-05-13): startup at 100% / 150% / 200%, runtime switch 200% -> 100% -> 150%, resize + scroll | Already done for current display | — |
| High-contrast mode | Not validated | Validate text readability | P3 |
| Screen reader accessibility | Not applicable | Not required for v1.0 GA | P3 |

---

## Testing Infrastructure

| Item | Current state | Required for GA | Priority |
|------|--------------|----------------|----------|
| Windows SDK 26100 CI check | CI verifies .NET 10 SDK and Windows SDK 10.0.26100.0 before restore/build | Already done | — |
| CI test suite | Windows 2025 / SDK 26100 matrix lane runs normal tests | Maintain green | — |
| D3D12-specific tests | Headless D3D12 smoke matrix lane runs `Category=D3D12` with graceful skip when D3D12 unavailable | Already done | — |
| Platform matrix CI | Minimal Windows 2025 / SDK 26100 lanes for tests, headless D3D12, performance baseline, and AOT publish | Already done | — |
| Performance regression CI | `Category=Performance` mock backend frame-time baseline + split frame-stage allocation baseline (`BuildView`, diff, retained apply, layout, record, D3D12 execute, render-request reuse) + warm `FrameDrawingResources` allocation baseline. Exact latest local byte output is recorded in `Project_Status_and_Todo.md`. | Already done | — |
| Sync wait regression baseline | Semi-automatic local diagnostic via `scripts/ga-baseline.ps1 -Mode Sync`; not a hard CI gate because hosted runners do not provide stable refresh/scale/GPU timing | Keep as local evidence; accepted budget documented above | — |
| Text cache/allocation baseline | Semi-automatic local diagnostic via `scripts/ga-baseline.ps1 -Mode TextCache`; CI guards pool allocation with a hardware-independent performance test | Already done for 100% / 150% / 200% on current machine | — |
| Manual smoke baseline | Semi-automatic local runner via `scripts/ga-baseline.ps1 -Mode Smoke`; user verifies scroll/text sync, hit-test, resize, occlusion, minimize/restore, and scale behavior | Latest default, rollback, 100% / 150% / 200%, and runtime scale-switch smokes passed on current display | — |

---

## GA Readiness Assessment

**Current state:** PoC V1 core architecture-complete. Windows version boundary centralized: Target SDK 26100, runtime minimum 15063. Display scale pipeline is complete and hand-tested at 100% / 150% / 200%. Device-lost recovery, soak, resize stress, frame-time profiling, D3D12 smoke tests, concurrent input/render validation, performance CI, D2D text overlay synchronization, platform integration smokes, GPU resource failure handling, and command allocator reset guards are complete. Sync wait cost is accepted temporarily as a correctness-first tradeoff; the long-term performance fix is a D3D12-only glyph atlas text renderer.

**Minimum GA checklist:**

1. ~~Device-lost recovery~~ — Done
2. ~~1000-frame soak test~~ — Done
3. ~~Resize stress test~~ — Done
4. ~~Frame time profiling~~ — Done
5. ~~D3D12 smoke tests in CI~~ — Done
6. ~~D2D text overlay sync~~ — Done, default-on
7. ~~Concurrent input + render~~ — Done
8. ~~Windows SDK 26100 CI check~~ — Done
9. ~~Performance regression CI baseline~~ — Done
10. ~~Sync wait budget decision~~ — Done: accepted temporarily; future D3D12-only glyph atlas renderer planned
11. ~~Platform integration smokes~~ — Done: minimize/restore, occlusion, resize, scroll/click, rollback, startup scale, runtime scale switch
12. ~~GPU memory pressure graceful path~~ — Done for V1 scope: explicit resource creation failure reasons, including `E_OUTOFMEMORY`
13. ~~Command allocator reset failure guard~~ — Done: retry once after GPU wait, then device-lost escalation

**Remaining before GA tag:** review and commit the candidate snapshot, then create the candidate tag. 144Hz hardware validation and glyph atlas implementation are post-GA follow-ups, not blockers for this candidate.

---

## Explicit non-goals for v1.0 GA

- HDR / wide color gamut
- Screen reader accessibility
- Multi-monitor hot-plug
- 144Hz-specific validation without real hardware
- Rewriting the current text renderer before GA
- Replacing the D3D11On12/D2D overlay before GA
- Introducing Skia
