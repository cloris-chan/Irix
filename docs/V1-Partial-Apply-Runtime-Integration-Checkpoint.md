# v1 Partial Apply Runtime Integration Checkpoint

> Checkpoint only. This document does not enable partial apply, does not change `RenderPipeline.Build`, does not change `RetainedRenderFrame`, does not change `DrawingBackendCompositor`, does not change `IDrawingBackend.Execute`, and does not make any gate satisfied.

## 1. Current Evidence Layers

| Gate | Preflight evidence exists | Shadow runtime evidence exists | Production runtime hookup still missing |
|------|---------------------------|--------------------------------|------------------------------------------|
| Resource resolver ownership | `RetainedResourceSegmentTable`, `SegmentedRetainedFrameReader`, and segmented backend adapter prototype prove command-range resolver ownership. | `SegmentedRetainedFrameOwner` can be fed real `RenderFrameBatch` data and expose segmented resolver reads. | Production retained frame does not expose segmented resource ownership. |
| Resource dispose policy | Segment table lifecycle tests prove retain/release rules. | `SegmentedRetainedFrameOwner` tests prove full apply, accepted partial rehearsal, explicit full fallback, invalidate, dispose, and replaced-snapshot release rules. | Production retained frame does not own multiple retained snapshots. |
| Command range stability | Planner and segment reader tests reject unstable, invalid, overlapping, non-contiguous, and command-count-mismatched ranges. | Shadow owner rejection tests prove failed partial rehearsal returns before command, segment, or retained-root mutation. | Production runtime still does not replace cross-frame command ranges. |
| Hit target metadata projection | `HitTargetMetadataProjector` proves action metadata projection from next root without next layout output. | Shadow harness consumes projector output while leaving hit-test runtime untouched. | Hit-test runtime still consumes full layout output from the existing path. |
| Retained root update | `RetainedRootMetadataPatcher` proves dirty control metadata projection for `ActionId`, `IsHovered`, `IsPressed`, and `IsFocused`. | Accepted shadow partial rehearsal advances retained root metadata with command/resource segments. | `RenderPipeline` retained root baseline is not patched by any partial path. |
| Fallback reporting | `RetainedPartialApplyPlanner` reports local `AppliedPartial`, `FallbackFull`, and `Rejected` reasons. | Shadow harness returns local rehearsal results only to tests. | No runtime/compositor result propagation exists. |
| Compositor ownership | Dry-run sentinel tests keep planner/projector/root patcher/segment reader flow outside compositor mutation. | `SegmentedBackendExecutionAdapter` can execute shadow owner reads without compositor involvement. | `DrawingBackendCompositor` still owns one retained frame and one resolver boundary. |
| Regression coverage | Focused preflight tests and existing layout/compositor tests cover no-change behavior. | Shadow owner/harness/adapter tests cover real-batch feed, fallback rehearsal, and per-segment execution. | Runtime partial apply has no production hookup to cover yet. |

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

Current shadow owner: `SegmentedRetainedFrameOwner`. It owns a command buffer, resource segment table, and retained root metadata, but it is not used by `RetainedRenderFrame` or `DrawingBackendCompositor`. The opt-in `SegmentedRetainedFrameShadowHarness` can be fed real `RenderPipeline.Build` output plus an explicit retained root; tests verify the existing pipeline retained frame state is unchanged.

Accepted partial rehearsal remains test-only. The rehearsal sequence is:

```text
planner result
  -> hit target projector
  -> retained root metadata patcher
  -> segmented frame TryAcceptPartial
  -> segmented reader
```

Success must advance command segments, resource snapshots, and retained root metadata together. Failure must return false before command buffer, segment table, or retained root mutation; full fallback is then an explicit separate `ApplyFull` rehearsal.

## 3. Backend Execution Adapter Decision

Preferred adapter direction: per-segment execute.

| Option | Decision | Impact on `IDrawingBackend.Execute` |
|--------|----------|-------------------------------------|
| Per-segment execute | Preferred first adapter. Call the existing backend `Execute` once per contiguous resource segment with that segment's resolver. | No signature change; possible multiple `Execute` calls between one `BeginFrame` and `EndFrame`. |
| Composite resolver adapter | Not first. A resolver alone cannot know which command owns which resource scope unless command-range metadata is already available. | No signature change, but unsafe without segment ownership metadata. |
| Resource rebase | Postponed. Requires copying text/style resources into one namespace and rewriting commands. | No signature change after rebase, but much higher implementation risk. |
| Stable global handles | Explicitly out of scope. | Would change the resource model and backend cache identity rules. |

Current test-only prototype: `SegmentedBackendExecutionAdapter`. It executes segment reads through the existing `IDrawingBackend` interface and is not connected to D3D12 or compositor runtime. Adapter tests pin call order, empty segment behavior, execute-throw `BeginFrame`/`EndFrame` pairing, and confirm dirty command ranges are not pushed through `IDirtyRangeAware`.

## 4. Runtime Evidence Promotion Checklist

Preflight evidence can be promoted only when each condition below is met in runtime-owned code while the no-change regression suite remains green.

| Gate | Promotion condition |
|------|---------------------|
| Resource resolver ownership | Runtime retained-frame owner exposes segmented reads from real retained state without changing the existing `TryReadFrame` contract. |
| Resource dispose policy | Production ownership paths cover accepted partial, rejected partial, full fallback, empty frame, invalidate, and dispose with multiple `FrameDrawingResources` snapshots. |
| Command range stability | Runtime rejects mismatched command counts and malformed dirty ranges before any command buffer or segment table mutation. |
| Hit target metadata projection | Runtime partial path consumes retained geometry plus next-root action metadata without reading next layout output. |
| Retained root update | Accepted runtime partials atomically advance retained root metadata with command segments and hit targets; failed partials leave the old baseline intact. |
| Fallback reporting | Runtime callers can observe local `AppliedPartial` / `FallbackFull` / `Rejected` vocabulary without changing CLI diagnostics output. |
| Compositor ownership | Compositor integration is explicitly opt-in and proves segmented execution does not change existing full-frame behavior or hit-test lookup. |
| Regression coverage | Runtime-seam tests and existing no-change suites pass together on every supported validation path. |

## 5. Candidate Hookup Order

1. Add an internal segmented retained-frame owner beside the existing retained frame path.
2. Feed it only from an opt-in test or diagnostic harness, not from `RenderPipeline.Build`.
3. Preserve existing `RetainedRenderFrame.TryReadFrame` and current `DrawingBackendCompositor` behavior.
4. Add a segmented read facade only after full apply, invalidate, dispose, and no-mutation sentinels are stable.
5. Only after retained-frame ownership is proven, evaluate compositor per-segment execution behind a separate internal seam.

No step above enables StyleOnly fast-path. Every failure path continues to use existing full layout/full apply behavior until all gates have runtime evidence.

## 6. Next Controlled Access Point Design

The next possible access point should be diagnostics-only and explicitly disabled by default. The proposed shape is an internal option/switch owned by tests or a diagnostic host, for example `EnableShadowSegmentedRetainedFrame`, that is passed when constructing a diagnostic `RenderPipeline` wrapper or harness. Default `RenderPipeline` construction must not allocate or update a shadow owner.

If the switch is enabled in a diagnostic-only path, the sequence should remain after the existing `RenderPipeline.Build` has completed and after the normal `RetainedRenderFrame` update has already happened:

```text
RenderPipeline.Build existing path
  -> existing RetainedRenderFrame update
  -> optional shadow owner feed/rehearsal
  -> diagnostic-only shadow result object
```

The switch must not change `--diagnose` or `--diagnose-*` text, must not become a global diagnostics channel, and must not affect compositor/backend/hit-test runtime. Rollback is disabling or removing the switch and deleting the wrapper; the production path should have no dependency on the shadow owner.

## 7. Sealed Lines

- Do not modify `RenderPipeline.Build`.
- Do not replace current `RetainedRenderFrame` behavior.
- Do not change `RetainedRenderFrame.TryReadFrame` contract.
- Do not connect `DrawingBackendCompositor` to segmented execution.
- Do not change `IDrawingBackend.Execute`.
- Do not touch D3D12.
- Do not add stable global handles or a resource cache.
- Do not change diagnostics output.