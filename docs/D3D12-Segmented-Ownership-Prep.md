# D3D12 Segmented Ownership Prep

> Inventory of D3D12 backend requirements for supporting per-segment execute and segment-local dirty ranges in production.

---

## Current D3D12 Architecture

The `D3D12DrawingBackend` uses a three-phase frame model:

| Phase | GPU work | CPU work |
|-------|----------|----------|
| `BeginFrame` | Reset command allocator + command list | Reset `_rects` and `_texts` lists |
| `Execute` | None | Accumulate DrawCommands into `FrameRenderList` buffers |
| `EndFrame` | Full GPU submission: transition, clear, render, present, fence | Flush accumulated buffers |

This architecture **already supports** multiple `Execute` calls per frame — commands from all segments are batched into a single GPU submission.

## What Already Works

| Capability | Status | Notes |
|-----------|--------|-------|
| Multiple Execute per frame | Works | CPU-only accumulation; all segments batch into one GPU submission |
| IDirtyRangeAware | Implemented | Stores ranges for diagnostic observation; does not affect rendering |
| Segment-local dirty ranges | Works via routing backend | `DirtyRangeRoutingBackend` feeds segment-local ranges before each Execute |
| Text resolution with mixed resolvers | Fixed (2026-05-13) | Was broken: `_resources` overwritten each Execute, EndFrame used last resolver for all text runs. Fixed by eagerly resolving styles and storing per-text-run resolver in `TextData` |
| Empty text handling | Works | `Resolve().IsEmpty` check skips empty text gracefully |

## What Needs Validation

### 1. Text Renderer Cache Safety

**Risk:** The `D3D12TextRenderer` caches `IDWriteTextFormat` (max 64) and `IDWriteTextLayout` (max 256) keyed by text content + style handle. When segments use different resolvers (old frame + replacement frame), the cache could serve a layout created with a different `IDWriteTextFormat` if the style handle is reused across frames.

**Current mitigation:** `ResourceHandle` values are per-`FrameDrawingResources` instance. Different resolvers would have different style handles for different styles, but the same style (e.g., default text style) could map to the same handle value across different `FrameDrawingResources` instances if they're from the same pool slot.

**Validation needed:** Confirm that cache key collisions between old and replacement resolvers do not produce incorrect text rendering. The cache key includes the `ResourceHandle` value, which is an index into the per-frame style list — different `FrameDrawingResources` instances could have the same index mapping to different `TextStyle` values.

**Recommendation:** Add the resolver identity (or `FrameId`) to the text cache key, or validate that the current cache key is sufficient because retained frames hold their own `FrameDrawingResources` with unique style lists.

**Validated (2026-05-13):** Cache keys are safe. The text format cache uses `TextStyle` (value equality on 8 fields) as key, not `ResourceHandle`. The text layout cache uses `TextLayoutCacheKey(TextHash, TextLength, TextStyle, Width, Height)`. Neither cache includes `ResourceHandle` in its key. The resolver is only used to resolve `ResourceHandle` → `TextStyle` before cache lookup. Different resolvers producing the same `TextStyle` will correctly hit the same cache entry.

### 2. Background Color Heuristic

**Risk:** `_bgR/_bgG/_bgB/_bgA` are set from the first `FillRect` command across all segments (line 196-199). With per-segment execution, the first segment's FillRect determines the background color for the entire frame.

**Current impact:** Minimal — the background color is used for the clear color in `RenderFrame`. If the first segment is the old (retained) segment, its background color should be identical to the replacement segment's background color. If they differ, the clear color would be wrong.

**Validation needed:** Confirm that background color is consistent across old and replacement segments in practice.

### 3. Vertex Buffer Capacity

**Risk:** `D3D12Renderer2D` has a fixed 1024-quad vertex buffer. Per-segment execution does not change the total command count, so this is not a new risk — but if segments are rendered independently (not batched), the buffer would need per-segment reset.

**Current impact:** None — the current architecture batches all segments into one submission, so the total quad count is the same as a full frame.

### 4. Device-Removed During Segmented Frame

**Risk:** If device-removed occurs during `EndFrame` (after some segments' commands are accumulated but before present), the compositor's selected-source state may be inconsistent with the production retained frame.

**Current mitigation:** The `D3D12DrawingBackend` checks `_deviceRemoved` in `BeginFrame`, `Execute`, and `EndFrame` and returns no-ops. The compositor's `ExecuteSelectedHandoffFrame` catches exceptions and sets `BackendThrewBeforeCommit` reason, preserving the previous state.

**Validation needed:** Confirm that the exception path correctly preserves both the compositor's retained frame and the owner's segment state.

**Validated (2026-05-13):**
- **Segmented path (handoff):** `SegmentedBackendExecutionAdapter.Execute` pairs `BeginFrame`/`EndFrame` via try/finally. `ExecuteSelectedHandoffFrame` catches exceptions, sets `BackendThrewBeforeCommit`, re-throws. Compositor counters and hit targets only updated after successful return. ✅
- **Non-handoff path:** `DrawingBackendCompositor.RenderAsync` direct backend call path (lines 160-175) previously had NO try/finally around `BeginFrame`/`Execute`/`EndFrame`. **Fixed (2026-05-13):** Added try/finally to pair `EndFrame` with `BeginFrame`. On exception, `EndFrame` now guaranteed to run. Compositor counters/hit targets are updated after the backend call, so they naturally skip on exception. ✅

### 5. Segment-Local Dirty Range Semantics

**Risk:** The `IDirtyRangeAware` contract says "treat as read-only diagnostic data; full-frame rendering behavior must not change." If D3D12 were to use dirty ranges for partial rendering (skipping unchanged commands), the per-segment dirty ranges would need careful intersection with the segment's command range.

**Current impact:** None — dirty ranges are diagnostic-only. No code change needed for default-on.

**Future consideration:** If D3D12 partial rendering is pursued (skipping unchanged commands within a segment), the backend would need to consult dirty ranges during `Execute` and skip commands outside the dirty set. This is a separate optimization, not required for default-on.

---

## Summary

| Item | Severity | Required for default-on? | Required for GA? | Status |
|------|----------|------------------------|-----------------|--------|
| Text cache safety | Medium | Yes (validation only) | Yes | ✅ Validated — cache keys use TextStyle value equality, not ResourceHandle |
| Resolver misrouting (per-segment) | High | Yes (fix required) | Yes | ✅ Fixed — eager style resolution + per-text-run resolver in TextData |
| Background color heuristic | Low | No | No | Not validated (low risk) |
| Vertex buffer capacity | None | No | No | N/A |
| Device-removed during segment | Medium | Yes (guard only) | Yes (full recovery) | ✅ Guard verified — segmented path try/finally OK, non-handoff path fixed |
| Segment-local dirty range use | None | No | Future optimization | N/A |

**Minimum for default-on:** ✅ All items validated or fixed.

**Minimum for GA:** Full device-lost recovery, platform matrix validation.
