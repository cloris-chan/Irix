# v1 Partial Apply Preflight Design

> Planner-only design for the work required before any real partial apply hookup. This document does not change `RenderPipeline.Build`, `RetainedRenderFrame`, `DrawingBackendCompositor`, `FrameDrawingResources`, input routing, diagnostics output, or backend rendering behavior.

## 1. Decision Summary

| Area | Decision |
|------|----------|
| Cross-frame resources | Prefer resource snapshot / composite resolver over stable global handles. |
| Current implementation scope | Keep `RetainedPartialApplyPlanner` data-only and side-effect-free. |
| Resource cache | Do not add one for v1 preflight. |
| Backend contract | Do not touch D3D12 or `IDrawingBackend` in this line. |
| Hit targets | Preserve retained geometry; reproject metadata from next `VirtualNode` only after a dedicated projector exists. |
| Runtime hookup | Postponed until every gate in section 5 is satisfied. |

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

### Test Strategy

| Test area | Required proof |
|-----------|----------------|
| Planner fallback | `ResourceOwnershipMismatch` remains side-effect-free when old/new resources differ. |
| Ownership | Accepted partial retains old and new snapshots; replaced snapshots release only after last command segment disappears. |
| Resolver correctness | Old text resolves from old resources and replacement text resolves from new resources in one retained frame. |
| Pool safety | Re-rented `FrameDrawingResources` with a new `FrameId` is never treated as the old snapshot. |
| Full fallback | Full apply releases all previous snapshots and exposes only the new full-frame resolver. |
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

## 4. Planner-Only Boundary

`RetainedPartialApplyPlanner` is allowed to read `RenderPipeline.LastRetainedInputSnapshot`, current viewport, retained resources, and replacement resources. It is not allowed to:

- Call `RenderPipeline.Build`.
- Mutate `RenderPipeline.RetainedFrame`.
- Mutate `DrawingBackendCompositor.RetainedFrame`.
- Replace command ranges.
- Retain or release resources.
- Write diagnostics output.
- Change hit-test runtime behavior.

Regression tests should keep every planner result reason covered: `None` for a data-only eligible plan, plus `NotStyleOnly`, `ViewportChanged`, `MissingRetainedSnapshot`, `UnstableCommandRange`, `HitTargetPatchFailed`, and `ResourceOwnershipMismatch`.

## 5. Integration Gates

No partial apply hookup should land until every gate below is satisfied.

| Gate | Required proof | Blocks premature fast-path by |
|------|----------------|-------------------------------|
| Resource resolver ownership | Old retained commands and new replacement commands resolve correct text/style resources in one retained frame. | Prevents mixed old/new commands from using the wrong `FrameDrawingResources`. |
| Resource dispose policy | Retained snapshots release exactly once across partial, full fallback, invalidate, and compositor dispose. | Prevents pooled resource reuse while retained commands still hold `TextSlice` references. |
| Command range stability | Dirty element ranges map to contiguous command ranges with unchanged total command count. | Prevents unsafe command buffer surgery. |
| Hit target metadata projection | Action metadata is reprojected from next `VirtualNode` without next layout output, while geometry stays retained. | Prevents stale or geometrically wrong hit targets. |
| Retained root update | Accepted partial updates retained root/control metadata consistently with command and hit-target changes. | Prevents future diffs from comparing against stale input. |
| Fallback reporting | Local `AppliedPartial` / `FallbackFull` / `Rejected` result is available without global diagnostics expansion. | Prevents silent behavior changes. |
| Compositor ownership | Compositor can own multiple retained resource snapshots or explicit segments without changing backend behavior unexpectedly. | Prevents backend/runtime coupling leaks. |
| Regression coverage | Planner, retained frame, compositor, diagnostics, and hit-test behavior all have no-change tests. | Prevents accidental StyleOnly fast-path enablement. |

## 6. Sealed Lines

- Diagnostics stay regression-only; no unified diagnostics channel/event bus/registry.
- Controls helpers stay PoC-owned; no typed ids.
- Scroll feedback stays side-channel only; no scroll extraction.
- Translator seam stays internal; no translator promotion.
- Scroll settings provider stays design-only/fallback-only if reopened.
- Retained planner stays planner-only; no `RenderPipeline.Build` main-path change.