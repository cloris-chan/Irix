# v1 Partial Apply Runtime Integration Checkpoint

> Checkpoint only. This document does not enable partial apply, does not change `RenderPipeline.Build`, does not change `RetainedRenderFrame`, does not change `DrawingBackendCompositor`, does not change `IDrawingBackend.Execute`, and does not make any gate satisfied.

## 1. Current Preflight Evidence

| Gate | Preflight evidence exists | Runtime evidence still missing |
|------|---------------------------|--------------------------------|
| Resource resolver ownership | `RetainedResourceSegmentTable`, `SegmentedRetainedFrameReader`, and segmented backend adapter prototype prove command-range resolver ownership. | Production retained frame does not expose segmented resource ownership. |
| Resource dispose policy | Segment table and segmented retained-frame prototype tests prove full apply, partial accept, invalidate, dispose, and replaced-snapshot release rules. | Production retained frame does not own multiple retained snapshots. |
| Command range stability | Planner and segment reader tests reject unstable, invalid, overlapping, non-contiguous, and command-count-mismatched ranges. | Runtime still does not replace cross-frame command ranges. |
| Hit target metadata projection | `HitTargetMetadataProjector` proves action metadata projection from next root without next layout output. | Hit-test runtime still consumes full layout output from the existing path. |
| Retained root update | `RetainedRootMetadataPatcher` proves dirty control metadata projection for `ActionId`, `IsHovered`, `IsPressed`, and `IsFocused`. | `RenderPipeline` retained root baseline is not patched by any partial path. |
| Fallback reporting | `RetainedPartialApplyPlanner` reports local `AppliedPartial`, `FallbackFull`, and `Rejected` reasons. | No runtime/compositor result propagation exists. |
| Compositor ownership | Dry-run sentinel tests keep planner/projector/root patcher/segment reader flow outside compositor mutation. | `DrawingBackendCompositor` still owns one retained frame and one resolver boundary. |
| Regression coverage | Focused preflight tests and existing layout/compositor tests cover no-change behavior. | Runtime partial apply has no hookup to cover yet. |

`PartialApplyIntegrationGateChecklist.CanHookUpPartialApply` must remain false until runtime evidence exists for every gate.

## 2. First Runtime Seam Decision

The first runtime seam should be retained frame segment ownership, not compositor segmented execution.

Reason: compositor segmented execution requires a retained frame that can already answer which resolver owns each command range. Without that owner, a compositor adapter would either guess resource scope or require an immediate backend contract change. Retained frame segment ownership is the smallest seam because it can stay internal, sit beside the existing `RetainedRenderFrame` path, and prove lifetime rules before any backend execution path changes.

First seam shape:

```text
RetainedCommandBuffer
  + RetainedResourceSegmentTable
  + retained root metadata snapshot
  -> segmented read facade
```

Current test-only prototype: `SegmentedRetainedFramePrototype`. It owns a command buffer, resource segment table, and retained root metadata, but it is not used by `RetainedRenderFrame` or `DrawingBackendCompositor`.

## 3. Backend Execution Adapter Decision

Preferred adapter direction: per-segment execute.

| Option | Decision | Impact on `IDrawingBackend.Execute` |
|--------|----------|-------------------------------------|
| Per-segment execute | Preferred first adapter. Call the existing backend `Execute` once per contiguous resource segment with that segment's resolver. | No signature change; possible multiple `Execute` calls between one `BeginFrame` and `EndFrame`. |
| Composite resolver adapter | Not first. A resolver alone cannot know which command owns which resource scope unless command-range metadata is already available. | No signature change, but unsafe without segment ownership metadata. |
| Resource rebase | Postponed. Requires copying text/style resources into one namespace and rewriting commands. | No signature change after rebase, but much higher implementation risk. |
| Stable global handles | Explicitly out of scope. | Would change the resource model and backend cache identity rules. |

Current test-only prototype: `SegmentedBackendExecutionAdapter`. It executes segment reads through the existing `IDrawingBackend` interface and is not connected to D3D12 or compositor runtime.

## 4. Candidate Hookup Order

1. Add an internal segmented retained-frame owner beside the existing retained frame path.
2. Feed it only from an opt-in test or diagnostic harness, not from `RenderPipeline.Build`.
3. Preserve existing `RetainedRenderFrame.TryReadFrame` and current `DrawingBackendCompositor` behavior.
4. Add a segmented read facade only after full apply, invalidate, dispose, and no-mutation sentinels are stable.
5. Only after retained-frame ownership is proven, evaluate compositor per-segment execution behind a separate internal seam.

No step above enables StyleOnly fast-path. Every failure path continues to use existing full layout/full apply behavior until all gates have runtime evidence.

## 5. Sealed Lines

- Do not modify `RenderPipeline.Build`.
- Do not replace current `RetainedRenderFrame` behavior.
- Do not change `RetainedRenderFrame.TryReadFrame` contract.
- Do not connect `DrawingBackendCompositor` to segmented execution.
- Do not change `IDrawingBackend.Execute`.
- Do not touch D3D12.
- Do not add stable global handles or a resource cache.
- Do not change diagnostics output.