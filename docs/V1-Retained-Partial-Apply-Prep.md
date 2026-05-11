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
| Retained data-only local planner | Implemented locally | `RetainedPartialApplyPlanner` consumes the snapshot and returns `AppliedPartial`, `FallbackFull`, or `Rejected` planning data without changing render behavior. Planner-only boundary tests cover every local reason. |
| Partial apply preflight scaffold | Implemented locally / not wired | [V1-Partial-Apply-Preflight-Design.md](V1-Partial-Apply-Preflight-Design.md) selects resource snapshot / composite resolver, defines internal resource segment, segmented reader with malformed-coverage guards, `SegmentedRetainedFrameOwner` shadow owner with opt-in `SegmentedRetainedFrameDiagnosticHarness`, `SegmentedRetainedFrameRuntimeOwner` seam draft, default-off `SegmentedRetainedFrameProductionOwnerFeed`, hardened `DrawingBackendCompositorShadowProbe`, local shadow result vocabulary, default-off / enabled-secondary no-change sentinels, empty batch and malformed dirty range fallback tests, per-segment backend adapter prototype, hit target metadata projector, retained root metadata patcher, dry-run flow, and layered preflight / shadow / production-off / production integration gate evidence. Runtime checkpoint: [V1-Partial-Apply-Runtime-Integration-Checkpoint.md](V1-Partial-Apply-Runtime-Integration-Checkpoint.md). |

## 2. Blocked Decisions

| Blocker | Why it blocks implementation |
|---------|------------------------------|
| Cross-frame resource strategy | Text slices and style resources are frame-resource scoped; true cross-frame partial apply needs stable handles, resource snapshots, or another ownership model. |
| Fast-path input shape | Current style-only planner consumes next layout output, so it cannot be the actual layout-skipping input boundary. |
| Retained layout ownership | `RenderPipeline` owns retained layout state today; promotion of partial apply needs explicit owner/lifetime rules. |
| Hit target metadata patching | Bounds/clip can be retained, but metadata updates need a stable projection from next `VirtualNode` without layout. |
| Fallback reporting | Local vocabulary exists, but it is not wired to runtime/compositor or diagnostics formatter. |

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
| Resource snapshot per retained frame | Retained commands keep the exact resources they need, even across frames. | Multiple resource snapshots can coexist; memory ownership and resolver lookup become more complex. | Recommended next design direction, likely through a composite resolver. |
| Ownership model with explicit transfer/retain | Keeps current frame-resource shape but formalizes when retained frames own resources. | Still does not solve replacing one command range with resources from a different frame unless mixed-resource resolution exists. | Current v0 model only supports full apply across frames and same-frame partial pilot. |

Recommendation: use a resource snapshot / composite resolver direction before stable global handles. It preserves the current frame-scoped `FrameDrawingResources` model while giving a future partial path a way to resolve both old retained commands and new replacement commands. Stable handles remain a larger later option because they imply cache identity, recycling, invalidation, and backend coordination. Explicit retain/transfer remains a necessary ownership guardrail, but by itself it does not solve mixed old/new resource resolution. The detailed preflight design and internal scaffold are in [V1-Partial-Apply-Preflight-Design.md](V1-Partial-Apply-Preflight-Design.md).

Minimum blocker conclusion: do not attempt cross-frame partial apply until replacement commands can resolve both retained old resources and current new resources safely.

## 5. Local Partial Apply Result Vocabulary

Local partial paths report result data without expanding the global diagnostics surface.

| Draft result | Meaning | Notes |
|--------------|---------|-------|
| `AppliedPartial` | The local retained path has enough data to describe a partial update. | Data-only today; no command ranges are replaced. |
| `FallbackFull` | The path intentionally uses the existing full layout/full apply behavior. | Correctness-preserving fallback with a local reason. |
| `Rejected` | The proposed partial path is not safe before mutating retained state. | Side-effect-free, like current `TryApplyPartial` failure. |

Candidate local fallback reasons:

| Draft reason | Meaning |
|--------------|---------|
| `NotStyleOnly` | Dirty classification includes layout/text/tree/viewport-affecting work. |
| `ViewportChanged` | Retained layout viewport does not match current viewport. |
| `MissingRetainedSnapshot` | Required retained layout/command/hit-target data is unavailable. |
| `UnstableCommandRange` | Dirty element ranges cannot map cleanly to command ranges. |
| `ResourceOwnershipMismatch` | Replacement commands cannot safely resolve resources across frames. |
| `HitTargetPatchFailed` | Hit target geometry/metadata cannot be patched from retained data. |

This vocabulary remains local to retained/partial planning. It must not add a diagnostics channel, event bus, or formatter surface in this line.

## 6. Hit Target Metadata Patching

Future hit target patching must separate retained geometry from reprojected metadata.

| Hit target data | Future handling | Fallback trigger |
|-----------------|-----------------|------------------|
| Bounds | Preserve from retained layout when dirty classification is style-only and viewport is unchanged. | Any bounds change, layout-affecting dirty reason, tree structure change, or missing retained layout. |
| Clip bounds | Preserve from retained layout when root/nested clip identity is unchanged. | Viewport change, scroll/layout change, clip-affecting attributes, or changed container geometry. |
| Action id | Reproject from the next `VirtualNode` / control metadata for dirty hit targets. | Missing next node, target count/order mismatch, or control/action identity ambiguity. |
| Hit target count/order | Preserve only when dirty element ranges map to stable command/hit-target ranges. | Added/removed hit targets or unstable element-to-command mapping. |
| Visual command metadata | Reproject hover/pressed/focused style inputs for replacement draw commands. | Any text-size/layout-affecting metadata, changed label measurement, or unknown attribute. |

Current preflight scaffold includes local metadata projection from next `VirtualNode` for hit-target action ids and retained-root control metadata. It remains internal/test-only and is not connected to `RenderPipeline.Build`. A future runtime planner must read retained geometry plus next-node metadata without consuming next layout output.

## 7. Safest Next Implementation Line

The safest next implementation line is not a full partial-rendering feature. After the retained-input snapshot seam and data-only local planner, the next safe step is still planner-only:

1. Keep using `RenderPipeline.LastRetainedInputSnapshot` as the only retained input bundle.
2. Keep every local planner result reason covered by planner-only regression tests.
3. Keep `RenderPipeline.Build` on the existing full layout path.
4. Do not apply replacement commands across frames until resource ownership is solved.
5. Require every gate in [V1-Partial-Apply-Preflight-Design.md](V1-Partial-Apply-Preflight-Design.md) before any runtime hookup.

Checkpoint decision: the first runtime seam should be retained frame segment ownership, not compositor segmented execution. The compositor cannot safely execute segments until a retained frame owner can expose correct command-range resolver ownership.

The current preflight dry-run is only a decision-flow proof. It does not replace production command ranges, mutate `RetainedRenderFrame`, or touch compositor/backend behavior. A separate opt-in diagnostic harness can consume real `RenderPipeline.Build` output in tests to prove segmented-frame ownership shape, accepted partial rehearsal, explicit full fallback, local `Disabled` / `ShadowAppliedPartial` / `ShadowFallbackFull` / `ShadowRejected` results, disabled-mode no-change behavior, long-lived runtime-owner lifecycle, and compositor-external segmented execution, but it remains outside production `RenderPipeline.Build`.

## 8. Guardrails

- Do not enable StyleOnly fast-path.
- Do not skip layout rebuilds for style-only changes yet.
- Do not broaden diagnostics formatter/channel scope.
- Do not move retained tree or translator ownership.
- Do not alter backend rendering behavior.
