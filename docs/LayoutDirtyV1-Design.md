# Layout Dirty v1

> Diagnostic and planning boundary for layout invalidation. The current implementation still rebuilds the full `LayoutTreeBuilder` result whenever layout is invalidated.

## Goal

Layout dirty v1 classifies why layout is dirty and records enough diagnostics to make future partial layout auditable before behavior changes. It does not skip layout today.

## Dirty Categories

| Category | Examples | Future partial-layout boundary |
|----------|----------|--------------------------------|
| `StyleOnly` | `IsHovered`, `IsPressed`, `IsFocused`, non-geometric interaction metadata such as `ActionId` | May stay at element/draw-command level only when bounds, clip, hit target geometry, scroll metrics, child ranges, and text measurement are unchanged. |
| `TextSizeAffecting` | `TextNodeContent`, future typed text measurement properties such as font size, font weight, wrapping, font-resource handles | May relayout the text leaf first; if measured size changes, it must bubble to the containing layout flow. No string `FontFamily` property or `PropertyValue.Text` path is implied. |
| `LayoutAffecting` | `ScrollY`, `Width`, `Height`, future typed layout metrics | Must relayout affected container/subtree and bubble when child bounds, visible height, max scroll, or sibling offsets change. |
| `TreeStructure` | add/remove/move, key/kind changes, child order changes | Must bubble to parent because DFS ranges, element ranges, command ranges, and hit target ranges can change. |
| `ViewportChanged` | renderer/layout viewport size changes after resize | Root rebuild. Viewport affects available width, root clip, scroll visible height, and descendant clip intersections. |

## Current Implementation Boundary

- `RenderPipeline` records `LastLayoutRebuildReason`, `LayoutRebuildCount`, and per-node `LastDirtyClassifications`.
- `--debug-ui` displays layout rebuild count/reason/classifications for observation.
- `--diagnose-input` prints dirty reasons for hover-only, press, and release.
- Tests cover hover-only `StyleOnly`, press `StyleOnly`, scroll `LayoutAffecting`, resize `ViewportChanged`, release `TextSizeAffecting`, and mixed priority.
- `LayoutTreeBuilder` still performs a full layout rebuild for dirty nodes, tree changes, and viewport changes.
- `dirtyElementRanges` and dirty command ranges are diagnostics for retained command replacement; they are not partial layout.

## Stage Freeze

- Layout dirty v1 diagnostics are complete for this stage.
- Do not expand dirty diagnostics unless fixing a concrete regression in existing outputs.
- Do not skip `StyleOnly` layout in this stage.
- Do not implement partial layout, local subtree layout, or a `LayoutTreeBuilder` rewrite as part of layout dirty v1 closure.

## StyleOnly Patch Design

This section is design-only. It is not implemented in layout dirty v1 and must not change `RenderPipeline.Build` behavior in the current stage.

A future style-only patch may reuse retained layout only when every dirty classification is `StyleOnly` and retained layout context is otherwise identical:

- Viewport bounds and root clip semantics.
- Flat layout element count and order.
- DFS node to element range mapping.
- Element bounds and clip bounds.
- Hit target geometry: bounds and clip bounds.
- Scroll diagnostics: visible height, content height, `ScrollY`, `MaxScrollY`, visible/clipped element counts.
- Element to draw command range mapping.

Any `TextSizeAffecting`, `LayoutAffecting`, `TreeStructure`, `ViewportChanged`, or unknown property reason must fall back to the existing full layout path.

## Future Fast Path Contract

A true style-only fast path would live inside `RenderPipeline.Build` after dirty classification and before the current full layout rebuild branch. It must:

- Reuse retained layout only from retained data plus next-node metadata.
- Rerecord only dirty command ranges mapped from retained element-to-command ranges.
- Patch hit target metadata while preserving retained geometry.
- Bind replacement commands to current-frame `FrameDrawingResources`.
- Preserve retained frame/resource ownership and avoid stale `TextSlice` / `ResourceHandle` references.
- Return an explicit result such as `AppliedPartial`, `FallbackFull`, or `Rejected`.

Before implementation, tests must prove layout rebuild count does not increase only for the intended fast path, retained bounds/clips/ranges remain stable, button visual commands update, `ActionId` metadata updates, current-frame resources are used, and mixed or uncertain dirty reasons fall back.
