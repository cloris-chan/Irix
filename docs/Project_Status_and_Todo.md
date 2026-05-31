# Irix Project Status

> Current developer handoff note for the Irix Windows PoC.
> Last verified: 2026-05-31.

---

## Canonical Docs

| Doc | Purpose |
|-----|---------|
| [Irix_Framework_Design.md](Irix_Framework_Design.md) | Main architecture design, layer boundaries, ADR index, and private execution scope. |
| [Active-Worklist.md](Active-Worklist.md) | Current private work items and framework-promotion candidates. |
| [Style-System.md](Style-System.md) | Style layer contract: layout style, visual style, text shaping style, composition style, and control-state style. |
| [Animation-Composition.md](Animation-Composition.md) | Animation ownership contract: UI-runtime animation, compositor animation, hybrid animation, and scroll presentation model. |
| [GPU-Composition-Architecture.md](GPU-Composition-Architecture.md) | Future platform-neutral composition/GPU-offload architecture for D3D12 now and Vulkan/Metal later. |
| [D3D12-Composition.md](D3D12-Composition.md) | Active D3D12 composition implementation for transform/opacity and fixed-clip scroll ticks over retained draw output. |
| [Poc-Promotion-Contracts.md](Poc-Promotion-Contracts.md) | Promotion contracts for `Irix.Poc` runtime adapters, Windows backend adapters, and legacy/debug presentation paths. |
| [Glyph-Atlas-Design.md](Glyph-Atlas-Design.md) | D3D12 glyph atlas text renderer design, degradation policy, and guarded coverage expansion. |
| [Glyph-Atlas-Entry-Eviction-Design.md](Glyph-Atlas-Entry-Eviction-Design.md) | Design-only preconditions and non-goals for future entry-level LRU / sub-rect free-list work. |
| [ADR-Scissor-Clipping.md](ADR-Scissor-Clipping.md) | Clip/scissor/text-clip decision and guarded behavior. |
| [LayoutDirty-Design.md](LayoutDirty-Design.md) | Layout dirty classification and StyleOnly planning boundary. |
| [Diagnostics-Snapshot.md](Diagnostics-Snapshot.md) | Diagnostics snapshot boundary and formatter contract. |

One-off smoke/default evidence files are intentionally not canonical. Keep durable evidence in current regression commands, local guard summaries, and this status document.

---

## Current State

Private execution mode: these docs are working notes for a personal repository, not public compatibility or delivery commitments. Prefer direct target-architecture work, migrate internal APIs and tests when boundaries change, and keep secondary paths explicit and diagnostic-visible.

GitHub Actions quota is currently exhausted. `.github/workflows/ci.yml` remains configured, but Actions status is not the source of truth until quota returns. Use local gates:

- Before and after broad changes: `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke`.
- After glyph/page/shaping changes: `.\scripts\glyph-atlas-regression.ps1 -Mode Local`.
- After page-policy, eviction, or shaping overhauls: manually run `.\scripts\glyph-atlas-regression.ps1 -Mode Nightly`.
- Keep local `TestResults\glyph-atlas-regression-*-*.guard.summary.txt` as the active glyph-atlas status source.

| Area | Status |
|------|--------|
| Branch state | Renderer foundation and current D3D12 composition work are on `master`; the active head has removed the premature viewport-space render-target cache and keeps direct/layer-content composition as the mainline. |
| Core foundation | Complete as a baseline, but not frozen. Reopen it when a target-architecture change is worth the migration cost; do not preserve old APIs for compatibility. |
| Windows backend | D3D12 is the active Windows PoC backend. GlyphAtlas is the only active text composition path. DirectWrite and WIC are allowed only as shaping, metrics, raster, and image source-data paths; final composition stays in the D3D12 command stream. |
| Glyph atlas | Coverage is guard-gated. New coverage should include matching oracle/regression/degradation checks; unguarded coverage expansion is rejected. SVG/COLR paint-tree-only glyphs, BiDi beyond current resolved-level projection, unsafe AtlasFull cases, and init/record failure remain explicit D3D12-only degradation until targeted. |
| Text/value IR | Framework/core paths must not retain raw text strings. Retained and drawing paths use `TextNodeContent`, `TextBufferSnapshot`, `FrameTextArena`, `TextSlice`, and resolver boundaries. CLI/debug/report formatting strings are output-boundary exceptions. |
| Architecture boundary | `Irix.Poc` is an app, diagnostics, and adapter-glue project. It is not the reusable framework home. `D3D12DrawingBackend` has moved to `Irix.Platform.Windows`; `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` have moved to `Irix.Rendering`; `WindowDrawCommandTranslator` remains the Poc adapter around viewport timing, app/control scroll feedback, diagnostics, allocation attribution, and Counter default composition. `WindowBackend` stays as legacy/debug presentation. |
| Style / animation / composition | D3D12 composition spine now has runtime `NodeKey` transform declarations, fixed-clip scroll presentation declarations, retained `CompositionTarget` / `ScrollCompositionTarget` resolution from normal UI output, retained nested/mixed-clip scroll decomposition into ordered composition layers, resolved compositor-only ticks over the retained frame, inverse-transform hit-test remapping for active transform and scroll presentation layers, typed composition clock values, declaration-level animation markers with compositor-produced runtime event ids, a runtime marker event pump that maps marker ids to app messages outside the backend, PoC scroll presentation interrupt policy for commit/cancel/retarget, live wheel retarget wiring from active compositor scroll presentation back to runtime state, a main-app scroll presentation producer that commits logical layout once and advances compositor-only ticks, reason-typed scroll presentation lifecycle invalidation/cancellation for viewport/tree/layout/text/max-scroll-changing frames, scroll runtime cancellation matrix diagnostics, active scroll hit-test/press diagnostics, main-app scroll interaction diagnostics for hover/press/release, chained retarget, `EnsureRunning` rapid wheel coalescing, top/bottom boundary wheel clamp behavior, and resize/DPI/max-scroll lifecycle cancellation while presentation is active, ordered multi-layer `CompositionFrame` execution on D3D12, D3D12 layer content cache for disjoint composition layers with display-scale, resource-resolver, and resource-frame reset invalidation coverage, formal D3D12 composition backend capabilities, composition skip diagnostics, and visible PoC/demo coverage on the retained UI path. Internal offscreen/render-target caching is not active; consider it only after content-space bounds/origin/clip semantics are designed and direct composition still needs it. Secondary path code is only for documented blockers. |
| Performance | Allocation measurement/hardening is on hold. Latest `--diagnose-text-cache 180` warm scroll baseline with buildRoot/layout attribution is the comparison point: about `396760 bytes`, `2204 B/frame`; `pipeline.layout=501` B/frame, `tree.buildRoot=546` B/frame, and record about `45 B/frame`. Only safe empty publication array reuse was implemented. Remaining tree/layout/snapshot buckets require ownership design, not opportunistic retained-array micro-optimization. |
| Diagnostics | Optional diagnostics are built with `IrixDiagnostics=true`, compile out by default, and use separate `bin/diagnostics` / `obj/diagnostics` outputs from normal `bin/runtime` / `obj/runtime` builds to avoid stale mixed-ABI incremental outputs. |
| Docs | Current docs describe active architecture and remaining work, not process history. |

Performance allocation note: `layout.nodeWalk=0` means the layout bucket is retained publication cost, not property-read or clip-propagation allocation. `LayoutTreeResult` arrays are retained across frames and must not expose pooled mutable storage. `tree.buildRoot` attribution is diagnostic-only; it does not change `VirtualNode` or builder APIs. Reopen retained-array, layout-result, or snapshot work only with a written ownership design and one measured target bucket.

---

## Active Source Guards

Keep guards green for these non-negotiable boundaries:

- Final text composition stays on the D3D12 GlyphAtlas path; DirectWrite/WIC remain source-data providers.
- No runtime shader compile in active renderer source.
- No raw retained text string path in framework/core rendering or layout state.
- No public string style/value factory path, legacy attribute-era API, public `VirtualNodeProperty` construction, silent `PropertyValue` getters, ref-struct leakage into retained/batch state, or framework/backend `record struct` outside the existing PoC authoring exception.
- No compositor/runtime code extraction before the relevant style, animation, composition, scroll, or input/control ownership contract is written.
- No viewport-space internal offscreen/render-target cache on the active composition path.

---

## Next Reasonable Work

| Priority | Work | Boundary |
|----------|------|----------|
| P0 | Keep local Smoke gate authoritative | Run `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke` for broad changes while Actions quota is unavailable. |
| P1 | Direct composition hardening | Keep transform/opacity, fixed-clip scroll, nested/mixed-clip layers, hit testing, marker dispatch, layer content cache, and skip diagnostics on the direct D3D12 path. Add narrow regression coverage when bugs appear in those semantics. |
| P1 | Main-app composition integration | Main app wheel input starts compositor scroll presentation ticks; hover/press/release can route through active presentation; chained retarget distance, `EnsureRunning` rapid wheel coalescing, top/bottom boundary wheel clamping, and resize/DPI/max-scroll lifecycle cancellation are diagnostic-covered; active presentation is cancelled with typed reasons before viewport/tree/layout/text/max-scroll-changing retained frames render. Continue hardening cancellation, retarget, hit-test, interaction, and diagnostics behavior without adding a generic fallback compositor. |
| P1 | Framework ownership contracts | `D3D12DrawingBackend` and translator core promotion are complete. `WindowBackend` stays in Poc. Scroll and input/control contracts are documentation-only for now; do not move those runtime types until the contracts are ready for code extraction. |
| P1 | Allocation follow-up hold | Keep the current `--diagnose-text-cache 180` baseline as a future comparison point. Do not resume retained array, snapshot, or tree-builder optimization without an ownership design and a single measured target bucket. |
| P1 | Glyph atlas expansion / maintenance | Add coverage aggressively when it is target-architecture work and includes matching oracle/regression/degradation checks; reject unguarded coverage drift. |
| P2 | Entry-level eviction | Keep design-only until retained atlas command ownership and sub-rect free-list ownership are explicit. |
| P2 | Pixel/layout oracle | Separate future work; do not block the current D3D12-only renderer foundation. |
