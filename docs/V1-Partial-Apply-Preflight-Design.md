# v1 Partial Apply Preflight Design

> Planner-only design for the work required before any real partial apply hookup. This document does not change `RenderPipeline.Build`, `RetainedRenderFrame`, `DrawingBackendCompositor`, `FrameDrawingResources`, input routing, diagnostics output, or backend rendering behavior.

## 1. Decision Summary

| Area | Decision |
|------|----------|
| Cross-frame resources | Prefer resource snapshot / composite resolver over stable global handles. |
| Current implementation scope | Keep `RetainedPartialApplyPlanner` data-only and side-effect-free; keep resource segments, metadata projection, retained-root metadata patching, and dry-run flow as internal/test-only scaffold. |
| Resource cache | Do not add one for v1 preflight. |
| Backend contract | Do not touch D3D12 or `IDrawingBackend` in this line. |
| Hit targets | Preserve retained geometry; reproject metadata from next `VirtualNode` through a local projector prototype only. |
| Runtime hookup | Postponed until every gate in section 6 is satisfied; first seam decision is recorded in [V1-Partial-Apply-Runtime-Integration-Checkpoint.md](V1-Partial-Apply-Runtime-Integration-Checkpoint.md). |

Stable global text/style handles are not the next step. They would require a durable resource table, recycling/invalidation rules, backend cache identity, and memory pressure policy. The smaller next design is a retained resource snapshot plus a resolver that can serve old retained commands and new replacement commands during one merged retained frame.

## 2. Resource Snapshot / Composite Resolver

The cross-frame blocker is not only lifetime. The current backend shape is:

```csharp
void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
```

Each `DrawCommand` carries `TextSlice` and `ResourceHandle`, but no resource-scope id. A naive composite resolver that simply holds two `FrameDrawingResources` instances cannot know whether a given command should resolve against the retained old resources or the replacement new resources. Therefore the future design needs one of these local mechanisms before command ranges can be replaced across frames:

| Option | Shape | Decision |
|--------|-------|----------|
| Command-range resolver metadata | Retained command buffer stores resolver ownership per contiguous command range. Backend reads commands through a retained frame facade that supplies the matching resolver for each range. | Preferred first implementation candidate. Keeps current resource handles local to their owning frame snapshot. |
| Resource rebase | Replacement commands are copied into a new retained resource snapshot, remapping `TextSlice` and `ResourceHandle` values to one resolver namespace. | Viable later, but requires copying text/style data and command rewriting. |
| Stable global handles | Commands use durable ids independent of frame resources. | Postponed. Larger resource table/cache problem. |

Preferred direction: resource snapshot per retained frame plus range-indexed resolver metadata. The retained command buffer remains the owner of command order. A future retained frame would also own a compact list of resource segments, for example `(commandStart, commandCount, resolverSnapshot)`. This keeps old commands resolving through the old snapshot and dirty replacement commands resolving through the new snapshot.

Current scaffold:

| Type | Scope | Purpose |
|------|-------|---------|
| `RetainedResourceSnapshot` | Internal / preflight tests | Captures a resolver plus `FrameId` generation and optional retain/release probes. |
| `RetainedResourceSegment` | Internal / preflight tests | Describes `(commandStart, commandCount, resolverSnapshot)` ownership. |
| `RetainedResourceSegmentTable` | Internal / preflight tests | Models full apply, partial accept, invalidate, dispose, and per-command resolver lookup. |
| `SegmentedRetainedFrameReader` | Internal / preflight tests | Reads retained commands by resource segment and returns the resolver owned by each segment; rejects malformed coverage. |
| `SegmentedFrameRead` | Internal / preflight tests | Carries a command slice copy plus its segment resolver for assertion/prototyping. |
| `SegmentedRetainedFrameOwner` | Internal / preflight tests | Shadow retained-frame owner that owns command buffer, resource segment table, and retained root metadata together; opt-in harness can consume real `RenderFrameBatch` inputs. |
| `SegmentedRetainedFrameShadowHarness` | Internal / tests only | Feeds real `RenderPipeline.Build` batches into the shadow owner after the normal path, simulates accepted partial and explicit fallback flow, and proves no pollution of the existing retained frame. |
| `SegmentedRetainedFrameDiagnosticHarness` | Internal / tests and diagnostics only | Wraps a `RenderPipeline`; disabled mode returns `Disabled` without allocating a shadow owner, enabled mode feeds batch/snapshot/root to the shadow harness after normal `Build`. |
| `SegmentedRetainedFrameRuntimeOwner` | Internal / runtime seam draft | Long-lived owner seam that can hold the segmented owner, secondary hit target metadata, accept partial rehearsal, explicit fallback, rebuild, read segments, and dispose without replacing `RetainedRenderFrame`. |
| `RetainedRenderFrameSegmentOwnership` | Internal / production-off tests only | Default-off retained-frame-adjacent seam; disabled mode has no segmented owner, enabled mode mirrors full apply, accepted partial rehearsal, rejected partial before fallback, fallback, empty batch, invalidate, repeated replacement, owner-side hit lookup, and dispose beside a real `RetainedRenderFrame` while preserving `TryReadFrame`. |
| `SegmentedRetainedFrameProductionOwnerFeed` | Internal / production-off tests only | Default-off wrapper around normal `RenderPipeline.Build`; disabled mode allocates no retained-frame segment ownership seam, enabled mode delegates to `RetainedRenderFrameSegmentOwnership` and never makes it the render source. |
| `RetainedRenderFrameHandoffCounterSemantics` | Internal / tests only | Records future handoff meaning for existing compositor counters and internal-only secondary owner counters. |
| `SegmentedBackendDirtyRangeHandoffPlanner` | Internal / tests only | Rehearses future segment-local dirty range mapping without changing `IDrawingBackend` or `IDirtyRangeAware` behavior. |
| `RetainedRenderFrameHandoffHarness` | Internal / tests only | Default-off candidate render-source harness; missing-owner mode no-ops, enabled mode executes `RuntimeOwner.ReadSegments()` through the per-segment adapter, uses owner-side clipped/no-hit targets, segment-local dirty ranges, and internal handoff counters without mutating production compositor state. |
| `DrawingBackendCompositor` handoff selector | Internal / tests only | Default-off constructor overload on the main compositor; disabled mode does not allocate a candidate harness, enabled mode renders the existing retained-frame path and invokes the handoff harness only for a fresh accepted partial owner, while stale/missing/rejected/fallback/empty-read cases stay on the retained-frame path and report `Disabled` / `MissingOwner` / `Executed` / `FallbackFull` / `Rejected` internally. |
| `DrawingBackendCompositorShadowProbe` | Internal / tests only | Runs `SegmentedBackendExecutionAdapter` outside `DrawingBackendCompositor`, records execute order and resolver ownership, and verifies compositor hit-test no-change. |

This scaffold is still default-off and internal. It does not change `RetainedRenderFrame.TryReadFrame`, `IDrawingBackend.Execute`, or D3D12, and the main compositor still uses the existing retained-frame render source unless the internal selector is explicitly constructed for tests.

### Ownership

| Owner | Responsibility |
|-------|----------------|
| Existing retained frame snapshot | Retain resources for commands that survive a partial update. |
| Replacement frame snapshot | Retain resources for newly recorded dirty command ranges that are accepted into the retained frame. |
| Full fallback path | Release all old retained snapshots, apply the full new batch, retain the new snapshot. |
| Rejected planner path | Retain/release nothing and mutate nothing. |
| Empty frame / invalidate | Release all retained snapshots and clear resolver metadata. |

The current `FrameDrawingResources` retain/release model is still the primitive. This design does not require changing `FrameDrawingResources`; it requires a future retained-frame owner to retain multiple snapshots at once and dispose them when their command ranges are no longer present.

### Dispose Rules

| Event | Required behavior |
|-------|-------------------|
| Accepted partial range | Retained frame takes ownership of replacement resources before the source batch can return them to the pool. |
| Replaced old range | Release old resource snapshot only when no remaining command segment references it. |
| Full apply fallback | Release every previous snapshot before retaining the full replacement snapshot. |
| Planner fallback/reject | Leave all ownership unchanged. |
| Compositor dispose | Release every retained snapshot exactly once. |

### Resolver Lookup

The lookup boundary must preserve zero ambiguity:

1. Merge command ranges only after `StyleOnlyPatchEligibility.TryMapStableCommandRanges` succeeds.
2. Attach accepted replacement ranges to their replacement resource snapshot.
3. Keep unchanged command ranges attached to their old resource snapshot.
4. Present backend execution through a boundary that never resolves a command against the wrong snapshot.

If the backend API still accepts one `IFrameResourceResolver`, the retained frame needs an adapter that either executes per segment or remaps resources into one namespace before calling the backend. Until that adapter exists, cross-frame partial apply must continue to return `ResourceOwnershipMismatch` or full fallback.

Current segmented reader prototype proves the facade shape without touching `IDrawingBackend.Execute`: old retained command segments read through the old resolver, while accepted replacement dirty ranges read through the replacement resolver. It copies command slices for tests only; it is not a backend hot path.

Current shadow owner proves the first runtime checkpoint target without replacing the existing retained frame: command buffer + resource segment table + retained root metadata can be owned together, full-applied, partially rehearsed, explicitly full-fallback-applied, invalidated, and disposed while retaining/releasing multiple snapshots exactly once. Current retained-frame segment ownership extends this proof beside a real `RetainedRenderFrame` with multiple `FrameDrawingResources` snapshots, rejected partial before fallback, repeated replacement, invalidate, dispose, and retained-frame no-change checks.

### Test Strategy

| Test area | Required proof |
|-----------|----------------|
| Planner fallback | `ResourceOwnershipMismatch` remains side-effect-free when old/new resources differ. |
| Ownership | Accepted partial retains old and new snapshots; replaced snapshots release only after last command segment disappears; repeated partial accepts are idempotent; failed partial returns false before mutation and explicit full fallback replaces ownership; retained-frame seam tests prove multiple `FrameDrawingResources` snapshots are not leaked, replayed, or released early. |
| Resolver correctness | Old text resolves from old resources and replacement text resolves from new resources in one retained frame. |
| Pool safety | Re-rented `FrameDrawingResources` with a new `FrameId` is never treated as the old snapshot. |
| Full fallback | Full apply releases all previous snapshots and exposes only the new full-frame resolver. |
| Range edges | Multiple dirty ranges, adjacent merge, invalid ranges, empty segment table, out-of-buffer segments, gaps, overlaps, command-count mismatch, and disposed table/snapshot behavior are explicit. |
| Adapter behavior | Per-segment execute preserves order, pairs `BeginFrame`/`EndFrame` on execute throws, handles empty segments, and does not leak dirty ranges; handoff harness proves segment-local dirty ranges behind a separate option. |
| Backend neutrality | Existing compositor/backend behavior is unchanged until an explicit integration step lands. |

## 3. Hit Target Metadata Projection

A future partial path must not depend on next layout output. It can reuse retained geometry only when dirty classification proves the update is style-only and the viewport is unchanged. Metadata such as action id must be reprojected from the next `VirtualNode` / control metadata.

| Data | Source | Rule |
|------|--------|------|
| Bounds | Retained layout snapshot | Reuse only for stable style-only dirty ranges. |
| Clip bounds | Retained layout snapshot | Reuse only when viewport, scroll, and clip-affecting layout inputs are unchanged. |
| Action id | Next `VirtualNode` attributes / control metadata | Reproject for dirty hit targets. |
| Hit target count/order | Retained layout tree plus next node mapping | Must remain stable. |
| Hover/pressed/focused visual state | Next `VirtualNode` attributes / control metadata | Reproject into replacement draw commands, not hit-test geometry. |

Future input shape for a projector:

```text
Retained layout tree + retained hit targets + retained root + next root + dirty DFS indices
```

The projector should locate the matching next node by retained DFS/key path, read `ActionId` from the next node, and patch metadata only for targets whose retained geometry is unchanged. It must not run layout, mutate input routing, or infer geometry from visual state.

Current scaffold:

| Type | Scope | Purpose |
|------|-------|---------|
| `HitTargetMetadataProjector` | Internal / preflight tests | Reprojects `ActionId` from next `VirtualNode` for stable retained hit target order. |
| `HitTargetMetadataProjection` | Internal / preflight tests | Reports success or local `HitTargetPatchFailed` fallback. |

This prototype does not call layout, does not change hit-test runtime, and does not infer bounds/clip geometry.

Current projector tests cover dirty DFS that is not an action node, non-dirty action id changes, key/path mismatch, multiple buttons, and nested trees. Success still only means metadata can be reprojected; geometry remains retained and must already be proven stable.

Fallback conditions:

| Condition | Reason |
|-----------|--------|
| Missing retained snapshot or retained root | `MissingRetainedSnapshot` |
| Viewport changed | `ViewportChanged` |
| Dirty classification is not style-only | `NotStyleOnly` |
| Dirty DFS index cannot map to retained layout element range | `UnstableCommandRange` |
| Next node cannot be found by stable path/key | `HitTargetPatchFailed` |
| Hit target count/order changes | `HitTargetPatchFailed` |
| Retained bounds or clip would change | `HitTargetPatchFailed` |
| `ActionId` metadata is ambiguous or missing for an existing target | `HitTargetPatchFailed` |

## 4. Retained Root Update Preflight

Accepted partial apply must advance retained root/control metadata or the next diff will compare against stale input. The update rule is intentionally narrower than generic `VirtualNode` replacement:

| Metadata | Accepted partial update | Full fallback trigger |
|----------|-------------------------|-----------------------|
| `ActionId` | May update on dirty action nodes after hit target metadata projection succeeds. | Missing/ambiguous action metadata, changed hit target count/order, or non-dirty action id drift. |
| `IsHovered` / `IsPressed` / `IsFocused` | May update on dirty controls when dirty classification is style-only. | Any unknown visual state attribute or mismatch between retained and next node path/key. |
| Text label/content | Do not update in partial path. | Any text/content change remains text-size-affecting unless a future measurement proof exists. |
| Layout-affecting attributes | Do not update in partial path. | Width/height/spacing/scroll/clip-affecting changes require full layout. |
| Tree shape / keys / kinds | Do not update in partial path. | Child count/order, key mismatch, kind mismatch, add/remove/move require full fallback. |

Future retained root update should run after command range planning, resource ownership, and hit target metadata projection all succeed. It should apply only the accepted dirty metadata patch to the retained root snapshot, then expose that root as the next retained baseline. If any dirty node cannot be updated from next-node metadata alone, the path must use the existing full layout/full apply behavior.

Current scaffold:

| Type | Scope | Purpose |
|------|-------|---------|
| `RetainedRootMetadataPatcher` | Internal / preflight tests | Projects dirty `ActionId`, `IsHovered`, `IsPressed`, and `IsFocused` metadata from next root into a retained-root copy. |
| `RetainedRootMetadataPatch` | Internal / preflight tests | Reports success plus patched root, or local fallback reason. |

The prototype requires stable kind/key/child path, style-only dirty classifications, unchanged content, unchanged non-dirty metadata, and no layout/tree/text drift. It is not wired to `RenderPipeline.Build` and does not change the retained diff baseline.

## 5. Planner-Only Boundary

`RetainedPartialApplyPlanner` is allowed to read `RenderPipeline.LastRetainedInputSnapshot`, current viewport, retained resources, and replacement resources. It is not allowed to:

- Call `RenderPipeline.Build`.
- Mutate `RenderPipeline.RetainedFrame`.
- Mutate `DrawingBackendCompositor.RetainedFrame`.
- Replace command ranges.
- Retain or release resources.
- Write diagnostics output.
- Change hit-test runtime behavior.

Regression tests should keep every planner result reason covered: `None` for a data-only eligible plan, plus `NotStyleOnly`, `ViewportChanged`, `MissingRetainedSnapshot`, `UnstableCommandRange`, `HitTargetPatchFailed`, and `ResourceOwnershipMismatch`.

The runtime integration checkpoint currently selects retained frame segment ownership as the first seam. Compositor segmented execution is intentionally second because it requires a retained frame owner that can already expose correct command-range resolver ownership.

## 6. Integration Gates

No partial apply hookup should land until every gate below is satisfied. Evidence is intentionally split so preflight proof cannot be mistaken for runtime readiness.

| Gate | Preflight evidence | Shadow runtime evidence | Production-off runtime evidence | Production runtime missing | No-change regression evidence | Still blocking |
|------|--------------------|-------------------------|---------------------------------|----------------------------|-------------------------------|----------------|
| Resource resolver ownership | Segmented reader and per-segment adapter prove old/new resolver ownership by command segment. | Diagnostic harness and runtime owner seam feed real batches into shadow owner; compositor shadow probe verifies segmented resolver ownership outside compositor. | `RetainedRenderFrameSegmentOwnership` plus the default-off `DrawingBackendCompositor` handoff selector can execute fresh accepted segmented reads as an internal candidate while `TryReadFrame` remains unchanged. | Production retained frame still exposes a single resolver boundary. | Backend contract and D3D12 execution stay unchanged. | Runtime retained frame must expose segmented resolver reads before hookup. |
| Resource dispose policy | Segment table covers ownership lifecycle rules. | Shadow owner, runtime owner seam, and diagnostic harness cover partial accept, explicit full fallback, rebuild, disabled mode, invalidate, dispose, replaced old ranges, and malformed reads. | Retained-frame segment ownership covers disabled no-owner mode plus enabled full apply, accepted partial, rejected partial before fallback, fallback full, rebuild, empty batch, malformed dirty ranges, hit target projection failure, invalidate, repeated replacement, disposal, multiple `FrameDrawingResources` exact-once lifecycle, and `TryReadFrame` survival. | Production retained frame is still not the segmented render-source owner of multiple retained snapshots. | Existing retained frame resource ownership tests keep same-frame behavior sealed. | Production render-source ownership must release retained snapshots exactly once across every path. |
| Command range stability | Planner, segment table, and segmented reader cover unstable, invalid, overlapping, and non-contiguous ranges. | Shadow owner reports `ShadowRejected` without command, segment, or retained-root mutation when an applied plan cannot be accepted. | Retained-frame segment ownership runs planner to hit target projection to root patch to owner accept, confirms projection and malformed dirty range failures preserve owner state before fallback, then explicitly full-applies fallback. | Production runtime still does not replace cross-frame command ranges. | Compositor no-change tests keep current full/guarded partial behavior sealed. | Runtime command replacement must require stable contiguous dirty command ranges. |
| Hit target metadata projection | Projector covers action metadata projection, dirty DFS mismatch, non-dirty drift, key/path mismatch, and nested controls. | Shadow harness consumes projector output while hit-test runtime remains unchanged. | Retained-frame segment ownership stores secondary hit targets and rehearses owner-side lookup for normal hit, clipped hit, no-hit, dirty action id projection, and projection-failure fallback without altering production hit-test runtime. | Hit-test runtime still consumes full layout output only. | Hit-test behavior tests keep retained geometry and compositor lookup unchanged. | Runtime projector must reproject action metadata without next layout output. |
| Retained root update | Root metadata patcher covers dirty control metadata projection plus non-dirty drift, key/path, text, layout, and tree fallback. | Opt-in shadow end-to-end tests prove accepted shadow partial rehearsal advances retained root metadata with command/resource segments. | Production-off feed accepted partial rehearsal advances the runtime owner's retained root metadata and hit target metadata while `RenderPipeline` production baseline remains unchanged. | `RenderPipeline` retained root baseline is not patched by a partial path. | `RenderPipeline.Build` tests keep the current diff/layout baseline unchanged. | Accepted partial updates must advance retained root metadata for the next diff. |
| Fallback reporting | Planner tests cover `AppliedPartial`, `FallbackFull`, `Rejected`, and every local reason. | Shadow result vocabulary reports `Disabled`, `ShadowAppliedPartial`, `ShadowFallbackFull`, and `ShadowRejected` for tests/internal diagnostics only. | Production-off feed reports disabled, partial/full fallback kind, fallback-applied, and owner-state-preserved flags without changing diagnostics text. | No runtime/compositor reporting hookup exists. | Diagnostics formatter tests keep CLI output unchanged. | Runtime hookup must preserve local reporting without diagnostics expansion. |
| Compositor ownership | Dry-run and adapter tests keep segment ownership/execution outside compositor mutation. | Compositor shadow probe executes shadow segmented reads outside compositor and verifies execute order, resolver ownership, and hit-test no-change. | Production-adjacent no-change tests cover compositor counters, backend execute calls, dirty range propagation, hit-test results, diagnostics text, enabled rendering through the normal full-frame batch, and default-off/missing-owner/stale/fallback/rejected/empty/fresh-partial selector behavior in `DrawingBackendCompositor`. | `DrawingBackendCompositor` still selects the existing retained frame as the production render source. | Compositor no-mutation tests keep counters, retained frame, and backend execution unchanged. | Compositor ownership of multiple retained snapshots and segmented execution must be explicit before hookup. |
| Regression coverage | Preflight tests cover planner, projector, root patch, segment table, and segmented reader scaffolds. | Shadow owner, runtime owner seam, compositor shadow probe, diagnostic harness, result vocabulary, disabled no-change sentinel, opt-in end-to-end, fallback, rejection, and adapter tests cover the current shadow path. | Production-adjacent tests cover retained-frame segment ownership, main-compositor selector default-off lazy allocation, same-frame freshness guard, missing-owner no-op, enabled-secondary equivalence, internal result reporting, candidate render-source backend call order, partial rehearsal, owner-side hit target lookup, projection failure fallback, multiple `FrameDrawingResources` lifecycle exact-once, repeated replacement, counter semantics, segment-local dirty-range handoff, dirty-range mismatch routing, four-piece atomicity, fallback, rejected, stale, rebuild, empty batch, malformed dirty ranges, dispose, empty segments, throw path, clipped/no-hit hit-test, dirty-range isolation, and diagnostics text no-change. | No partial apply runtime hookup exists to cover. | Focused and full test suites act as the no-change regression guard. | Planner, retained frame, compositor, diagnostics, and hit-test coverage must remain green. |

The internal `PartialApplyIntegrationGateChecklist` mirrors this table as a regression guard. Every gate currently remains unsatisfied and `CanHookUpPartialApply` is false.

Each checklist item carries separate `PreflightEvidence`, `ShadowRuntimeEvidence`, `ProductionOffRuntimeEvidence`, `ProductionRuntimeEvidence`, `NoChangeRegressionEvidence`, `BlockingCondition`, and `RuntimePromotionCondition` fields. A gate is not satisfied until production runtime ownership/hookup exists; preflight, shadow runtime, or production-off runtime evidence alone must not flip `CanHookUpPartialApply`.

## 7. Pipeline Dry-Run Boundary

The current pure-test dry-run chains `RetainedPartialApplyPlanner`, `HitTargetMetadataProjector`, `RetainedRootMetadataPatcher`, `RetainedResourceSegmentTable`, and `SegmentedRetainedFrameReader` to prove decision flow. It uses synthetic retained data and test-only resolvers, does not replace production command ranges, does not retain production resources, and leaves `RenderPipeline`, `RetainedRenderFrame`, and `DrawingBackendCompositor` sentinels unmutated after each step.

The opt-in segmented retained-frame diagnostic harness can also consume real `RenderPipeline.Build` output plus an explicit retained root. This remains a test/diagnostic harness: it is not called by production `RenderPipeline.Build`, does not replace the current `RetainedRenderFrame` read path, and its local shadow result vocabulary is not sent to CLI diagnostics formatters. The retained-frame segment ownership seam is stronger than the feed because it sits beside a real `RetainedRenderFrame` and preserves `TryReadFrame` while maintaining secondary segmented state. The production-off owner feed now delegates to that seam: disabled mode allocates no seam, enabled mode follows batches into `SegmentedRetainedFrameRuntimeOwner`, and rendering still comes from the existing production retained frame. Accepted production-off partial rehearsal advances commands, resource snapshots, retained root metadata, and owner-side hit target metadata together; projection or dirty-range failure preserves owner state before explicit fallback. Multi-snapshot lifecycle, owner-side clipped/no-hit lookup, handoff counter semantics, missing-owner no-op behavior, same-frame freshness, segment-local dirty-range rules, dirty-range mismatch routing, candidate render-source backend execution, and main-compositor internal result reporting are now rehearsed only as internal/test-only evidence. The compositor shadow probe and handoff harness still run beside the compositor; the main compositor selector is internal, default-off, and does not select segmented reads as the production render source.

## 8. Sealed Lines

- Diagnostics stay regression-only; no unified diagnostics channel/event bus/registry.
- Controls helpers stay PoC-owned; no typed ids.
- Scroll feedback stays side-channel only; no scroll extraction.
- Translator seam stays internal; no translator promotion.
- Scroll settings provider stays design-only/fallback-only if reopened.
- Retained planner stays planner-only; no `RenderPipeline.Build` main-path change.