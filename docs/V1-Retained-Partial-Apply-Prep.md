# v1 Retained / Partial Apply Prep

> Coarse precondition inventory for the next retained-frame / partial-apply line. This document does not change `RenderPipeline`, enable StyleOnly fast-path, move retained tree ownership, or alter renderer behavior.

## 1. Current Assets

| Area | Status | Notes |
|------|--------|-------|
| `RetainedTree` patch apply | Already available | Applies diff patches and returns sorted dirty DFS indices for translator/pipeline use. |
| Layout dirty classification | Already available | `RenderPipeline` exposes rebuild reason and dirty classifications for diagnostics/tests. |
| Layout element ranges | Already available | `LayoutTreeResult` maps dirty DFS nodes to dirty element ranges. |
| Element to command ranges | Already available | `DrawCommandRecorder` exposes `ElementCommandRange[]` and dirty command ranges. |
| `RenderFrameBatch.DirtyCommandRanges` | Already available | Batch carries dirty ranges to compositor/backend boundary. |
| `RetainedRenderFrame` | Already available | Supports full apply, same-frame partial apply, resource ownership, `TryReadFrame`, and pure failure paths. |
| `DrawingBackendCompositor` retained frame pilot | Already available | Applies retained frame and records partial/full counters; cross-frame partial remains guarded. |
| StyleOnly plan diagnostics | Already available | Planning remains post-layout diagnostics only; not a fast path. |

## 2. Blocked Decisions

| Blocker | Why it blocks implementation |
|---------|------------------------------|
| Cross-frame resource strategy | Text slices and style resources are frame-resource scoped; true cross-frame partial apply needs stable handles, resource snapshots, or another ownership model. |
| Fast-path input shape | Current style-only planner consumes next layout output, so it cannot be the actual layout-skipping input boundary. |
| Retained layout ownership | `RenderPipeline` owns retained layout state today; promotion of partial apply needs explicit owner/lifetime rules. |
| Hit target metadata patching | Bounds/clip can be retained, but metadata updates need a stable projection from next `VirtualNode` without layout. |
| Fallback reporting | A future partial path needs explicit `AppliedPartial`, `FallbackFull`, or `Rejected` result data without expanding diagnostics channel scope. |

## 3. Safest Next Implementation Line

The safest next implementation line is not a full partial-rendering feature. It should be a retained-input snapshot seam inside `RenderPipeline` tests or internals:

1. Capture retained layout result, element command ranges, hit targets, retained root, viewport, and dirty classifications as one local snapshot.
2. Add tests proving the snapshot is stable across hover-only/style-only changes while current layout rebuild behavior remains unchanged.
3. Keep `RenderPipeline.Build` on the existing full layout path.
4. Do not apply replacement commands across frames until resource ownership is solved.

## 4. Guardrails

- Do not enable StyleOnly fast-path.
- Do not skip layout rebuilds for style-only changes yet.
- Do not broaden diagnostics formatter/channel scope.
- Do not move retained tree or translator ownership.
- Do not alter backend rendering behavior.
