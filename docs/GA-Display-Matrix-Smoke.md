# GA Display Matrix Smoke Checklist

> Repeatable smoke test for display scale + refresh rate + resize + runtime scale change + partial apply. Run before any GA validation checkpoint.

---

## Prerequisites

- Windows 10/11 with D3D12-capable GPU
- Build: `dotnet build -c Debug`
- Run: `dotnet run --project src/Irix.Poc --no-build`
- At least one of: 100% DPI test machine, or ability to change system DPI

## Matrix

| Test | Scale | Refresh Rate | Resize | Runtime Scale Change | Partial Apply |
|------|-------|-------------|--------|---------------------|---------------|
| A | 100% | 60Hz | Yes | No | Default-on |
| B | 125% | 60Hz | Yes | No | Default-on |
| C | 150% | 60Hz | Yes | No | Default-on |
| D | 200% | 60Hz | Yes | No | Default-on |
| E | 100% | 120Hz+ | Yes | No | Default-on |
| F | 150% | 60Hz | Yes | Yes | Default-on |
| G | 100% | 60Hz | Yes | No | Forced-off |

## Smoke Steps (per matrix row)

### 1. Startup

| Step | Action | Expected |
|------|--------|----------|
| 1.1 | Launch Counter PoC | Console shows `Display scale: 1x1` (or `1.25x1.25`, etc.) |
| 1.2 | Console shows `Partial apply: ENABLED (default)` (or `DISABLED` for row G) | Correct partial apply state |
| 1.3 | Verify buttons render | Text visible, no rendering artifacts, correct font size |

### 2. Interaction

| Step | Action | Expected |
|------|--------|----------|
| 2.1 | Click + button 5 times | Counter increments to 5, text updates correctly |
| 2.2 | Click - button | Counter decrements |
| 2.3 | Press R | Counter resets to 0 |
| 2.4 | Mouse wheel scroll | Scroll responds, content moves |

### 3. Resize

| Step | Action | Expected |
|------|--------|----------|
| 3.1 | Drag window to resize (10+ seconds) | Content reflows, no crash, text/buttons scale correctly |
| 3.2 | Resize to very small | Content clips, no crash |
| 3.3 | Maximize then restore | Content renders correctly |

### 4. Text consistency

| Step | Action | Expected |
|------|--------|----------|
| 4.1 | Verify button labels | Text is crisp, correct font size relative to button rect |
| 4.2 | Verify counter number | Number updates on each click |
| 4.3 | Rapid click + 20 times | All text updates render correctly |

### 5. Text overlay sync (scroll)

| Step | Action | Expected |
|------|--------|----------|
| 5.1 | Mouse wheel scroll continuously for 10+ seconds | Button text stays synchronized with rectangles, no visible lag |
| 5.2 | Stop scrolling, then resume | Text catches up on stop, stays sync on resume |
| 5.3 | Rapid scroll at 60Hz | No text lag behind rectangles |

### 6. Hit-test

| Step | Action | Expected |
|------|--------|----------|
| 6.1 | Click each button precisely at edges | Hit-test works, no offset |
| 6.2 | Click between buttons | No false activation |

### 7. Runtime scale change (row F only)

| Step | Action | Expected |
|------|--------|----------|
| 6.1 | Change system DPI (Settings → Display → Scale) | Window resizes, content relayouts |
| 6.2 | Verify text/buttons | Correct font size, no rendering artifacts |
| 6.3 | Verify hit-test | Clicks still work at correct positions |
| 6.4 | Change DPI back | Returns to original scale correctly |

### 8. Diagnostics

| Step | Action | Expected |
|------|--------|----------|
| 7.1 | Launch with `--diagnose-resize` | Output shows `scale=`, `logicalViewport=`, `physicalViewport=` |
| 7.2 | Verify scale values match system DPI | Correct scale factors |

## Pass Criteria

- All steps produce expected results for each matrix row
- No crashes, no exceptions in console
- No rendering artifacts (flickering, stale content, wrong colors)
- Text and button visual proportions consistent across all scales
- Hit-test accurate at all scales
- Resize + relayout works at all scales
- Runtime scale change triggers correct relayout (row F)

## Regression Triggers

Re-run this checklist after changes to:
- `DrawingBackendCompositor` (compositor logic, scale boundary)
- `WindowDrawCommandTranslator` (physical→logical conversion, scale batch)
- `FrameDrawingResources` (text style scaling)
- `D3D12DrawingBackend` (backend accumulate/submit)
- `D3D12TextRenderer` (text cache, style resolution, font size)
- `D3D12Renderer` (sync fence, `WaitForQueueIdle`, `SyncTextOverlay`)
- `WindowsNativeWindow` (WM_DPICHANGED handling)
- `DisplayScale` / `FrameContext` types
- `DrawCommand.Scale` / `HitTestTarget.Scale` methods

## Current Results (2026-05-13)

| Row | Scale | Refresh | Resize | Runtime Change | Partial Apply | Result |
|-----|-------|---------|--------|---------------|---------------|--------|
| A | 100% | 60Hz | ✅ | — | Default-on | PASS |
| B | 125% | 60Hz | ✅ | — | Default-on | PASS (manual) |
| C | 150% | 60Hz | ✅ | — | Default-on | PASS |
| D | 200% | 60Hz | ✅ | — | Default-on | PASS |
| E | 100% | 120Hz+ | — | — | Default-on | Not tested |
| F | 150% | 60Hz | ✅ | ✅ | Default-on | PASS (AOT mode verified) |
| G | 100% | 60Hz | ✅ | — | Forced-off | PASS (manual) |

**AOT mode verification (2026-05-13):** Runtime real-time switching of display scale and refresh rate in AOT mode — text, buttons, hit-test, scroll, resize all correct after scale/refresh change. No rendering artifacts or stale content.

## Scroll Text Overlay Sync Verification (2026-05-13)

**Problem:** At 60Hz, button text visibly lags behind rectangles during scrolling. At 120Hz the lag is mild; at 240Hz nearly invisible. Text catches up when scrolling stops. Root cause: D3D12 rect pass and D3D11on12/D2D text overlay not synchronized before Present.

**Fix:** `D3D12Renderer.WaitForQueueIdle()` — GPU fence wait after D2D text overlay, before Present. Default: `SyncTextOverlay=true`.

| Test | Sync | Refresh | Scroll | Result |
|------|------|---------|--------|--------|
| H | Default-on (`SyncTextOverlay=true`) | 60Hz | Continuous wheel scroll | PASS — text/rect stay synchronized, no visible lag |
| I | Default-on | 120Hz | Continuous wheel scroll | PASS — no lag |
| J | Default-on | 240Hz | Continuous wheel scroll | PASS — no lag |
| K | Disabled (`--no-sync-text-overlay`) | 60Hz | Continuous wheel scroll | EXPECTED FAIL — text lags rect (confirms sync is required) |
| L | Default-on | 60Hz | Rapid click + 20 times | PASS — all text updates render correctly |
| M | Default-on | 60Hz | Scroll stop + resume | PASS — text catches up on stop, stays sync on resume |

**Sync overhead:** Measurable via `--diagnose-sync` (frame serial diagnostics). Target: <2ms/frame avg at 60Hz.

**Regression coverage:** 4 scroll text-sync tests in `ScrollTextSyncTests.cs`:
- `Continuous_scroll_rect_and_text_same_frame_batch` — rect/text in same Execute call
- `Continuous_scroll_text_positions_track_rect_positions` — Y positions match
- `Rapid_scroll_no_frame_skips_text` — 200 rapid frames, no rect-without-text
- `Scroll_stop_resume_text_stays_synchronized` — pause/resume consistency
