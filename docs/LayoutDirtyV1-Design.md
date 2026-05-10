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