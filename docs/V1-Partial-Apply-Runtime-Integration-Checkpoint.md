# v1 Partial Apply Runtime Integration Checkpoint

> V1 core checkpoint. The selected segmented render-source path is implemented only behind internal/default-off options. This does not change `RenderPipeline.Build`, does not change `RetainedRenderFrame.TryReadFrame`, does not change `IDrawingBackend.Execute`, does not touch D3D12, and does not change CLI diagnostics text.

## 1. Current State

The V1 core partial-apply runtime integration is gate-driven complete:

- `DrawingBackendCompositor` has an internal/default-off selected render-source path.
- A fresh accepted `RetainedRenderFrameSegmentOwnership.RuntimeOwner.ReadSegments()` can be selected for one frame and executed through the real backend.
- Disabled, missing owner, stale owner, rejected owner, fallback, empty reads, malformed segment coverage, malformed dirty ranges, and backend throw paths fall back to the existing retained-frame render path or preserve the pre-commit state.
- `PartialApplyIntegrationGateChecklist.CanHookUpPartialApply` is `true` because every required V1 core gate now has production runtime evidence and `Satisfied=true`.
- The default production path remains unchanged because all new selected-source behavior is internal/default-off.

## 2. Gate Status

| Gate | V1 core status | Production runtime evidence |
|------|----------------|-----------------------------|
| Resource resolver ownership | Satisfied | Fresh accepted segmented reads execute as the selected source through the real compositor backend; disabled/missing/stale/rejected/fallback paths use `RetainedRenderFrame.TryReadFrame`. |
| Resource dispose policy | Satisfied | Runtime owner snapshots retain/release exactly once across accepted partials, reported fallback without owner mutation, empty frame, repeated replacement, backend throw, and dispose. |
| Command range stability | Satisfied | Selected execution is guarded by owner freshness, batch resources, command owner, command count, frame id, strict dirty ranges, and contiguous segment coverage. Malformed coverage and overlapping/out-of-range dirty ranges are rejected before candidate execution. |
| Hit target metadata projection | Satisfied | `DrawingBackendCompositor.TryGetActionIdAt` uses owner-side hit targets only after selected segmented execution succeeds. Projection failure, fallback, stale owner, rejected owner, and backend throw retain the previous/production hit targets. |
| Retained root update | Satisfied | Accepted partials atomically advance command segments, resource snapshots, retained root metadata, and hit target metadata; failed projection/malformed/fallback cases report fallback while preserving the previous owner state. |
| Fallback reporting | Satisfied | `LastHandoffResult` exposes internal `Disabled`, `MissingOwner`, `Executed`, `FallbackFull`, and `Rejected` kinds plus internal reasons including stale owner, malformed segment coverage, dirty range mismatch, and backend throw before commit. CLI diagnostics remain unchanged. |
| Compositor ownership | Satisfied | The compositor owns selected-source choice behind an opt-in option, routes segment-local dirty ranges, counts a selected partial as one frame, and commits owner hit targets only after successful selected execution. |
| Regression coverage | Satisfied | Focused tests cover default-off equivalence, enabled selected execution, fallback, stale/missing/rejected owners, backend throw, malformed guards, dirty-range routing, hit-test ownership, counters, diagnostics unchanged, and the style-only pre-switch. Full `dotnet test` is green. |

## 3. Runtime Shape

The selected path is still deliberately narrow:

```text
RenderPipeline.Build existing path
  -> RetainedRenderFrameSegmentOwnership.Update(...)
  -> DrawingBackendCompositor.RenderAsync(...)
  -> if internal option enabled and owner result is fresh accepted partial:
       execute RuntimeOwner.ReadSegments() through per-segment backend calls
     else:
       execute existing RetainedRenderFrame.TryReadFrame path
```

The selected path does not migrate state into `RetainedRenderFrame` and does not alter `TryReadFrame`. The retained frame is still updated before selection so rollback remains local to the compositor option.

## 4. Guard Rules

Selected segmented execution is allowed only when all of these are true:

- `DrawingBackendCompositorHandoffOptions.EnableSegmentedRenderSourceCandidate` is enabled.
- `RetainedRenderFrameSegmentOwnership.RuntimeOwner` exists.
- `LastResult` is fresh for the same batch resources, command owner, command count, and `FrameId`.
- `LastResult.Kind == ShadowAppliedPartial` and the plan was not rejected.
- `LastDirtyCommandRanges` are strict, non-overlapping, positive, and inside the command count.
- `RuntimeOwner.ReadSegments()` succeeds.
- Segment reads and resource segments cover `[0, commandCount)` contiguously with no gaps or overlaps.
- Runtime reads still match the reads stamped in `LastResult`.

Any failure returns to the existing retained-frame path and leaves selected-source counters and hit targets uncommitted.

## 5. Commit And Fallback Semantics

Accepted selected partial:

- Executes one backend frame with one `BeginFrame`, one `EndFrame`, and one `Execute` per retained segment.
- Sends segment-local dirty ranges to `IDirtyRangeAware` before each segment execute.
- Increments compositor counters once for the frame, not once per segment.
- Sets `PartialApplyCount` only for a selected accepted segmented partial.
- Commits owner-side hit targets after backend execution succeeds.

Failed or non-selected frame:

- Uses the existing retained-frame backend path.
- Keeps CLI diagnostics text unchanged.
- Does not keep owner-side hit targets after fallback.
- Does not retry an already-started selected backend frame after a backend throw.
- Preserves previous owner state for projection failure, malformed dirty range, and partial accept failure; later full rebuilds can refresh the owner explicitly.

## 6. StyleOnly Pre-Switch

`StyleOnlyFastPathOptions` is an internal/default-off pre-switch. Disabled mode maps to disabled production-owner and handoff options. Enabled mode maps to:

- `RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled`
- `DrawingBackendCompositorHandoffOptions.Enabled`

It does not skip layout and does not change `RenderPipeline.Build`. It only enables the already guarded selected segmented render-source path when retained snapshot, planner, root patch, hit target projection, resource ownership, owner freshness, dirty ranges, and segment coverage all pass.

## 7. Postponed Beyond V1 Core

These remain explicitly out of scope for this checkpoint:

- Default-on partial apply.
- Public API changes.
- `IDrawingBackend.Execute` changes.
- D3D12 segmented resource ownership.
- Typed ids.
- Scroll extraction / settings provider runtime hookup.
- Translator promotion.
- Unified diagnostics channel.
- Resource cache or stable global handles.
- GA hardening, platform matrix, and device-specific D3D12 validation.

## 8. Validation

Current validation command:

```powershell
dotnet test --no-restore
```

Current result: all tests passing, including rendering/compositor/partial apply/diagnostics/window pipeline coverage. D3D12 device-level segmented execution remains not run because this checkpoint intentionally does not connect selected segmented ownership to D3D12-specific behavior.
