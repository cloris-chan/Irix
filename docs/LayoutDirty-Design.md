# Layout Dirty Classification

> Diagnostic and planning boundary for layout invalidation. The current implementation skips `LayoutTreeBuilder` only for proven `StyleOnly` dirty sets; all text/layout/tree/viewport or unsafe projections still rebuild the full layout. Retained partial apply can reuse command/resource/hit-target metadata after current-frame publication; it is separate from the layout-skip decision.

## Goal

Layout dirty classification records why layout is dirty and keeps enough diagnostics to make retained layout reuse auditable. It implements a narrow `StyleOnly` layout skip, but not partial layout or local subtree layout.

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
- `LayoutTreeBuilder` performs a full layout rebuild for tree, viewport, text-size, layout-affecting, or unsafe dirty projections.
- Proven `StyleOnly` dirty sets reuse retained layout geometry/tree/scroll diagnostics, refresh current text handles, patch dirty visual/action metadata in one retained-DFS pass, and re-record current-frame commands/resources.
- Segmented retained-frame ownership can select a segment-local render source after normal layout/draw publication when ownership, freshness, command range, resource, and hit-target guards pass. The Poc runtime enables this by default and `--no-partial-apply` disables it.
- The selected render-source path does not change `LayoutRebuildCount`, `LastLayoutRebuildReason`, `LastLayoutResult`, or `RenderPipelineRetainedInputSnapshot` semantics.
- Focused tests pin this boundary: `StyleOnly` hover/action-id/visual changes keep `LayoutRebuildCount` stable when reuse is proven, semantic background paint and border stroke update through the fast path, current-frame resources and hit-target metadata update, and mixed text/layout/viewport or incomplete dirty projections still fall back to full layout.

## `LayoutTreeResult` Publication Contract

`LayoutTreeResult` is a retained publication object, not a same-frame scratch result.

`RenderPipeline.Build` stores it as `LastLayoutResult`, embeds the same instance in `LastRetainedInputSnapshot`, and exposes its arrays to retained-frame planning, diagnostics, tests, and Poc scroll feedback.

Its published collections must therefore be owned, stable, and immutable after publication.

`Elements`:

- Same-frame: draw command recording, hit target construction, and dirty element to command mapping.
- Retained: `_retainedLayout`, `LastLayoutResult`, `LastRetainedInputSnapshot`, partial-apply planning, diagnostics, and tests.
- Ownership: full-frame publication storage; do not expose stack memory, rented arrays, or caller-mutated scratch.

`TreeNodes`:

- Same-frame: DFS lookup during layout diagnostics and future planning.
- Retained: `LastLayoutResult`, `LastRetainedInputSnapshot`, diagnostics, tests, and future retained planning.
- Ownership: owned preorder publication array; treat it as immutable after result construction.

`DirtyElementRanges`:

- Same-frame: dirty command range recording for the current frame.
- Retained: `LastDirtyElementRanges`, `LastRetainedInputSnapshot`, retained/partial planning, diagnostics, and tests.
- Ownership: owned merged range publication collection; empty may use the shared empty array.

`ScrollDiagnostics`:

- Same-frame: `LastMaxScrollY` resolution and Poc `TranslatorFeedbackSink` app/control feedback delivery.
- Retained: `LastLayoutResult`, `LastRetainedInputSnapshot`, diagnostics, tests, and scroll debug/status reporting.
- Ownership: owned diagnostics publication collection; empty may use the shared empty array.

The current `--diagnose-text-cache 180` layout attribution shows `layout.nodeWalk=0` B/frame in the warm scroll path. The visible `pipeline.layout` allocation is therefore publication cost: `elementsArray`, `treeNodesArray`, `dirtyRanges`, `scrollDiagnosticsArray`, and the `LayoutTreeResult` shell.

It is not evidence of property-read, clip-propagation, or temporary node-walk allocation.

Safe allocation work must preserve this contract:

- Do not return pooled mutable arrays from `LayoutTreeResult`.
- Do not retain stack memory or rented buffers through `RenderPipelineRetainedInputSnapshot`.
- Prefer empty/static singleton paths when the published collection is empty.
- If optimizing a non-empty bucket, first introduce a compact owned publication representation or a same-frame scratch boundary that is copied before publication.
- Treat `scrollDiagnosticsArray` and `result` as smaller first candidates only because their blast radius is lower than `Elements` / `TreeNodes`; `ScrollDiagnostics` is still retained state and must not become pool-backed mutable data.

Current implementation: empty `DirtyElementRanges` and absent `ScrollDiagnostics` explicitly publish `Array.Empty<T>()`.

## Stage Guard

- Layout dirty diagnostics are complete as the current baseline.
- Expand dirty diagnostics only when a target implementation needs the additional signal or an existing output regresses.
- Partial layout, local subtree layout, and `LayoutTreeBuilder` rewrites should be reopened as explicit target-architecture work, not as incidental cleanup.

## StyleOnly Patch Boundary

The planning, retained-frame ownership, and pipeline layout-skip pieces for style-only reuse are implemented: style-only eligibility, retained layout patching, current text-handle refresh, dirty element to command-range planning, retained root metadata projection, hit-target metadata projection, segmented retained-frame ownership, and guarded compositor handoff all exist as internal/runtime paths. Dirty element to command-range validation is shared through `RangeUtils`; retained layout patching walks the next tree once against retained DFS metadata instead of re-finding nodes per dirty item.

The active `RenderPipeline.Build` branch reuses retained layout only when every dirty classification is `StyleOnly` and retained layout context is otherwise identical:

- Viewport bounds and root clip semantics.
- Flat layout element count and order.
- DFS node to element range mapping.
- Element bounds and clip bounds.
- Hit target geometry: bounds and clip bounds.
- Scroll diagnostics: visible height, content height, `ScrollY`, `MaxScrollY`, visible/clipped element counts.
- Element to draw command range mapping.

Any `TextSizeAffecting`, `LayoutAffecting`, `TreeStructure`, `ViewportChanged`, or unknown property reason must fall back to the existing full layout path.

## Fast Path Contract

The style-only fast path lives inside `RenderPipeline.Build` after dirty classification and before the full layout rebuild branch. It must:

- Reuse retained layout only from retained data plus next-node metadata.
- Patch the retained layout in one DFS pass that refreshes current text/button handles and updates only dirty visual/action metadata.
- Re-record current-frame commands/resources and publish dirty command ranges mapped from current element-to-command ranges.
- Patch hit target metadata while preserving retained geometry.
- Bind replacement commands to current-frame `FrameDrawingResources`.
- Preserve retained frame/resource ownership and avoid stale `TextSlice` / `ResourceHandle` references.
- Fall back to the existing full layout path when retained projection is incomplete or unsafe.

Tests prove layout rebuild count does not increase only for the intended fast path, retained bounds/clips/ranges remain stable, visual commands update, `ActionId` metadata updates, current-frame resources are used, current text handles resolve across snapshots, and mixed or uncertain dirty reasons fall back.
