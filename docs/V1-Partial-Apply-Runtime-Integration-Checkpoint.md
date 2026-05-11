# v1 Partial Apply Runtime Integration Checkpoint

> Checkpoint only. This document does not enable partial apply, does not change `RenderPipeline.Build`, does not change `RetainedRenderFrame`, does not change `DrawingBackendCompositor`, does not change `IDrawingBackend.Execute`, and does not make any gate satisfied.

## 1. Current Evidence Layers

| Gate | Preflight evidence exists | Shadow runtime evidence exists | Production-off runtime evidence exists | Production runtime hookup still missing |
|------|---------------------------|--------------------------------|-----------------------------------------|------------------------------------------|
| Resource resolver ownership | `RetainedResourceSegmentTable`, `SegmentedRetainedFrameReader`, and segmented backend adapter prototype prove command-range resolver ownership. | `SegmentedRetainedFrameDiagnosticHarness` and `SegmentedRetainedFrameRuntimeOwner` feed real batches into `SegmentedRetainedFrameOwner`; `DrawingBackendCompositorShadowProbe` verifies segmented resolver ownership outside the compositor. | `RetainedRenderFrameSegmentOwnership` can sit beside the production `RetainedRenderFrame`, follow normal batches, and expose segmented reads plus owner-side hit target metadata while `TryReadFrame` remains unchanged. | Production retained frame does not expose segmented resource ownership through `TryReadFrame`. |
| Resource dispose policy | Segment table lifecycle tests prove retain/release rules. | Shadow owner, runtime owner seam, and diagnostic harness tests prove full apply, accepted partial rehearsal, explicit full fallback, rebuild, disabled mode, invalidate, dispose, and replaced-snapshot release rules. | Retained-frame segment ownership tests cover default-off no segmented owner plus enabled full apply, accepted partial, rejected partial before fallback, fallback full, empty batch, malformed dirty ranges, invalidate, disposal, repeated replacement, and multiple `FrameDrawingResources` retain/release exactly-once while `TryReadFrame` survives. | Production retained frame is still not the segmented render-source owner of multiple snapshots across command ranges. |
| Command range stability | Planner and segment reader tests reject unstable, invalid, overlapping, non-contiguous, and command-count-mismatched ranges. | Shadow owner reports `ShadowRejected` and leaves state unchanged when an applied plan cannot be accepted by the owner. | Retained-frame segment ownership runs planner to hit target projection to root patch to owner partial accept, reports failed rehearsal on projection or malformed dirty ranges, preserves owner state before fallback, then explicitly full-applies fallback state. | Production retained frame still does not replace cross-frame command ranges from segmented ownership. |
| Hit target metadata projection | `HitTargetMetadataProjector` proves action metadata projection from next root without next layout output. | Shadow harness consumes projector output while leaving hit-test runtime untouched. | Retained-frame segment ownership stores secondary hit target metadata and rehearses owner-side lookup for normal hit, clipped hit, no-hit, dirty action id projection, and projection-failure fallback while leaving production hit-test unchanged. | Hit-test runtime still consumes full layout output from the existing retained frame path. |
| Retained root update | `RetainedRootMetadataPatcher` proves dirty control metadata projection for `ActionId`, `IsHovered`, `IsPressed`, and `IsFocused`. | Opt-in shadow end-to-end tests prove accepted shadow partial rehearsal advances retained root metadata with command/resource segments. | Retained-frame segment ownership accepted partial rehearsal advances secondary retained root metadata and hit target metadata while the production `RenderPipeline` baseline remains unchanged. | `RenderPipeline` retained root baseline is not patched by any production partial path. |
| Fallback reporting | `RetainedPartialApplyPlanner` reports local `AppliedPartial`, `FallbackFull`, and `Rejected` reasons. | Local shadow result vocabulary reports `Disabled`, `ShadowAppliedPartial`, `ShadowFallbackFull`, and `ShadowRejected` for tests/internal diagnostics only. | Production-off feed reports `Disabled`, partial/full fallback kind, fallback-applied, and owner-state-preserved flags for tests/internal diagnostics only. | No runtime/compositor result propagation exists. |
| Compositor ownership | Dry-run sentinel tests keep planner/projector/root patcher/segment reader flow outside compositor mutation. | `DrawingBackendCompositorShadowProbe` executes shadow segmented reads through the adapter outside `DrawingBackendCompositor` and verifies execute order, resolver ownership, and hit-test no-change. | Default-off and enabled-secondary feed tests prove compositor counters, backend execute calls, dirty range propagation, hit-test results, and diagnostics text are unchanged; counter semantics and segment-local dirty-range handoff rules are rehearsed as internal/test-only design. | `DrawingBackendCompositor` still owns one retained frame and one resolver boundary. |
| Regression coverage | Focused preflight tests and existing layout/compositor tests cover no-change behavior. | Shadow owner, runtime owner seam, compositor shadow probe, diagnostic harness, result vocabulary, disabled no-change sentinel, opt-in end-to-end, fallback, rejection, and adapter tests cover the current shadow path. | Production-off tests cover retained-frame segment ownership default-off and enabled-secondary equivalence, accepted partial, rejected partial fallback, owner-side hit target lookup, projection failure fallback, multiple `FrameDrawingResources` lifecycle exact-once, repeated replacement, counter semantics, dirty-range handoff planning, four-piece atomicity, fallback, rebuild, empty batch, malformed dirty ranges, dispose, empty segments, throw path, clipped hit-test, dirty-range isolation, and diagnostics text no-change. | Runtime partial apply has no production render-source hookup to cover yet. |

`PartialApplyIntegrationGateChecklist.CanHookUpPartialApply` must remain false until production runtime hookup evidence exists for every gate. Production-off evidence is stronger than shadow evidence, but it still does not satisfy a gate.

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

Current shadow owner: `SegmentedRetainedFrameOwner`. It owns a command buffer, resource segment table, and retained root metadata, but it is not used by `RetainedRenderFrame` or `DrawingBackendCompositor`. The opt-in `SegmentedRetainedFrameDiagnosticHarness` wraps a `RenderPipeline`, calls the normal `Build` path first, then feeds the resulting batch/snapshot/root into `SegmentedRetainedFrameShadowHarness` only when `RenderPipelineShadowOptions.EnableSegmentedRetainedFrame` is true. Default/disabled mode does not allocate a shadow owner.

Current retained-frame-adjacent seam: `RetainedRenderFrameSegmentOwnership`. It is constructed only by opt-in tests or wrappers. Disabled mode has no segmented owner. Enabled mode holds a secondary `SegmentedRetainedFrameRuntimeOwner` beside a real `RetainedRenderFrame`, can full apply, rehearse accepted partial, reject malformed partial before fallback, explicitly fallback full, rebuild, invalidate, repeat replacement, or dispose, and keeps `RetainedRenderFrame.TryReadFrame` unchanged. The segmented owner owns secondary command segments, resource snapshots, retained root metadata, and hit target metadata, but it is never the render source.

Current production-off owner feed: `SegmentedRetainedFrameProductionOwnerFeed`. It wraps a `RenderPipeline`, calls normal `Build`, and only when `RenderPipelineProductionOwnerOptions.EnableSegmentedRetainedFrameRuntimeOwner` is true does it allocate `RetainedRenderFrameSegmentOwnership` against `pipeline.RetainedFrame`. Disabled mode reports `Disabled` and allocates no seam. Enabled mode delegates to the retained-frame-adjacent seam. Rendering still reads the existing production retained frame.

Accepted partial rehearsal remains test-only. The rehearsal sequence is:

```text
planner result
  -> hit target metadata projector
  -> retained root metadata patcher
  -> segmented frame TryAcceptPartial
  -> segmented reader
```

Success must advance command segments, resource snapshots, retained root metadata, and hit target metadata together. Failure must return false before command buffer, segment table, retained root, or hit target mutation; full fallback is then an explicit separate `ApplyFull` rehearsal.

## 3. Backend Execution Adapter Decision

Preferred adapter direction: per-segment execute.

| Option | Decision | Impact on `IDrawingBackend.Execute` |
|--------|----------|-------------------------------------|
| Per-segment execute | Preferred first adapter. Call the existing backend `Execute` once per contiguous resource segment with that segment's resolver. | No signature change; possible multiple `Execute` calls between one `BeginFrame` and `EndFrame`. |
| Composite resolver adapter | Not first. A resolver alone cannot know which command owns which resource scope unless command-range metadata is already available. | No signature change, but unsafe without segment ownership metadata. |
| Resource rebase | Postponed. Requires copying text/style resources into one namespace and rewriting commands. | No signature change after rebase, but much higher implementation risk. |
| Stable global handles | Explicitly out of scope. | Would change the resource model and backend cache identity rules. |

Current test-only prototype: `SegmentedBackendExecutionAdapter`. It executes segment reads through the existing `IDrawingBackend` interface and is not connected to D3D12 or compositor runtime. Adapter tests pin call order, empty segment behavior, execute-throw `BeginFrame`/`EndFrame` pairing, and confirm dirty command ranges are not pushed through `IDirtyRangeAware`.

Current dirty-range handoff rehearsal: `SegmentedBackendDirtyRangeHandoffPlanner` maps retained-frame dirty ranges into segment-local ranges for a future handoff option. Disabled mode remains the existing compositor behavior: one `SetDirtyCommandRanges` call with production retained-frame ranges before one full-frame `Execute`. The future handoff rule is per segment: intersect each production dirty range with the segment command range, then pass segment-local ranges only behind the handoff option before that segment's `Execute` call. The current adapter still does not call `IDirtyRangeAware`.

Current counter semantics rehearsal: `RetainedRenderFrameHandoffCounterSemantics` records that existing compositor counters keep their current meaning until a segmented owner is the approved production render source. Secondary owner partial/fallback/empty rehearsals require internal-only counters and do not affect `RenderCount`, `FullApplyCount`, `PartialApplyCount`, `EmptyFrameCount`, `LastDirtyCommandRanges`, or `LastPartialApplySucceeded`.

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

Production-off gate pre-review:

| Gate | What can be upgraded from production-off evidence | Remaining production blocker |
|------|-----------------------------------------------|-----------------------------|
| Resource resolver ownership | The retained-frame seam proves real batches can produce segmented reads as secondary state beside a real `RetainedRenderFrame`. | `RetainedRenderFrame.TryReadFrame` still exposes one resolver and remains the production render-source contract. |
| Resource dispose policy | The seam covers full, accepted partial, rejected partial before fallback, fallback, rebuild, empty batch, malformed dirty ranges, hit target projection failure, invalidate, repeated replacement, disposal, and multiple `FrameDrawingResources` exact-once retain/release as secondary ownership. | Production retained ownership still is not the segmented render source and does not own multiple snapshots across command ranges. |
| Command range stability | Projection and malformed dirty range failures now prove owner-state-preserved-before-fallback before explicit full fallback. | Production command replacement still does not reject and apply ranges inside the production retained-frame owner. |
| Hit target metadata projection | Production-off tests prove owner-side lookup matches production for normal hit, clipped hit, no-hit, dirty action id projection, and projection-failure fallback. | `DrawingBackendCompositor.TryGetActionIdAt` still reads production full-layout output and does not consume owner-side hit targets. |
| Retained root update | Accepted production-off partial rehearsal advances the secondary owner's retained root metadata and hit target metadata atomically. | `RenderPipeline` retained root baseline is still advanced only by the full build path. |
| Fallback reporting | Internal result vocabulary is available without diagnostics formatter changes. | Production callers and compositor still do not consume or report partial/fallback/rejected results. |
| Compositor ownership | Default-off and enabled-secondary sentinels prove compositor counters, backend calls, dirty ranges, hit-test, and diagnostics text are unchanged; counter semantics and segment-local dirty ranges are rehearsed internally. | `DrawingBackendCompositor` still owns one `RetainedRenderFrame`, never consumes segmented reads, and has no segmented execution option. |
| Regression coverage | Focused production-off coverage now includes accepted partial, rejected partial fallback, fallback, rebuild, empty batch, malformed ranges, exact-once resource lifecycle, repeated replacement, owner-side hit-test lookup, counter semantics, dirty-range handoff planning, probe hardening, and visible-output equivalence. | A true production render-source path still needs its own no-change suite before any gate can be satisfied. |

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

Implemented access point: `SegmentedRetainedFrameDiagnosticHarness` with `RenderPipelineShadowOptions`. Disabled mode returns local `Disabled` and does not allocate a shadow owner. Enabled mode returns local `ShadowFallbackFull`, `ShadowAppliedPartial`, or `ShadowRejected`; these results are not passed to the CLI formatter and are not consumed by compositor/backend rendering.

The switch must not change `--diagnose` or `--diagnose-*` text, must not become a global diagnostics channel, and must not affect compositor/backend/hit-test runtime. Rollback is disabling or removing the wrapper; the production path has no dependency on the shadow owner.

## 7. First Production Hookup Design Cut

The first real production hookup, when allowed, should be retained frame segment ownership. It should not be compositor segmented execution.

Minimal access point:

```text
RenderPipeline existing full Build path
  -> retained input snapshot + emitted batch
  -> opt-in retained segment owner candidate
  -> existing RetainedRenderFrame remains the render source
```

Default strategy:

- The production option is disabled by default and must be removable without changing default rendering.
- The first implementation can allocate a runtime-owned segmented owner only behind the option.
- Accepted partials may update the segmented owner state, but rendering still uses the existing retained frame until the owner has production ownership evidence for every gate.
- Full fallback must call the existing full apply behavior and rebuild the segmented owner from the full batch as secondary state.

Rollback:

- Disable the option and stop constructing the runtime-owned segmented owner.
- Remove the owner feed without touching `RetainedRenderFrame`, `DrawingBackendCompositor`, `IDrawingBackend`, D3D12, or diagnostics formatting.

Test gates before implementation:

- Default-off tests prove no allocation, no counter change, no hit-test change, and no CLI output change.
- Runtime owner tests prove full apply, accepted partial, rejected partial, fallback, rebuild, invalidate, dispose, and malformed range rejection.
- Compositor tests prove rendering still comes from `DrawingBackendCompositor`'s existing retained frame.
- Existing full test suite, `WindowLayoutPipelineTests`, diagnostics formatter tests, and D3D12 no-change boundary scans stay green.

Implemented production-off feed: `SegmentedRetainedFrameProductionOwnerFeed` follows the minimal access point above, but only as secondary state behind a default-off option. It does not modify `RenderPipeline.Build`, does not replace `RetainedRenderFrame`, and does not hook `DrawingBackendCompositor` into segmented execution.

Implemented retained-frame seam: `RetainedRenderFrameSegmentOwnership` is the first retained-frame-adjacent ownership seam. It is default-off, holds no segmented owner when disabled, and when enabled mirrors full apply, accepted partial rehearsal, fallback full, empty batch rebuild, projection failure fallback, malformed dirty range fallback, and disposal while preserving the existing `TryReadFrame` contract.

Remaining pre-handoff proof:

1. Keep `DrawingBackendCompositor` rendering from the existing `RetainedRenderFrame`; the segmented owner remains secondary evidence until every gate has production runtime proof.
2. Move multiple-snapshot lifecycle proof from secondary ownership into an actual production render-source owner before any hookup.
3. Move counter semantics and segment-local dirty-range rules from internal rehearsal into an explicit default-off handoff option before any compositor consumption.
4. Move owner-side hit-test lookup from seam rehearsal into the handoff option only after production no-change equivalence remains green.
5. Keep `RenderPipeline.Build`, `RetainedRenderFrame.TryReadFrame`, `DrawingBackendCompositor`, `IDrawingBackend`, D3D12, and diagnostics formatter sealed while handoff evidence is gathered.

Rollback for the next implementation is to disable the option and stop constructing the segmented owner. No rollback step may touch `DrawingBackendCompositor`, `IDrawingBackend`, D3D12, or diagnostics formatting.

## 8. Future Render-Source Handoff Checklist

This is design-only. No handoff is implemented by the current seam.

Before a segmented owner may become a render source, all of these must be true:

| Area | Required proof before handoff |
|------|-------------------------------|
| Default-off strategy | Disabled mode constructs no segmented owner and remains byte-for-byte equivalent in counters, backend calls, dirty ranges, hit-test, diagnostics text, and retained-frame read behavior. |
| Render source selection | Handoff is behind a separate default-off option; rollback switches back to `RetainedRenderFrame.TryReadFrame` without data migration. |
| Counter semantics | `RenderCount` remains non-empty frames rendered; `FullApplyCount` remains production render-source full apply; `PartialApplyCount` remains production render-source partial apply; `EmptyFrameCount` remains empty input batches; `LastDirtyCommandRanges` remains production retained-frame ranges in disabled mode; `LastPartialApplySucceeded` remains production partial success. Secondary owner rehearsal uses internal-only counters until promotion. |
| Hit-test ownership | Handoff must prove `TryGetActionIdAt` uses the owner-side hit target snapshot only after accepted partial metadata projection is proven; clipped targets and no-hit cases must remain unchanged. |
| Backend dirty ranges | `IDirtyRangeAware.SetDirtyCommandRanges` receives the same production retained-frame ranges as today in disabled mode. Under a future handoff option, retained-frame dirty ranges are intersected with each segment and converted to segment-local ranges before that segment's `Execute`. |
| Backend execution | Existing `IDrawingBackend.Execute` signature stays unchanged; any per-segment execution adapter remains behind the handoff option and must preserve `BeginFrame`/`EndFrame` pairing on throws. |
| Resource lifetime | Multiple retained resource snapshots must retain/release exactly once across accepted partial, rejected partial, fallback full, empty frame, invalidate, dispose, and repeated replacement. |
| Diagnostics | CLI diagnostics text stays unchanged; handoff results remain internal until separately approved. |

Rollback plan:

1. Disable the handoff option.
2. Render from the existing `RetainedRenderFrame` path.
3. Keep or remove the secondary segmented owner without changing `RenderPipeline.Build`, `DrawingBackendCompositor`, `IDrawingBackend`, D3D12, or diagnostics formatting.

This design cut still does not implement production hookup and does not enable StyleOnly fast-path.

## 9. Sealed Lines

- Do not modify `RenderPipeline.Build`.
- Do not replace current `RetainedRenderFrame` behavior.
- Do not change `RetainedRenderFrame.TryReadFrame` contract.
- Do not connect `DrawingBackendCompositor` to segmented execution.
- Do not change `IDrawingBackend.Execute`.
- Do not touch D3D12.
- Do not add stable global handles or a resource cache.
- Do not change diagnostics output.