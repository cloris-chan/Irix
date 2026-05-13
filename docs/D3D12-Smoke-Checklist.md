# D3D12 Smoke Checklist

> Repeatable smoke test for default-on partial apply regression. Run after any change to `DrawingBackendCompositor`, `SegmentedBackendExecutionAdapter`, `D3D12DrawingBackend`, `D3D12TextRenderer`, or `SegmentedRetainedFrameProductionOwnerFeed`.

---

## Prerequisites

- Windows 10/11 with D3D12-capable GPU
- Build: `dotnet build -c Debug`
- Run: `dotnet run --project src/Irix.Poc --no-build`

## Smoke Steps

### 1. Default startup (partial apply enabled)

| Step | Action | Expected |
|------|--------|----------|
| 1.1 | Launch Counter PoC (no flags) | Console shows `Partial apply: ENABLED (default)` |
| 1.2 | Verify first visible frame | Background is the normal theme color; no black/green flash or oversized random rectangle |
| 1.3 | Verify buttons render | Text visible, no rendering artifacts |
| 1.4 | Click + button 5 times | Counter increments to 5, text updates correctly |
| 1.5 | Click - button | Counter decrements |
| 1.6 | Press R | Counter resets to 0 |
| 1.7 | Mouse wheel scroll | Scroll responds, content moves |

### 2. Resize

| Step | Action | Expected |
|------|--------|----------|
| 2.1 | Drag window to resize (10+ seconds) | Content reflows, no crash |
| 2.2 | Resize to very small | Content clips, no crash |
| 2.3 | Maximize then restore | Content renders correctly |

### 3. Text rendering

| Step | Action | Expected |
|------|--------|----------|
| 3.1 | Verify button labels | Text is crisp, correct font |
| 3.2 | Verify counter number | Number updates on each click |
| 3.3 | Rapid click + 20 times | All text updates render correctly |

### 4. Forced-off fallback

| Step | Action | Expected |
|------|--------|----------|
| 4.1 | Launch with `--no-partial-apply` | Console shows `Partial apply: DISABLED (--no-partial-apply)` |
| 4.2 | Verify first visible frame | Background is the normal theme color; no black/green flash or oversized random rectangle |
| 4.3 | Click buttons, scroll, resize | Same behavior as default-on |

### 4a. Repeated startup flicker check

Run both startup modes repeatedly on the same machine:

```powershell
dotnet run --project src/Irix.Poc --no-build
dotnet run --project src/Irix.Poc --no-build -- --no-partial-apply
```

Expected: across 30-50 launches per mode, no black/green startup flash, no partial-window random background rectangle, and no startup crash.

### 4b. Text overlay sync (scroll)

| Step | Action | Expected |
|------|--------|----------|
| 4b.1 | Launch default mode | Console shows `Sync text overlay: ENABLED (default)` |
| 4b.2 | Mouse wheel scroll continuously for 10+ seconds | Button text stays synchronized with rectangles, no visible lag |
| 4b.3 | Stop scrolling, then resume | Text catches up on stop, stays sync on resume |
| 4b.4 | Launch with `--no-sync-text-overlay` | Console shows `Sync text overlay: DISABLED (--no-sync-text-overlay)` |
| 4b.5 | Scroll at 60Hz | Text visibly lags behind rectangles (confirms sync is required) |

### 5. Console compositor

| Step | Action | Expected |
|------|--------|----------|
| 5.1 | Launch with `--console` | Console output shows draw commands |
| 5.2 | Click buttons | Both console and D3D12 output update |

### 6. Debug UI

| Step | Action | Expected |
|------|--------|----------|
| 6.1 | Launch with `--debug-ui` | Debug diagnostics overlay visible |
| 6.2 | Click buttons | Diagnostics update |

## HiDPI Note

The PoC includes `app.manifest` with PerMonitorV2 DPI awareness. Windows-targeted projects target Windows SDK 26100 and declare Windows 10 1703 / 10.0.15063.0 as the runtime minimum so the display scale pipeline does not rely on older OS boundaries. Under HiDPI (150%, 200%), verify that:
- Buttons are clickable (hit-test works)
- Text renders correctly (not corrupted)
- Resize works

## Pass Criteria

- All steps produce expected results
- No crashes, no exceptions in console
- No rendering artifacts (flickering, stale content, wrong colors, random background rectangles)

## Regression Triggers

Re-run this checklist after changes to:
- `DrawingBackendCompositor` (compositor logic)
- `SegmentedBackendExecutionAdapter` (per-segment execute)
- `D3D12DrawingBackend` (backend accumulate/submit)
- `D3D12Renderer` (sync fence, `WaitForQueueIdle`, `SyncTextOverlay`, present)
- `D3D12TextRenderer` (text cache, style resolution)
- `SegmentedRetainedFrameProductionOwnerFeed` (ownership feed)
- `RetainedRenderFrameSegmentOwnership` (segment ownership)
- `CompositorLoop` (render dispatch)
