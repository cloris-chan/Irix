# Irix Project Status

> Current developer handoff note for the Irix Windows PoC.
> Last verified: 2026-05-22.

---

## Canonical Docs

| Doc | Purpose |
|-----|---------|
| [Irix_Framework_Design.md](Irix_Framework_Design.md) | Main architecture design, phase boundaries, ADR index, and v1/v2 scope. |
| [GA-Hardening-Plan.md](GA-Hardening-Plan.md) | Current GA/MVP hardening state, accepted risks, display/stability/performance evidence. |
| [Post-V1-MVP-Backlog.md](Post-V1-MVP-Backlog.md) | Remaining post-GA renderer and framework-promotion backlog. |
| [Glyph-Atlas-Post-GA-Design.md](Glyph-Atlas-Post-GA-Design.md) | Post-GA D3D12-only glyph atlas text renderer design. |
| [Glyph-Atlas-Mixed-Fallback-Smoke-Evidence-2026-05-19.md](Glyph-Atlas-Mixed-Fallback-Smoke-Evidence-2026-05-19.md) | Local evidence for mixed ASCII/NonAscii/clipped glyph-atlas fallback and default long smoke. |
| [Post-GA-Default-Baseline-Evidence-2026-05-18.md](Post-GA-Default-Baseline-Evidence-2026-05-18.md) | Local evidence for the post-GA default switch to GlyphAtlas text composition and Scissor clipping. |
| [ADR-Scissor-Clipping-v0.md](ADR-Scissor-Clipping-v0.md) | Clip/scissor/text-clip v0 decision and frozen behavior. |
| [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md) | Layout dirty classification and StyleOnly planning boundary. |
| [Diagnostics-Snapshot-v0.md](Diagnostics-Snapshot-v0.md) | Diagnostics snapshot v0 boundary and formatter contract. |
| [Project_Status_and_Todo.md](Project_Status_and_Todo.md) | This short current status page. |

Removed historical prep/checkpoint docs were already absorbed into the canonical docs above or into tests. Do not recreate `*-Prep.md`, checkpoint, release-note, or smoke-checklist docs unless there is a new active design line.

---

## Current State

| Area | Status |
|------|--------|
| V1 core | Complete / regression-only. Do not reopen core feature scope for GA cleanup. |
| Windows backend | D3D12 is the active v1 PoC backend. GlyphAtlas text composition is the post-GA default on `post-ga-renderer-foundation`; accepted atlas runs stay on D3D12 and unsupported/failure runs degrade without D3D11On12/D2D overlay. The D3D11On12/D2D overlay renderer, sync strategy, native generation entries, and explicit overlay mode are removed from active source. |
| Backend clip | Scissor is the default backend clip mode; `--disable-scissor` / `--clip-mode diagnostic` remain diagnostic rollback paths. |
| Default renderer baseline | GlyphAtlas + Scissor default baseline enabled. Do not introduce another runtime default switch before shader/resource lifetime and allocation attribution hardening. |
| Partial apply | Default-on, with `--no-partial-apply` rollback. Existing segmented ownership path and guards are test-covered. |
| Shader packaging | D3D12 rectangle and glyph-atlas passes use embedded DXBC bytecode. Runtime `D3DCompile` / `d3dcompiler_47.dll` is no longer required by renderer source. |
| Resource lifetime | D3D12 upload maps and swapchain intermediates release through `finally`. Core device/queue/RTV/command/fence setup is shared by constructor and recovery with pointer guards and constructor-failure cleanup. Rectangle vertices, glyph vertices, and glyph page upload buffers are frame-slot owned, so the renderer no longer needs a coarse `BeginFrame` wait before recording into reusable upload resources. Glyph-atlas command-list input, DirectWrite factory/font/analysis resources, vertex/page upload, pipeline, and draw resource presence is guarded before GPU binding/map. |
| Resource cache handles | POST-011 entry/page handle slices done: glyph atlas cache lookup uses stable value handles with generations, glyph entries and draw batches bind to atlas page handles, page-owned texture/upload/SRV state replaces renderer-level atlas fields, cache touches carry a monotonic atlas record serial, a bounded four-page atlas pool switches pages on allocation pressure, cold-page selection is strict-oldest guarded, AtlasFull schedules a tested record-serial- and retained-floor-gated next-record cold-page reuse request with a generation bump, reused-page cache cleanup is generation-guarded, reused-page reset marks the full page dirty and clears usage, stale atlas page handles and missing atlas GPU resources are classified as specific record failure phases, and diagnostics expose page budget/capacity, pending/completed page reuse, scheduled reuse requests, hard full-without-reuse count, page usage/fragmentation, and page age. Full LRU/entry-level eviction remains future work. |
| Glyph shaping | DirectWrite shaped-run handling is wired into the glyph atlas renderer through `IDWriteTextAnalyzer`, system `IDWriteFontFallback`, and pointer-based shaping calls over resolved spans. It projects DirectWrite glyph index/advance/offset/property output into renderer-owned `ShapedGlyph`, segment, explicit-line, and per-character advance scratch plus a synchronous `ShapedGlyphRun` span view. Shaped runs with nonzero glyph indices can rasterize/draw through the D3D12 atlas, including explicit CR/LF line breaks, tab control advances, minimal whitespace wrapping, and mixed ASCII/CJK runs segmented through DirectWrite fallback font mapping. Surrogate pairs and variation selectors are guarded before shaping so emoji/color text degrades explicitly. Complex script line breaking and color atlas rendering remain future work. |
| Display scale | Complete / regression-only for current evidence: 100%, 150%, 200%; 60Hz, 120Hz, 240Hz. |
| Text/value IR | Complete. `VirtualNode -> LayoutElement -> DrawCommandRecorder` uses `TextNodeContent` and `TextBufferSnapshot.ResolveRequired`; no string text property path. Device error diagnostics use typed `DeviceErrorDiagnostic`; text formatting stays at CLI/debug/report output boundaries. |
| Style/property model | Complete after Round 15 cleanup. Public authoring uses one typed property helper surface. Metadata/support/diagnostics remain internal. |
| Ref struct boundary | Complete for Round 16. `ref struct` is limited to synchronous builders/readers/layout context; retained IR and batches stay ordinary storable types. |
| Record struct boundary | Complete for Round 18. Framework/internal primitives, IR, render hot paths, platform types, and PoC backend/diagnostics do not use `record struct`. |
| Profiler allocation pass | Complete for retained/input first pass. The 2026-05-17 VS profiler GCDump drove targeted cleanup in canonical retained apply, input hit-test routing, input ownership diagnostics, retained metadata projection, and text snapshot comparison. |
| Allocation baseline | In place for MVU BuildView, diff, retained apply, layout full/dirty, draw command record full/dirty, D3D12 ExecuteCore at 100%/150%, render-request reuse, mock backend frame timing, and FrameDrawingResources warm pool allocation. |
| Performance micro-optimization | Pre-GA micro-optimization is closed. `--diagnose-text-cache` now attributes warm glyph-atlas allocation by tree/diff/translate/render stage; optimize from evidence, not guesses. |
| Private GA | Tagged as `v1.0-private-ga`; `post-ga-renderer-foundation` is the active next branch. This is an internal/private milestone, not a public API freeze. |
| Docs | Trimmed to canonical docs only; obsolete prep/checkpoint docs deleted. |

---

## Property / Node API Rules

Public authoring uses semantic helpers:

```csharp
VirtualNodeProperty.Width(160);
VirtualNodeProperty.Height(40);
VirtualNodeProperty.ScrollY(scrollY);
VirtualNodeProperty.Action(actionId);
VirtualNodeProperty.Hovered(isHovered);
VirtualNodeProperty.Pressed(isPressed);
VirtualNodeProperty.Focused(isFocused);
```

Current public keys:

| Property | Value kind | Effects | Supported nodes |
|----------|------------|---------|-----------------|
| `Width` | Number | Layout | Rectangle, Button |
| `Height` | Number | Layout | Rectangle, Button, ScrollContainer |
| `ScrollY` | Number | Layout | ScrollContainer |
| `ActionId` | ActionId | Interaction | Button |
| `IsHovered`, `IsPressed`, `IsFocused` | Boolean | Interaction + Visual | Button |

Current exclusions:

- No public `Opacity` until it is wired to real draw/composite behavior.
- No public `FillColor` / `TextColor` until a pure typed color value exists.
- No string style properties such as `FontFamily`.
- No global layout metrics as node properties.
- No control-specific size keys such as `ButtonHeight` or `RectangleHeight`.
- No `VirtualAttribute*` / `AttributeValue*` compatibility aliases.

Round 15 hardening state:

- `VirtualNodeKind.None` is the default node kind.
- `VirtualNode.Properties` and `VirtualNode.Children` are immutable snapshots exposed as `ReadOnlySpan<T>`; no per-node `IReadOnlyList` wrapper is allocated.
- `VirtualNode`, `VirtualNodeTree`, and `VirtualNodePatch` do not expose public value equality; explicit structural comparison is internal.
- `VirtualNodeProperty` constructor is private; normal construction goes through helpers.
- `PropertyValue` exposes `TryGetX` and `GetRequiredX`; silent getters were removed.
- `VirtualNodeFactory.Rectangle(width, height, ...)` was removed; width/height are explicit properties.
- Buttons require an explicit label; layout no longer falls back to `"Button"`.

Round 16 construction/layout state:

- `VirtualNode` remains a normal `readonly struct`; it is safe for retained/diff/patch/batch storage.
- `VirtualNodePropertyListBuilder` is a `ref struct` over caller-provided `Span<VirtualNodeProperty>`; small lists can use `stackalloc`, and callers hand off `Written` to span factories.
- `VirtualNodeChildrenBuilder` is a `ref struct` with an `InlineArray(4)` child buffer and array fallback beyond inline capacity.
- `VirtualNodeFactory.Create(...)` accepts `ReadOnlySpan<VirtualNodeProperty>` and `ReadOnlySpan<VirtualNode>` / children builder overloads.
- Internal owned-array construction is named `CreateFromOwnedArraysUnsafe` and documents the no-mutation ownership handoff.
- Public authoring helpers use `params scoped ReadOnlySpan<T>`, not array `params`; array `params` is guarded against reintroduction.
- `LayoutTreeBuilder` reads node properties through internal `PropertyReader` over `VirtualNode.Properties`; `LayoutContext` is a synchronous `ref struct`.

Display/input coordinate rules:

- Public input-facing compositor hit testing uses physical pixels: `DrawingBackendCompositor.TryGetActionIdAtPhysicalPixel(...)`.
- Logical hit testing is internal/test/handoff-only: `TryGetActionIdAtLogicalPixel(...)` is not a platform input API.
- Stored `DisplayScale` values are normalized at ingress; default/invalid scale is converted to `1x` before storage or backend frame use.
- Non-uniform draw scale remains supported. Text font size uses the normalized Y-axis text scale, not an average of X/Y.

No-record-struct boundary:

- `Irix.Core`, `Irix.Drawing`, `Irix.Rendering`, `Irix.Platform`, and `Irix.Platform.Windows` must not introduce `record struct`.
- Framework/internal primitives, retained IR, render-frame batches, layout/render diagnostics, handoff structs, platform input/viewport types, and backend hot-path structs are ordinary `readonly struct` types with explicit constructors and value equality.
- `Irix.Poc` backend, diagnostics snapshots, and rendering/window helpers follow the same no-record-struct rule.
- The only exception is UI authoring/MVU state and message shape in `Irix.Poc`, such as `CounterModel` / `CounterMessage`, scroll/input/control state, and feedback state. That exception is for authoring ergonomics only and must not leak into framework or backend hot paths.
- Framework/internal hot paths also avoid record `with` syntax; any rebuild is explicit constructor reconstruction.

Profiler-driven retained/input allocation pass:

- `RetainedTree.Apply` treats differ-created canonical-root batches as authoritative and no longer reconstructs an intermediate tree that is discarded. Dirty indices are still derived from patches: updates mark the node, adds mark the parent in the next tree, and removes mark the parent in the previous tree.
- Runtime input routing uses a value-type hit-test resolver instead of allocating a `Func<int,int,ActionId>` delegate/closure on the native input path. `Program.TryMapInputForRuntime` has no delegate overload.
- Input ownership diagnostics are value events in a bounded 128-event ring buffer. The diagnostic stream is not a retained unbounded object log and does not compact with `RemoveAt(0)`.
- Text content equality uses internal `TryResolve` instead of `ResolveRequired` when diffing/classifying. Mismatched snapshots return `false` for comparison; actual draw recording still uses `ResolveRequired` and continues to fail hard on invalid text ownership.
- Retained root metadata projection and hit-target metadata projection use stack spans/arrays for small dirty/key sets instead of short-lived `HashSet` / `List` objects on the partial-apply path.

Profiler findings intentionally not folded into this pass:

- `List<LayoutElement>`, `List<LayoutTreeNode>`, and dirty range scratch allocations remain the next layout-builder scratch arena / pooling target.
- D3D12 text layout cache and overlay synchronization remain in the post-GA glyph atlas line; this pass does not implement glyph atlas or rewrite renderer ownership.

---

## Current Verification

Latest local default-baseline verification:

```powershell
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --filter "Category!=D3D12&Category!=Performance" --verbosity normal
dotnet test --no-build -c Release --filter "Category=D3D12" --verbosity normal
dotnet test --no-build -c Release --filter "Category=Performance" --verbosity normal
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --diagnose-sync-non-ascii
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-mixed-fallback 30 --diagnose-scale 150
```

Result: Release build passed; normal tests `608` passed; D3D12 tests `6` passed; performance tests `6` passed; default GlyphAtlas sync smoke reported `syncWaits=0`; NonAscii degradation smokes presented normally. Self-contained publish passed after shader packaging removal. Glyph-atlas diagnostics now keep constructor-time `initFailurePhase` separate from runtime `recordFailurePhase`.

Mixed fallback v0 update: Release build passed; normal tests `618` passed; D3D12 tests `6` passed; performance tests `6` passed.
Program diagnostics tests `57` passed. Short default GlyphAtlas smoke reported `atlasRuns=90`, `syncWaits=0`.
Short NonAscii mixed fallback smoke previously reported overlay fallback before the degradation/removal update; this evidence is historical only.

Mixed fallback extended smoke and overlay subset parity evidence are historical and superseded by explicit degradation.
Default long GlyphAtlas `300 x 3` reported `frameSerial=900`, `presentSerial=900`, `syncWaits=0`, and `atlasRuns=2700`.
Mixed AtlasFull stress before overlay deletion is superseded by the 2026-05-20 degradation stress below.
Record-failure contract tests now pin all-renderable-run fallback with `recordFailurePhase=AtlasUploadMap`; this is unit contract coverage, not a forced GPU upload-failure smoke.

Overlay removal update: default GlyphAtlas no longer records unsupported, AtlasFull, initialization, or runtime record-failed text through D3D11On12 / D2D overlay fallback. `D3D12TextRenderer`, `TextOverlaySyncStrategy`, D3D11 query extensions, D3D11On12/D2D native generation entries, and explicit `Overlay` composition mode are removed.
2026-05-20 short mixed degradation smoke at 150% scale reported `frameSerial=3`, `presentSerial=3`, `syncWaits=0`, `atlasRuns=6`, `degradedRuns=6`, `atlasPages=4`, `atlasBudgetPages=4`, `atlasCapacity=4194304 px`, `NonAscii=6`, `textClipSkipped=0`, and `lastEffectiveTextClip=(36,264,168,39)`.
2026-05-20 MixedAtlasFull stress after the four-page pool reported `frameSerial=1`, `presentSerial=1`, `syncWaits=0`, `atlasRuns=11`, `degradedRuns=24`, `atlasPages=4`, `atlasBudgetPages=4`, `atlasCapacity=4194304 px`, `atlasEvictions=0`, `atlasPendingPageReuses=1`, `cachedGlyphs=990`, `atlasUsed=2860342 px`, `atlasFragmented=1280000 px`, `AtlasFull=23`, `NonAscii=1`, `initFailurePhase=None`, `recordFailurePhase=None`, and no device removal.
2026-05-20 mixed degradation smoke also reported page usage `atlasUsed=4461 px`, `atlasFragmented=1125 px`.
2026-05-20 MixedAtlasFullReuse stress after the four-page pool reported `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasRuns=13`, `degradedRuns=24`, `atlasPages=4`, `atlasBudgetPages=4`, `atlasCapacity=4194304 px`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `cachedGlyphs=628`, `atlasUsed=2153953 px`, `atlasFragmented=950441 px`, `atlasOldestPageAge=1`, `AtlasFull=23`, `NonAscii=1`, and no device removal. The eviction is the record-serial-gated next-frame cold-page reset/reuse path, not same-frame LRU.
2026-05-20 frame-slot upload ownership update: Release build passed; all tests `646` passed; program diagnostics tests `58` passed. Short default GlyphAtlas sync smoke reported `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, avg/p95 sync wait `0.000ms`, `atlasRuns=90`, and no device removal. Short mixed degradation and MixedAtlasFull / MixedAtlasFullReuse stress also remained `syncWaits=0`. Rectangle vertex, glyph vertex, and per-page atlas upload buffers now rotate by backbuffer frame slot instead of using a last-submitted-frame upload fence at `BeginFrame`.
Glyph atlas upload diagnostics now expose `uploadedGlyphs` separately from cache misses so upload policy can distinguish actual new atlas entries from failed misses and degradation.
Glyph atlas `Wrap` now has minimal whitespace-based multi-line support in the D3D12 path, and ASCII CR/LF explicit line breaks plus tab spacing are accepted. Simple Latin Extended, Greek, and Cyrillic BMP characters covered by the selected DirectWrite face are now accepted by the atlas classifier instead of being pre-classified as NonAscii. `NoWrap` still clips over-wide line segments; words that cannot fit, complex text, combining marks, surrogate pairs/emoji, fallback font identity cases, complex shaping, and over-height line stacks still degrade explicitly without D3D11On12 / D2D fallback.
2026-05-20 wrap-layout update: Release build passed; all tests `649` passed; program diagnostics tests `61` passed. Short default GlyphAtlas sync smoke stayed at `syncWaits=0`, `atlasRuns=90`, `degradedRuns=0`, `uploadedGlyphs=29`. Short mixed degradation stayed at `syncWaits=0`, `atlasRuns=6`, `degradedRuns=6`, `NonAscii=6`. MixedAtlasFull / MixedAtlasFullReuse stress stayed at `syncWaits=0` with the same four-page AtlasFull and next-frame reuse behavior as the frame-slot upload baseline.
2026-05-20 wrap smoke update: Release build passed; all tests `650` passed; program diagnostics tests `62` passed. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` reported expected per-frame `textRuns=4`, `atlasRuns=2`, `degradedRuns=2`, `wrappedAtlasRuns=1`, `Wrapping=1`, `NonAscii=1`; final diagnostics were `frameSerial=3`, `presentSerial=3`, `syncWaits=0`, `atlasRuns=6`, `degradedRuns=6`, `Wrapping=3`, `NonAscii=3`, and `uploadedGlyphs=22`.
2026-05-20 explicit-line-break update: Release build passed; all tests `653` passed; program diagnostics tests `65` passed. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` now includes an accepted ASCII LF run and reported expected per-frame `textRuns=5`, `atlasRuns=3`, `degradedRuns=2`, `wrappedAtlasRuns=1`, `Wrapping=1`, `NonAscii=1`; final diagnostics were `frameSerial=3`, `presentSerial=3`, `syncWaits=0`, `atlasRuns=9`, `degradedRuns=6`, `Wrapping=3`, `NonAscii=3`, and `uploadedGlyphs=24`. Short default GlyphAtlas sync smoke stayed at `syncWaits=0`, `atlasRuns=90`, and `degradedRuns=0`.
2026-05-20 tab-spacing update: Release build passed; all tests `654` passed; program diagnostics tests `66` passed. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` now includes an accepted ASCII tab run and reported expected per-frame `textRuns=6`, `atlasRuns=4`, `degradedRuns=2`, `wrappedAtlasRuns=1`, `Wrapping=1`, `NonAscii=1`; final diagnostics were `frameSerial=3`, `presentSerial=3`, `syncWaits=0`, `atlasRuns=12`, `degradedRuns=6`, `Wrapping=3`, `NonAscii=3`, and `uploadedGlyphs=25`. Short default GlyphAtlas sync smoke stayed at `syncWaits=0`, `atlasRuns=90`, and `degradedRuns=0`.
2026-05-20 page-reuse guard update: Release build passed; all tests `655` passed; program diagnostics tests `67` passed. Atlas page reuse request application is now pinned by a direct helper test: no pending request cannot apply, the triggering record cannot apply, and only a later record can apply. `--diagnose-glyph-atlas-stress --reuse-page` reported `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `atlasRecordSerial=2`, `atlasOldestPageAge=1`, `degradedRuns=23`, and `AtlasFull=23`.
2026-05-20 reused-page cleanup guard update: Release build passed; all tests `656` passed; program diagnostics tests `68` passed. Reused-page cache cleanup is now pinned by a direct helper test: dead entries are ignored, matching page index with stale generation is ignored, different page index is ignored, and only live entries from the exact reused page generation are cleared. `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, and `AtlasFull=23`.
2026-05-20 reused-page reset guard update: Release build passed; all tests `657` passed; program diagnostics tests `69` passed. Reused-page reset state is now pinned by a direct helper test: packing cursors reset to padding, row height/used/allocated/last-used reset to zero, and the full page dirty rect is marked for upload. `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, and `AtlasFull=23`.
2026-05-20 cold-page selection guard update: Release build passed; all tests `658` passed; program diagnostics tests `70` passed. Atlas cold-page selection is now pinned by a direct helper test: the first candidate is accepted from `long.MaxValue`, a strictly older candidate replaces the current selection, and equal/newer candidates do not replace it. `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, and `AtlasFull=23`.
2026-05-21 stale-page record-failure update: Release build passed; all tests `658` passed; program diagnostics tests `70` passed. Active page, pending reuse page, and draw-batch page stale-handle paths now throw typed glyph-atlas record exceptions instead of ordinary invalid-operation messages, so they enter `RecordFailed` degradation with `recordFailurePhase=Record`. `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, and `AtlasFull=23`.
2026-05-21 atlas GPU resource guard update: Release build passed; all tests `659` passed; program diagnostics tests `71` passed. Glyph vertex upload now requires the frame-slot vertex upload buffer before mapping, atlas upload requires both page texture and frame-slot upload buffer before mapping/copying, and draw requires the page SRV heap before binding; missing resources throw typed glyph-atlas record exceptions and enter the existing `RecordFailed` degradation path. `--diagnose-sync 30 1` stayed at `syncWaits=0`, `atlasRuns=90`, and `degradedRuns=0`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, and `AtlasFull=23`.
2026-05-21 retained-floor page reuse update: Release build passed; all tests `659` passed; program diagnostics tests `71` passed. Atlas page reuse now requires a pending request, a later record serial, and an `oldestRetainedRecordSerial` beyond the triggering record before a page generation can be reused. The current renderer passes the current record serial because glyph atlas draw batches are record-local; future retained atlas command caching must provide its actual retained floor or block reuse. `--diagnose-sync 30 1` stayed at `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, and `degradedRuns=0`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, and `AtlasFull=23`.
Glyph pipeline guard update: Release build passed; all tests `659` passed; program diagnostics tests `71` passed. Glyph atlas draw now requires both the pipeline state and root signature before binding either resource; missing pipeline resources throw typed glyph-atlas record exceptions and enter `RecordFailed` degradation rather than reaching a null D3D12 binding. `--diagnose-sync 30 1` stayed at `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, and `degradedRuns=0`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, and `AtlasFull=23`.
Glyph command-list guard update: Release build passed; all tests `659` passed; program diagnostics tests `71` passed. Glyph atlas record now requires a command list before advancing the atlas record serial or applying pending page reuse; a missing command list is classified as a typed `RecordFailed` degradation instead of reaching `UploadAtlas` or `DrawGlyphs` with a null command list. `--diagnose-sync 30 1` stayed at `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, and `degradedRuns=0`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, and `AtlasFull=23`.
Glyph DirectWrite resource guard update: Release build passed; all tests `659` passed; program diagnostics tests `71` passed. Glyph atlas record now requires the DirectWrite factory and font collection before advancing the atlas record serial, and font-face cache/create paths require a live DirectWrite font-face pointer before measurement or rasterization. Missing DirectWrite runtime resources enter typed `RecordFailed` degradation instead of reaching a null DirectWrite call. `--diagnose-sync 30 1` stayed at `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, `degradedRuns=0`, and `RecordFailed=0`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, `AtlasFull=23`, and `RecordFailed=0`.
Glyph run analysis guard update: Release build passed; all tests `659` passed; program diagnostics tests `71` passed. Glyph rasterization now requires `CreateGlyphRunAnalysis` to return a live DirectWrite glyph-run analysis pointer before reading bounds or alpha texture data. Missing analysis resources enter typed `RecordFailed` degradation instead of reaching a null DirectWrite analysis call. `--diagnose-sync 30 1` stayed at `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, `degradedRuns=0`, and `RecordFailed=0`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, `AtlasFull=23`, and `RecordFailed=0`.
DirectWrite record phase update: Release build passed; all tests `660` passed; program diagnostics tests `72` passed. DirectWrite runtime failures now use `recordFailurePhase=DirectWrite` instead of the generic `Record` phase across font family/font/font-face lookup, glyph index lookup, glyph advance measurement, glyph-run analysis creation, alpha bounds, and alpha texture rasterization. Missing font families still remain the existing `FontMissing` per-run degradation path when fallback family lookup reports no match. `--diagnose-sync 30 1` stayed at `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, `degradedRuns=0`, `RecordFailed=0`, and `recordFailurePhase=None`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `degradedRuns=23`, `AtlasFull=23`, `RecordFailed=0`, and `recordFailurePhase=None`.
AtlasFull reuse diagnostics update: Release build passed; all tests `660` passed; program diagnostics tests `72` passed. Glyph atlas diagnostics now distinguish current pending page reuse (`atlasPendingPageReuses`) from cumulative scheduled page reuse requests (`atlasPageReuseRequests`) and hard AtlasFull cases where no page reuse could be scheduled (`atlasFullWithoutPageReuse`). `--diagnose-sync 30 1` stayed at `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, `degradedRuns=0`, `atlasPageReuseRequests=0`, and `atlasFullWithoutPageReuse=0`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `atlasPageReuseRequests=1`, `atlasFullWithoutPageReuse=0`, `degradedRuns=23`, and `AtlasFull=23`.
Record resource phase update: Release build passed; all tests `661` passed; program diagnostics tests `73` passed. Glyph atlas runtime record diagnostics now split missing command-list input (`CommandList`), stale atlas page handles (`AtlasPage`), missing pipeline/root signature prerequisites (`Pipeline`), and missing draw SRV heap resources (`AtlasDraw`) from the generic `Record` phase while keeping DirectWrite, vertex-buffer map, and atlas-upload map phases unchanged. `--diagnose-sync 30 1` stayed at `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, `degradedRuns=0`, `RecordFailed=0`, and `recordFailurePhase=None`; `--diagnose-glyph-atlas-stress --reuse-page` stayed at `frameSerial=2`, `presentSerial=2`, `syncWaits=0`, `atlasEvictions=1`, `atlasPendingPageReuses=0`, `atlasPageReuseRequests=1`, `degradedRuns=23`, `AtlasFull=23`, `RecordFailed=0`, and `recordFailurePhase=None`.
Text cache diagnostic frame-scope update: Release build passed; all tests `662` passed; program diagnostics tests `74` passed. `TextCacheAllocationDiagnosticRunner` now calls `VirtualTextArena.BeginFrame()` for each scenario frame before building the tree, matching `CounterApplication.BuildView` and removing cross-frame text-buffer growth from the attribution. Corrected `--diagnose-text-cache 30` scroll evidence is `total=83792 bytes`, `2793 bytes/frame`, with attribution `tree=28608 bytes (953/frame)`, `diff=8200 bytes (273/frame)`, `translate=48944 bytes (1631/frame)`, and `render=8200 bytes (273/frame)`. The scale-change sample was `67944 bytes`, `2264 bytes/frame`; renderer submit is no longer the primary warm allocation target.
Text cache translate attribution update: Release build passed; all tests `663` passed; program diagnostics tests `75` passed. `--diagnose-text-cache` now prints a translate sub-breakdown from `WindowDrawCommandTranslator`: retained apply, viewport, render pipeline build, and scroll feedback. The corrected scroll sample stayed at `total=83792 bytes`, `2793 bytes/frame`, with top-level attribution `tree=28608 bytes (953/frame)`, `diff=8200 bytes (273/frame)`, `translate=48944 bytes (1631/frame)`, and `render=8200 bytes (273/frame)`. Translate attribution was `retainedApply=0`, `viewport=0`, `pipeline=48944 bytes (1631/frame)`, `feedback=0`, so the next allocation target is inside `RenderPipeline.Build` rather than retained tree apply, viewport conversion, scroll feedback, or D3D12 submit.
Text cache pipeline attribution update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. `--diagnose-text-cache` now prints a render-pipeline sub-breakdown: dirty classification, layout, draw record, hit targets, retained input snapshot, and retained frame update. The corrected scroll sample stayed at `total=83792 bytes`, `2793 bytes/frame`; translate remained `48944 bytes (1631/frame)`, all in pipeline build. Pipeline attribution was `classify=0`, `layout=24344 bytes (811/frame)`, `record=0`, `hitTargets=8200 bytes (273/frame)`, `snapshot=8200 bytes (273/frame)`, and `retainedFrame=8200 bytes (273/frame)`. The next allocation target is layout result arrays first, then hit-target/snapshot/retained-frame result boundaries.
Latin-1 atlas widening update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. Glyph atlas classification now accepts simple Latin-1 BMP characters (`U+00A0` through `U+00FF`) so precomposed accented UI text can enter the existing DirectWrite glyph-index/raster path. Combining marks, surrogate pairs/emoji, CJK, complex shaping, and fallback-font identity remain explicit `NonAscii` degradation until the shaped glyph-run path exists. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` now includes a Latin-1 atlas candidate and reported expected per-frame `textRuns=7`, `atlasRuns=5`, `degradedRuns=2`, `wrappedAtlasRuns=1`, `Wrapping=1`, `NonAscii=1`; final diagnostics were `frameSerial=3`, `presentSerial=3`, `syncWaits=0`, `atlasRuns=15`, `degradedRuns=6`, `Wrapping=3`, `NonAscii=3`, and `uploadedGlyphs=26`.
Simple BMP glyph atom update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. Glyph atlas cache keys now use a `GlyphAtom` instead of a raw `char`, with the current atom carrying simple Unicode code point plus DirectWrite glyph index. This keeps the existing simple glyph path zero-string and prepares the cache for a later shaped-run atom. The simple accepted classifier expands from Latin-1 to Latin Extended, Greek, and Cyrillic BMP ranges when the selected face maps the character directly; combining marks, surrogate pairs/emoji, CJK/fallback-face cases, and complex shaping remain explicit `NonAscii` degradation. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` stayed at `syncWaits=0`, expected per-frame `textRuns=7`, `atlasRuns=5`, `degradedRuns=2`, and final diagnostics `atlasRuns=15`, `degradedRuns=6`, `NonAscii=3`, `Wrapping=3`, `uploadedGlyphs=28`.
Shaped-run probe update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. Glyph atlas initialization now creates `IDWriteTextAnalyzer`, DirectWrite resource guards include the analyzer, and `NonAscii` degraded runs probe DirectWrite shaping through pinned span/pointer overloads without retaining source strings. Probe failures stay diagnostic-only and do not disable the atlas. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` stayed at expected per-frame `textRuns=7`, `atlasRuns=5`, `degradedRuns=2`, `Wrapping=1`, `NonAscii=1`; final diagnostics were `frameSerial=3`, `presentSerial=3`, `syncWaits=0`, `atlasRuns=15`, `degradedRuns=6`, `NonAscii=3`, `Wrapping=3`, `RecordFailed=0`, `shapedProbeRuns=3`, and `shapedProbeGlyphs=33`.
Shaped glyph run projection update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. DirectWrite shaped probe output now projects into a local `ShapedGlyph` scratch buffer and a synchronous `ShapedGlyphRun` span view carrying glyph index, advance, offset, cluster map, and glyph property flags without retaining source text. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` preserved the expected per-frame `textRuns=7`, `atlasRuns=5`, `degradedRuns=2`, `Wrapping=1`, `NonAscii=1`; final diagnostics stayed at `syncWaits=0`, `atlasRuns=15`, `degradedRuns=6`, `RecordFailed=0`, `shapedProbeRuns=3`, and `shapedProbeGlyphs=33`. This prepares shaped atlas raster/draw work while preserving current degradation behavior.
Single-face shaped atlas update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. The glyph atlas now attempts `NonAscii` single-face `NoWrap` shaped runs after DirectWrite shaping; accepted shaped glyphs reuse the D3D12 atlas raster/cache/draw path through a shaped `GlyphAtom` variant while retaining zero source strings. Missing glyph index `0`, CJK/fallback-face cases, line breaks/tabs, and shaped wrapping still degrade. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` now reports per-frame `textRuns=7`, `atlasRuns=6`, `degradedRuns=1`, `Wrapping=1`, `NonAscii=0`; final diagnostics were `syncWaits=0`, `atlasRuns=18`, `degradedRuns=3`, `RecordFailed=0`, `shapedProbeRuns=3`, and `shapedProbeGlyphs=42`. `--diagnose-glyph-atlas-mixed-fallback 3 --diagnose-scale 150` remains the fallback-face degradation guard with final `atlasRuns=6`, `degradedRuns=6`, `NonAscii=6`, and `RecordFailed=0`.
Single fallback-face shaped atlas update: Release build passed; program diagnostics tests `76` passed. Glyph atlas initialization now creates system `IDWriteFontFallback`; shaped probes retry selected-face missing glyph runs through DirectWrite fallback when the whole run maps to one fallback font face. Glyph cache keys now include internal font-face identity plus physical em size, so fallback faces and different DPI/font-size raster outputs cannot collide without retaining source strings. `--diagnose-glyph-atlas-mixed-fallback 3 --diagnose-scale 150` now uses pure CJK fallback-face samples and reports per-frame `textRuns=4`, `atlasRuns=4`, `degradedRuns=0`, `NonAscii=0`; final diagnostics were `syncWaits=0`, `atlasRuns=12`, `degradedRuns=0`, `RecordFailed=0`, `shapedProbeRuns=6`, and `shapedProbeGlyphs=12`. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` stayed at `atlasRuns=18`, `degradedRuns=3`, `Wrapping=3`, and `NonAscii=0`.
Mixed fallback-face shaped atlas update: Release build passed; program diagnostics tests `76` passed. Missing-glyph shaped runs can now be segmented through DirectWrite `MapCharacters`; each segment carries its own cached font face and effective em size while sharing the renderer-owned shaped glyph scratch. The mixed fallback smoke is back to ASCII+CJK inside the same `DrawTextRun` and reports per-frame `textRuns=4`, `atlasRuns=4`, `degradedRuns=0`, `NonAscii=0`; final diagnostics were `syncWaits=0`, `atlasRuns=12`, `degradedRuns=0`, `RecordFailed=0`, `shapedProbeRuns=6`, and `shapedProbeGlyphs=90`. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` stayed at `atlasRuns=18`, `degradedRuns=3`, `Wrapping=3`, and `NonAscii=0`. At this point the remaining glyph shaping gap was shaped wrapping/line breaking, plus surrogate/color glyph handling.
Shaped explicit-line-break atlas update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. `NoWrap` shaped text now splits CR/LF input into renderer-owned line spans without retaining source strings, then draws each line through the existing shaped segment/font-face atlas path. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` now uses a multi-line combining-mark shaped sample and reports expected per-frame `textRuns=7`, `atlasRuns=6`, `degradedRuns=1`, `Wrapping=1`, `NonAscii=0`; final diagnostics were `syncWaits=0`, `atlasRuns=18`, `degradedRuns=3`, `RecordFailed=0`, `shapedProbeRuns=3`, `shapedProbeGlyphs=69`, `Wrapping=3`, and `NonAscii=0`. `--diagnose-glyph-atlas-mixed-fallback 3 --diagnose-scale 150` stayed atlas-only with final `atlasRuns=12`, `degradedRuns=0`, `RecordFailed=0`, and `shapedProbeGlyphs=90`. At that point the remaining glyph shaping gap was shaped wrapping beyond explicit CR/LF, shaped tabs, and surrogate/color glyph handling.
Shaped tab-control atlas update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. `NoWrap` shaped text now splits tabs out as zero-glyph control segments with measured four-space advance, so tabbed combining-mark text remains in the shaped atlas path without retaining source strings or rasterizing tab glyphs. `--diagnose-glyph-atlas-wrap 3 --diagnose-scale 150` now uses a shaped tab plus CR/LF sample and reports expected per-frame `textRuns=7`, `atlasRuns=6`, `degradedRuns=1`, `Wrapping=1`, `NonAscii=0`; final diagnostics were `syncWaits=0`, `atlasRuns=18`, `degradedRuns=3`, `RecordFailed=0`, `shapedProbeRuns=3`, `shapedProbeGlyphs=66`, `Wrapping=3`, and `NonAscii=0`. At that point the remaining glyph shaping gap was shaped wrapping beyond explicit CR/LF and surrogate/color glyph handling.
Shaped whitespace-wrap atlas update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. Shaped text now projects monotonic DirectWrite cluster maps into per-character advances and reuses the existing whitespace line planner, then maps planned line ranges back to shaped segment spans. The wrap diagnostic now uses a `Wrap` combining-mark sample with tab and spaces and reports expected per-frame `textRuns=7`, `atlasRuns=6`, `degradedRuns=1`, `wrappedAtlasRuns=2`, `Wrapping=1`, `NonAscii=0`; final diagnostics were `syncWaits=0`, `atlasRuns=18`, `degradedRuns=3`, `RecordFailed=0`, `shapedProbeRuns=3`, `shapedProbeGlyphs=60`, `Wrapping=3`, and `NonAscii=0`. Mixed fallback stayed atlas-only with final `atlasRuns=12`, `degradedRuns=0`, `RecordFailed=0`, and `shapedProbeGlyphs=78`. Remaining glyph shaping gap is complex line breaking plus surrogate/color glyph handling.
Surrogate/color guard update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. The shaped probe now rejects surrogate pairs and variation selectors before DirectWrite shaping, keeping emoji/color text as explicit `NonAscii` degradation instead of risking an accepted invisible grayscale glyph. The wrap diagnostic now includes an emoji plus VS16 sample and reports expected per-frame `textRuns=8`, `atlasRuns=6`, `degradedRuns=2`, `wrappedAtlasRuns=2`, `Wrapping=1`, `NonAscii=1`; final diagnostics were `syncWaits=0`, `atlasRuns=18`, `degradedRuns=6`, `RecordFailed=0`, `shapedProbeRuns=3`, `shapedProbeGlyphs=60`, `Wrapping=3`, and `NonAscii=3`. Mixed fallback stayed atlas-only with final `atlasRuns=12`, `degradedRuns=0`, `RecordFailed=0`, and `shapedProbeGlyphs=78`.
Complex-script guard update: Release build passed; all tests `664` passed; program diagnostics tests `76` passed. The shaped probe now rejects known complex-script candidate ranges before DirectWrite shaping, keeping BiDi/script text as explicit `NonAscii` degradation until atlas line breaking and ordering are correctness-complete. The wrap diagnostic now includes an Arabic sample and reports expected per-frame `textRuns=9`, `atlasRuns=6`, `degradedRuns=3`, `wrappedAtlasRuns=2`, `Wrapping=1`, `NonAscii=2`; final diagnostics were `syncWaits=0`, `atlasRuns=18`, `degradedRuns=9`, `RecordFailed=0`, `shapedProbeRuns=3`, `shapedProbeGlyphs=60`, `Wrapping=3`, and `NonAscii=6`. Mixed fallback stayed atlas-only with final `atlasRuns=12`, `degradedRuns=0`, `RecordFailed=0`, and `shapedProbeGlyphs=78`.

Manual smoke status:

| Smoke | Status |
|-------|--------|
| Default run | Passed |
| 100% scale | Passed |
| 150% scale | Passed |
| 200% scale | Passed |
| Runtime scale switch | Passed |
| Refresh evidence | 60Hz / 120Hz / 240Hz accepted; 144Hz removed from the current matrix because no hardware is available. |

Current allocation/performance guards:

| Area | Current guard |
|------|---------------|
| VirtualNode authoring helpers | Builder/span path must not regress over inline helper path by more than 128 KB over 5,000 authoring iterations. |
| Diff -> layout -> record pipeline parity | Builder/span root path must not regress over inline helper path by more than 128 KB over 500 retained pipeline iterations. |
| Frame-stage warm path baseline | `BuildView`, diff, retained apply, layout full/dirty, record full/dirty, render-request reuse, and D3D12 `ExecuteCore` are measured as separate stages. |
| Stage allocation budgets | `BuildView` < 128 KB; diff < 16 KB; retained apply < 8 KB; layout full/dirty < 32 KB each; record full/dirty < 64 KB each; render-request reuse < 64 KB. |
| D3D12 ExecuteCore scale path | 100% and 150% scale execute with zero command-scaling allocation. |
| FrameDrawingResources warm pool | Warm rent/add/seal/return stays under 2 MB over 1,000 frames. |
| Compositor render loop | Mock backend render loop stays under the 20 ms/frame average guard over 180 frames. |

Latest local frame-stage allocation output (2026-05-17, Debug build, .NET 10.0.8, Windows x64):

| Stage | Allocated bytes | Allocation type | Current reading |
|-------|-----------------|-----------------|-----------------|
| MVU `BuildView` | 17,408 | Necessary authoring/result allocation plus remaining temporary arrays in the PoC view builder | Highest measured stage; do not optimize blind yet. Next investigation should separate `CounterApplication` authoring arrays from framework node freezing cost. |
| `VirtualNodeDiffer.CreatePatchBatch` | 1,288 | Necessary patch result boundary, with small retained temporary overhead | Already low; not the next target unless patch payload grows. |
| `RetainedTree.Apply` canonical path | 80 | Necessary `ApplyResult` / dirty result boundary | Canonical retained apply is effectively clean after the profiler pass. |
| `LayoutTreeBuilder.BuildLayoutTree` full | 544 | Necessary result arrays | Flat layout tree removed the main temporary collection cost; remaining bytes should mostly be returned result arrays. |
| `LayoutTreeBuilder.BuildLayoutTree` dirty | 576 | Necessary result arrays plus dirty range result | Dirty path is near full path; no obvious temporary collection issue. |
| `DrawCommandRecorder.Record` full | 192 | Necessary command/resource result boundary | Full record is low; current command/resource handoff dominates. |
| `DrawCommandRecorder.Record` dirty | 1,752 | Necessary command/resource result boundary plus dirty command range result | Higher than full because dirty range/result handoff is included; verify before changing recorder internals. |
| D3D12 `ExecuteCore` 100% | 0 | No allocation | Scale-free backend execute path is clean. |
| D3D12 `ExecuteCore` 150% | 0 | No allocation | On-the-fly scale path does not allocate command arrays. |
| Render-request reuse | 2,280 | Re-record result boundary plus retained input snapshot copies; no layout rebuild | Attribution below shows record/result handoff is the largest residual source, not hit targets. |

Render-request reuse allocation attribution (same local environment):

| Component | Allocated bytes | Reading |
|-----------|-----------------|---------|
| `DrawCommandRecorder.Record` | 1,688 | Main residual cost. This includes command owner/resource result handoff and element-command range result arrays even when layout is reused. |
| `BuildHitTargets` | 152 | One small `HitTestTarget[]`; not the primary optimization target for this measured tree. |
| `RenderPipelineRetainedInputSnapshot` spread/copy | 448 | Snapshot copies are visible but smaller than record. Reusing retained arrays may help, but it should follow record/result analysis. |
| `RenderFrameBatch` construction | 24 | Effectively noise; not worth targeting. |
| Total render-request reuse path | 2,280 | Layout rebuild count stayed unchanged and `LastLayoutRebuildReason == None`. |

CI currently enforces the guards above but does not persist exact per-stage byte output as an artifact. Treat the table as the latest local measurement, not a CI average.

Source guards currently block:

- Primitive `ActionId.ToString()` and `VirtualPropertyKey.ToString()`.
- Legacy attribute-era API names.
- Old alias helpers.
- String style/value factories.
- Global layout key reintroduction.
- Missing property metadata.
- Public/no-op `Opacity`.
- `VirtualNodeProperty` public/internal construction.
- Silent `PropertyValue` getters.
- Reference-equality `VirtualNode` semantics.
- Ref struct leakage into batch/retained state sources.
- Framework/backend `record struct` and framework record `with` syntax outside MVU authoring state exceptions.
- Device error diagnostic regression back to retained `string` state in platform/PoC paths.

---

## Next Reasonable Work

| Priority | Work | Boundary |
|----------|------|----------|
| P0 | Renderer foundation hardening | GlyphAtlas + Scissor default baseline enabled; do not flip another default. Continue resource lifetime hardening from the embedded-shader baseline. |
| P1 | Resource cache / stable global handles | POST-011 now has generation-safe retained-floor page reuse. Continue with resource lifetime hardening and only move to full LRU/entry-level eviction after retained atlas command ownership is explicit. |
| P1 | Shader packaging follow-up | Runtime shader compile is removed. Decide later whether inline embedded DXBC is enough or a build-time shader asset pipeline is worth adding. |
| P1 | Attribute warm glyph atlas allocation | Latest corrected `--diagnose-text-cache 30` scroll sample is `2793 bytes/frame`; pipeline attribution is `layout=811`, `hitTargets=273`, `snapshot=273`, `retainedFrame=273`, `record=0` bytes/frame. Optimize layout result arrays first. |
| P1 | Framework promotion review | Translator/scroll/settings promotion only after a concrete contract is written in the main design/backlog docs. |
| P1 | Degradation smoke evidence | Short mixed and MixedAtlasFull smoke now show nonzero `DegradedRuns`; four-page allocation reduces AtlasFull degradation while retained-floor-gated next-record page reuse remains active. Full LRU/entry-level eviction is still deferred before widening atlas text coverage. |
| P1 | Overlay removal path | Active source removal done. Do not reintroduce D3D11On12/D2D; reduce accepted degradation through D3D12 handling. |
| P2 | StyleOnly layout skip | Future fast path only; current layout still rebuilds. |

Do not mix glyph atlas implementation, renderer rewrites, or public API expansion into style/property cleanup.

