# v1 Retained / Partial Apply Prep

> Coarse precondition inventory for the next retained-frame / partial-apply line. This document does not enable StyleOnly fast-path, move retained tree ownership, change compositor behavior, or alter renderer behavior.

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
| Retained-input snapshot seam | Implemented locally | `RenderPipeline.LastRetainedInputSnapshot` collects retained layout result, element command ranges, hit targets, retained root, viewport, dirty classifications, dirty ranges, and rebuild reason. |

## 2. Blocked Decisions

| Blocker | Why it blocks implementation |
|---------|------------------------------|
| Cross-frame resource strategy | Text slices and style resources are frame-resource scoped; true cross-frame partial apply needs stable handles, resource snapshots, or another ownership model. |
| Fast-path input shape | Current style-only planner consumes next layout output, so it cannot be the actual layout-skipping input boundary. |
| Retained layout ownership | `RenderPipeline` owns retained layout state today; promotion of partial apply needs explicit owner/lifetime rules. |
| Hit target metadata patching | Bounds/clip can be retained, but metadata updates need a stable projection from next `VirtualNode` without layout. |
| Fallback reporting | A future partial path needs explicit `AppliedPartial`, `FallbackFull`, or `Rejected` result data without expanding diagnostics channel scope. |

## 3. Retained-Input Snapshot Seam

Current seam:

- `RenderPipeline.LastRetainedInputSnapshot` is local/internal only.
- It is written after the existing full layout and draw recording path has produced the current frame data.
- It copies element command ranges, hit targets, dirty classifications, dirty element ranges, and dirty command ranges for stable test reads.
- It references the existing retained `LayoutTreeResult`; it does not create an alternate layout path.
- It carries the retained root and viewport that `RenderPipeline` already owns.

Regression scope:

- Snapshot tests prove hover-only / style-only changes keep retained layout geometry, tree ranges, scroll diagnostics, command ranges, and hit target geometry stable.
- Snapshot tests prove dirty classifications match the existing diagnostics fields.
- Snapshot tests prove dirty command ranges and hit targets match the emitted `RenderFrameBatch`.
- `RenderPipeline.Build` still rebuilds layout for style-only changes; this seam is not a fast path.

## 4. Cross-Frame Resource Blockers

True cross-frame partial apply is still blocked by frame-scoped resource ownership. Text content and style resources are resolved through `FrameDrawingResources`; `TextSlice` values are valid only while their owning frame resources are retained.

| Direction | Benefit | Cost / risk | Current decision |
|-----------|---------|-------------|------------------|
| Stable text/style handles | Commands could reference durable resource ids across frames. | Requires a resource table, lifetime/recycling rules, invalidation strategy, and backend cache coordination. | Postponed; likely larger than the next safe line. |
| Resource snapshot per retained frame | Retained commands keep the exact resources they need, even across frames. | Multiple resource snapshots can coexist; memory ownership and resolver lookup become more complex. | Viable design direction, but not implemented. |
| Ownership model with explicit transfer/retain | Keeps current frame-resource shape but formalizes when retained frames own resources. | Still does not solve replacing one command range with resources from a different frame unless mixed-resource resolution exists. | Current v0 model only supports full apply across frames and same-frame partial pilot. |

Minimum blocker conclusion: do not attempt cross-frame partial apply until replacement commands can resolve both retained old resources and current new resources safely.

## 5. Local Partial Apply Result Vocabulary

Future partial paths should report local result data without expanding the global diagnostics surface.

| Draft result | Meaning | Notes |
|--------------|---------|-------|
| `AppliedPartial` | The local retained path replaced only dirty command ranges and kept retained state valid. | Only valid after resource ownership, hit target metadata, and retained root updates all succeed. |
| `FallbackFull` | The path intentionally used the existing full layout/full apply behavior. | Correctness-preserving fallback; should carry a local reason. |
| `Rejected` | The proposed partial path was not safe before mutating retained state. | Must be side-effect-free, like current `TryApplyPartial` failure. |

Candidate local fallback reasons:

| Draft reason | Meaning |
|--------------|---------|
| `NotStyleOnly` | Dirty classification includes layout/text/tree/viewport-affecting work. |
| `ViewportChanged` | Retained layout viewport does not match current viewport. |
| `MissingRetainedSnapshot` | Required retained layout/command/hit-target data is unavailable. |
| `UnstableCommandRange` | Dirty element ranges cannot map cleanly to command ranges. |
| `ResourceOwnershipMismatch` | Replacement commands cannot safely resolve resources across frames. |
| `HitTargetPatchFailed` | Hit target geometry/metadata cannot be patched from retained data. |

This vocabulary remains local to future retained/partial planning. It must not add a diagnostics channel, event bus, or formatter surface in this line.

## 6. Safest Next Implementation Line

The safest next implementation line is not a full partial-rendering feature. After the retained-input snapshot seam, the next safe step is a local planner that consumes the snapshot but still falls back to the existing full path:

1. Keep using `RenderPipeline.LastRetainedInputSnapshot` as the only retained input bundle.
2. Add a data-only planner result using the local vocabulary above.
3. Keep `RenderPipeline.Build` on the existing full layout path.
4. Do not apply replacement commands across frames until resource ownership is solved.

## 7. Guardrails

- Do not enable StyleOnly fast-path.
- Do not skip layout rebuilds for style-only changes yet.
- Do not broaden diagnostics formatter/channel scope.
- Do not move retained tree or translator ownership.
- Do not alter backend rendering behavior.
