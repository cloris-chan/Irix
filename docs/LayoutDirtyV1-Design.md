# Layout Dirty v1 Design

## Goal

Layout dirty v1 is a diagnostic and planning step before partial layout. The current implementation still rebuilds the full `LayoutTreeBuilder` result whenever layout is invalidated. This document defines how dirty changes are classified and where future partial layout may stop or must bubble upward.

## Dirty Categories

| Category | Examples | Future partial-layout boundary |
| --- | --- | --- |
| `StyleOnly` | `IsHovered`, `IsPressed`, `IsFocused`, current non-geometric `ActionId` metadata | May stay at the element/draw-command level when bounds, clip, hit target geometry, scroll metrics, and child ranges are unchanged. Current v0 still rebuilds layout when a dirty DFS node is supplied, but the reason is explicit. |
| `TextSizeAffecting` | `Text` content, future `FontSize`, `FontFamily`, `FontWeight`, wrapping/text style properties | May relayout the text leaf first. If measured size is unchanged, it can stop locally; if size changes, it must bubble to the containing layout flow because later siblings move. |
| `LayoutAffecting` | `ScrollY`, `Width`, `Height`, padding/spacing/layout metric changes | Must relayout the affected container/subtree. If child bounds, visible height, max scroll, or sibling offsets change, bubble to the nearest `ScrollContainer` and possibly root. |
| `TreeStructure` | add/remove/move, key/kind changes, child order changes | Must bubble to the parent container because DFS ranges, element ranges, command ranges, and hit target ranges can change. Root replacement remains a root rebuild. |
| `ViewportChanged` | renderer/layout viewport size changes after resize | Root rebuild. Viewport affects available width, root clip, scroll visible height, and every descendant clip intersection. |

## Bubble Rules

- Dirty can remain local only when the element count, element order, bounds, clip bounds, hit target geometry, scroll diagnostics, and command range mapping remain stable.
- Dirty must bubble to the parent flow when a node changes its measured width/height, visible height, max scroll, child count, or child order.
- Dirty must bubble to root when the viewport changes or root `ScrollContainer` semantics change.
- Nested `ScrollContainer` semantics are unchanged for this v1 design pass; any nested clip/layout expansion should be a separate task.

## Current Implementation Boundary

- `RenderPipeline` records `LastLayoutRebuildReason`, `LayoutRebuildCount`, and per-node `LastDirtyClassifications`.
- `--debug-ui` displays `layoutRebuildCount`, `LastLayoutRebuildReason`, and `LastDirtyClassifications` so hover, press, scroll, and resize can be observed manually.
- `--diagnose-input` prints dirty reasons for hover-only, press, and release so real input classification is available without UI.
- Real PoC path tests cover hover-only `StyleOnly`, press `StyleOnly`, scroll `LayoutAffecting`, resize `ViewportChanged`, release `TextSizeAffecting`, and mixed `StyleOnly` + `LayoutAffecting` priority.
- Manual `--debug-ui` verification passed on 2026-05-10 for hover/press/scroll/resize dirty reason observation.
- `LayoutTreeBuilder` still performs a full layout rebuild for dirty nodes, tree changes, and viewport changes.
- `dirtyElementRanges` and dirty command ranges remain diagnostics for retained command replacement; they are not partial layout.
- `--diagnose` and tests use rebuild reason output to make future partial-layout work auditable before behavior changes.

## Stage Freeze

- Layout dirty v1 diagnostics are complete for this stage: reason tracking, debug UI visibility, `--diagnose-input` dirty reason output, tests, and docs are closed.
- Do not continue expanding dirty diagnostics in this stage unless fixing a concrete regression in the existing outputs.
- Do not skip `StyleOnly` layout in this stage. A dirty DFS node still causes the same full `LayoutTreeBuilder` rebuild path as before.
- Do not implement partial layout, local subtree layout, or a `LayoutTreeBuilder` rewrite as part of layout dirty v1 closure.

## StyleOnly Patch v0 Design

This section is design-only. It is not implemented in layout dirty v1, and it must not change `RenderPipeline.Build` behavior in the current stage.

### Reusable Layout Preconditions

A future style-only patch may reuse retained layout only when every dirty classification is `StyleOnly` and the retained layout context is otherwise identical. The following data must remain unchanged:

- Viewport bounds and root clip semantics.
- Flat layout element count and order.
- DFS node to element range mapping.
- Element bounds and clip bounds.
- Hit target geometry: bounds and clip bounds.
- Scroll diagnostics: visible height, content height, `ScrollY`, `MaxScrollY`, visible/clipped element counts.
- Element to draw command range mapping.

Any `TextSizeAffecting`, `LayoutAffecting`, `TreeStructure`, or `ViewportChanged` reason must bail out to the existing full layout path. Unknown properties should also bail out unless they are explicitly added to the style-only allowlist with tests proving the invariants above.

### Draw Command Rerecord Scope

Layout reuse does not mean command reuse. The dirty element ranges still need to be mapped through the retained element-to-command ranges, and only those draw command ranges should be rerecorded. This is enough for hover, pressed, and focused visual changes because button fill color and related visual state live in draw commands, not in layout geometry.

Command rerecord must not rebuild layout, must not reorder commands, and must not expand beyond the merged dirty element ranges unless the existing element-to-command mapping cannot prove a stable range. If a dirty element has no stable command range, the patch must bail out to full command recording for the frame.

### Hit Target Metadata Patch

Style-only dirty can reuse hit target geometry only when bounds and clip bounds are unchanged. Metadata must still be refreshed for affected hit targets. `ActionId` is currently classified as style-only because it does not move geometry, but it is unsafe to reuse old action metadata blindly: a stable rectangle can point at a different action.

A future patch should therefore update hit target metadata for affected element ranges while preserving geometry from retained layout. If metadata cannot be mapped from the dirty element range to an existing hit target, or if the number/order of hit targets changes, the patch must bail out to full hit target rebuild.

### Frame Resource Lifetime

New draw commands must bind resources from the current frame's `FrameDrawingResources`. They must not reference text slices, text styles, or other handles owned by a previous frame. A style-only command patch must either record with the current frame resource arena or prove that retained resources are still owned by the same live retained frame scope.

The retained command buffer may only accept partial command replacement when resource ownership is safe. If new commands are recorded against a different resource owner, the patch must update the retained frame's resource ownership consistently or fall back to a full apply. This prevents stale `TextSlice` / `ResourceHandle` references after old frame resources are returned to the pool.

### Plan Builder Boundary

`StyleOnlyPatchPlanBuilder` is post-layout validation only in the current stage. It consumes the next frame's already-built layout elements, dirty element ranges, retained element-to-command ranges, and retained hit targets, then reports whether a future style-only patch would be safe. Because it depends on next layout output, it is not a fast-path implementation and must not be used as evidence that `LayoutTreeBuilder` was skipped.

The `--diagnose` style-only plan smoke prints an eligible hover-only plan and a layout-affecting fallback reason to make these guard decisions visible. The smoke still runs after layout has been built; it does not replace retained layout, retained frame resources, hit targets, or command buffers.

Plan diagnostics are complete for this stage. `--diagnose` now exposes the eligible/fallback smoke surface needed to observe the planner, including dirty element ranges, dirty command ranges, fallback reason, and patched hit target count. Do not expand the formatter further unless an existing field regresses or a future implementation requires a new stable diagnostic contract.

### Future Fast-Path Insertion Point

A true style-only fast path would live inside `RenderPipeline.Build` after dirty classification and before the current full layout rebuild branch. The current stage must not enable this path. The intended future flow is:

```text
Build(root, viewport, dirtyNodes):
	compute hadRetainedLayout, treeChanged, viewportChanged, hasDirty
	classify dirty nodes against retainedRoot and next root
	resolve LastLayoutRebuildReason for diagnostics

	if style-only fast path is enabled:
		plan = TryBuildStyleOnlyFastPathPlan(
			retainedRoot,
			nextRoot,
			retainedLayoutResult,
			retainedElementCommandRanges,
			retainedHitTargets,
			dirtyClassifications,
			dirtyNodes,
			viewportChanged)

		if plan.Eligible:
			rerecord dirty command ranges using retained geometry plus next-node visual metadata
			patch hit target metadata while preserving retained geometry
			attach current-frame resources
			apply retained frame according to the plan result
			update retained root and diagnostics without incrementing layout rebuild count
			return patched render frame

	run the existing full layout rebuild and full command recording path
```

This insertion point matters because the decision must happen before `_layoutTreeBuilder.BuildLayoutTree(root, viewportBounds, dirtyNodes)`. Once the next layout has already been built, the plan is only validating whether the future shortcut would have been safe; it is not avoiding layout work.

### Future Fast-Path Inputs

The current plan builder accepts `nextLayoutElements`, so it cannot be the actual fast-path input shape. A real style-only patch must derive everything from retained layout plus the next `VirtualNode` tree:

- Retained layout result: flat elements, layout tree nodes, scroll diagnostics, and retained DFS-to-element ranges.
- Retained element-to-command ranges from the previous command recording.
- Retained hit targets and retained frame/resource ownership state.
- Previous retained root and next root, plus dirty DFS indices and dirty classifications.
- Viewport identity and root clip identity.
- A metadata projection step that maps dirty DFS nodes to retained elements, preserves retained bounds/clip, and reads only style-only metadata from the next `VirtualNode` tree.

The metadata projection must not call layout and must not consume next layout output. For buttons, it can derive visual command metadata from retained geometry plus next-node `IsHovered`, `IsPressed`, `IsFocused`, `ActionId`, and stable label metadata only when the dirty classification proves text measurement is unchanged. Any content or property that can affect measurement remains `TextSizeAffecting` or `LayoutAffecting` and must fall back.

### Future Patch Output Boundary

A future fast-path planner should return a data-only result that can be applied by `RenderPipeline.Build` without rebuilding layout:

- Dirty element ranges mapped from retained layout tree nodes.
- Dirty command ranges mapped from retained element-to-command ranges.
- Replacement draw commands for those dirty command ranges.
- Patched hit targets, including refreshed metadata and retained geometry.
- Current-frame `FrameDrawingResources` used by all replacement commands.
- A retained-frame apply result such as `AppliedPartial`, `FallbackFull`, or `Rejected`, with a reason when partial apply is not safe.

The apply step must be explicit: replacing command ranges, hit targets, resources, and retained root state are separate responsibilities. If any output cannot be produced from retained data and next-node metadata alone, the future implementation must fall back to the existing full layout path.

### Required Tests Before Implementation

Before implementing a style-only patch path, add tests proving that layout rebuild count does not increase only for the intended fast path, and that retained bounds, clips, element ranges, hit target geometry, scroll diagnostics, and command ranges remain byte-for-byte stable. Also test that button visual commands update, `ActionId` metadata updates, current-frame resources are used, and every mixed or uncertain dirty reason falls back to the existing full layout behavior.