# Layout Dirty v1 Design

## Goal

Layout dirty v1 is a diagnostic and planning step before partial layout. The current implementation still rebuilds the full `LayoutTreeBuilder` result whenever layout is invalidated. This document defines how dirty changes are classified and where future partial layout may stop or must bubble upward.

## Dirty Categories

| Category | Examples | Future partial-layout boundary |
| --- | --- | --- |
| `StyleOnly` | `IsHovered`, `IsPressed`, `IsFocused`, current non-geometric `ActionId` metadata | May stay at the element/draw-command level when bounds, clip, hit target geometry, scroll metrics, and child ranges are unchanged. Current v0 still rebuilds layout when a dirty DFS node is supplied, but the reason is explicit. |
| `TextSizeAffecting` | `Text` content, future `FontSize`, `FontFamily`, `FontWeight`, wrapping/text style attributes | May relayout the text leaf first. If measured size is unchanged, it can stop locally; if size changes, it must bubble to the containing layout flow because later siblings move. |
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

Any `TextSizeAffecting`, `LayoutAffecting`, `TreeStructure`, or `ViewportChanged` reason must bail out to the existing full layout path. Unknown attributes should also bail out unless they are explicitly added to the style-only allowlist with tests proving the invariants above.

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

### Required Tests Before Implementation

Before implementing a style-only patch path, add tests proving that layout rebuild count does not increase only for the intended fast path, and that retained bounds, clips, element ranges, hit target geometry, scroll diagnostics, and command ranges remain byte-for-byte stable. Also test that button visual commands update, `ActionId` metadata updates, current-frame resources are used, and every mixed or uncertain dirty reason falls back to the existing full layout behavior.