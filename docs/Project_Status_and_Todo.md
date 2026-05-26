# Irix Project Status

> Current developer handoff note for the Irix Windows PoC.
> Last verified: 2026-05-24.

---

## Canonical Docs

| Doc | Purpose |
|-----|---------|
| [Irix_Framework_Design.md](Irix_Framework_Design.md) | Main architecture design, layer boundaries, phase boundaries, ADR index, and private checkpoint scope. |
| [Post-Foundation-Roadmap.md](Post-Foundation-Roadmap.md) | Post-renderer-foundation roadmap for style, animation, GPU composition, and runtime ownership planning. |
| [Style-System-v0.md](Style-System-v0.md) | Style layer contract: layout style, visual style, text shaping style, composition style, and control-state style. |
| [Animation-Composition-v0.md](Animation-Composition-v0.md) | Animation ownership contract: UI-runtime animation, compositor animation, hybrid animation, and scroll presentation model. |
| [GPU-Composition-Architecture-v0.md](GPU-Composition-Architecture-v0.md) | Future platform-neutral composition/GPU-offload architecture for D3D12 now and Vulkan/Metal later. |
| [D3D12-Composition-Spike-v0.md](D3D12-Composition-Spike-v0.md) | Narrow first composition IR implementation gate for D3D12-backed translation and opacity. |
| [Private-Execution-Backlog.md](Private-Execution-Backlog.md) | Private execution backlog and framework-promotion candidates. |
| [Poc-Promotion-Contracts.md](Poc-Promotion-Contracts.md) | Promotion contracts for `Irix.Poc` runtime adapters, Windows backend adapters, and legacy/debug presentation paths. |
| [Glyph-Atlas-Design.md](Glyph-Atlas-Design.md) | D3D12 glyph atlas text renderer design, degradation policy, and guarded coverage expansion. |
| [Glyph-Atlas-Entry-Eviction-Design.md](Glyph-Atlas-Entry-Eviction-Design.md) | Design-only preconditions and non-goals for future entry-level LRU / sub-rect free-list work. |
| [ADR-Scissor-Clipping-v0.md](ADR-Scissor-Clipping-v0.md) | Clip/scissor/text-clip v0 decision and guarded behavior. |
| [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md) | Layout dirty classification and StyleOnly planning boundary. |
| [Diagnostics-Snapshot-v0.md](Diagnostics-Snapshot-v0.md) | Diagnostics snapshot v0 boundary and formatter contract. |

One-off smoke/default evidence files are intentionally not canonical. Keep durable evidence in current regression commands, local guard summaries, and this status document.

---

## Current State

Private execution mode: milestone labels are planning snapshots, not compatibility or release constraints. Prefer direct target-architecture work, migrate internal APIs and tests when boundaries change, and keep fallback paths explicit and diagnostic-visible.

GitHub Actions quota is currently exhausted. `.github/workflows/ci.yml` remains configured, but Actions status is not the source of truth until quota returns. Use local gates:

- Before and after broad changes: `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke`.
- After glyph/page/shaping changes: `.\scripts\glyph-atlas-regression.ps1 -Mode Local`.
- After page-policy, eviction, or shaping overhauls: manually run `.\scripts\glyph-atlas-regression.ps1 -Mode Nightly`.
- Keep local `TestResults\glyph-atlas-regression-*-*.guard.summary.txt` as the active glyph-atlas status source.

| Area | Status |
|------|--------|
| Branch state | Renderer foundation work has been merged to `master`. The closeout commit is `7659a15 Document renderer foundation closeout gate`. |
| Core foundation | Complete as a baseline, but not frozen. Reopen it when a target-architecture change is worth the migration cost; do not preserve old APIs for compatibility. |
| Windows backend | D3D12 is the active Windows PoC backend. GlyphAtlas is the only active text composition path. DirectWrite and WIC are allowed only as shaping, metrics, raster, and image source-data paths; final composition stays in the D3D12 command stream. |
| Glyph atlas | Coverage is guard-gated rather than milestone-frozen. New coverage should include matching oracle/regression/degradation checks; unguarded coverage expansion is rejected. SVG/COLR paint-tree-only glyphs, BiDi beyond current resolved-level projection, unsafe AtlasFull cases, and init/record failure remain explicit D3D12-only degradation until targeted. |
| Text/value IR | Framework/core paths must not retain raw text strings. Retained and drawing paths use `TextNodeContent`, `TextBufferSnapshot`, `FrameTextArena`, `TextSlice`, and resolver boundaries. CLI/debug/report formatting strings are output-boundary exceptions. |
| Architecture boundary | `Irix.Poc` is an app, diagnostics, and adapter-glue project. It is not the reusable framework home. `D3D12DrawingBackend` has moved to `Irix.Platform.Windows`; `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` have moved to `Irix.Rendering`; `WindowDrawCommandTranslator` remains the Poc adapter around viewport timing, app/control scroll feedback, diagnostics, allocation attribution, and Counter default composition. `WindowBackend` stays as legacy/debug presentation. |
| Style / animation / composition | D3D12 composition spike is the active implementation gate. First code validates layer identity, translation, opacity, diagnostics, and a visible PoC demo through the D3D12 backend path; fallback compatibility is only for explicit blockers. |
| Performance | Allocation measurement/hardening is closed for this stage. Latest `--diagnose-text-cache 180` warm scroll baseline with buildRoot/layout attribution is the comparison point: about `396760 bytes`, `2204 B/frame`; `pipeline.layout=501` B/frame, `tree.buildRoot=546` B/frame, and record about `45 B/frame`. Only safe empty publication array reuse was implemented. Remaining tree/layout/snapshot buckets require ownership design, not opportunistic retained-array micro-optimization. |
| Docs | Current docs should describe active architecture and remaining work, not milestone process history. |

Performance allocation note: `layout.nodeWalk=0` means the layout bucket is retained publication cost, not property-read or clip-propagation allocation. `LayoutTreeResult` arrays are retained across frames and must not expose pooled mutable storage. `tree.buildRoot` attribution is diagnostic-only; it does not change `VirtualNode` or builder APIs. Reopen retained-array, layout-result, or snapshot work only with a written ownership design and one measured target bucket.

---

## Active Source Guards

Keep guards green for these non-negotiable boundaries:

- Final text composition stays on the D3D12 GlyphAtlas path; DirectWrite/WIC remain source-data providers.
- No runtime shader compile in active renderer source.
- No raw retained text string path in framework/core rendering or layout state.
- No public string style/value factory path, legacy attribute-era API, public `VirtualNodeProperty` construction, silent `PropertyValue` getters, ref-struct leakage into retained/batch state, or framework/backend `record struct` outside the existing PoC authoring exception.
- No compositor/runtime code extraction before the relevant style, animation, composition, scroll, or input/control ownership contract is written.

---

## Next Reasonable Work

| Priority | Work | Boundary |
|----------|------|----------|
| P0 | Keep local Smoke gate authoritative | Run `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke` for broad changes while Actions quota is unavailable. |
| P1 | Post-foundation architecture planning | Use the roadmap and style/animation/GPU docs before code changes. After the contract is narrow enough, prefer a D3D12/GPU-backed spike over a generic compatibility compositor. |
| P1 | D3D12-first composition spike | Validate layer identity, immutable composition IR handoff, transform/opacity updates, and diagnostics on the active D3D12 backend. Use the existing renderer as fallback only when a written blocker prevents the GPU path. |
| P1 | Framework ownership contracts | `D3D12DrawingBackend` and translator core promotion are complete. `WindowBackend` stays in Poc. Scroll and input/control contracts are documentation-only for now; do not move those runtime types until the contracts are ready for code extraction. |
| P1 | Allocation follow-up hold | Keep the current `--diagnose-text-cache 180` baseline as a future comparison point. Do not resume retained array, snapshot, or tree-builder optimization without an ownership design and a single measured target bucket. |
| P1 | Glyph atlas expansion / maintenance | Add coverage aggressively when it is target-architecture work and includes matching oracle/regression/degradation checks; reject unguarded coverage drift. |
| P2 | Entry-level eviction | Keep design-only until retained atlas command ownership and sub-rect free-list ownership are explicit. |
| P2 | Pixel/layout oracle | Separate future work; do not block the current D3D12-only renderer foundation. |
