# Private Execution Backlog

> Private planning snapshots are not release gates or compatibility constraints. This document tracks actionable work and keeps completed history compressed.

---

## Windows Version Boundary

The Windows PoC separates target SDK from runtime minimum. Windows-targeted projects inherit `IrixWindowsTargetFramework=net10.0-windows10.0.26100.0` and `IrixWindowsSupportedOSPlatformVersion=10.0.15063.0` from `Directory.Build.props`; CI checks for .NET 10 and Windows SDK 10.0.26100.0 before restore/build. The 10.0.15063.0 runtime floor is intentional for PerMonitorV2 DPI awareness and display scale support. `IDWriteFactory4` is available within this runtime target and is the baseline DirectWrite factory for the glyph atlas path.

---

## Completed Checkpoints

| Milestone | Summary |
|-----------|---------|
| Private checkpoint | Default-on partial apply, D3D12 segmented ownership, device-lost recovery, platform smokes, Windows SDK CI checks, performance guards, GPU memory pressure diagnostics, and command allocator reset failure handling are complete for that evidence point. |
| Renderer foundation | D3D12 GlyphAtlas text composition is default. DirectWrite/WIC remain source-data providers only. Regression matrix, soak, color-format, BiDi oracle, glyph oracle, and entry-eviction design docs are the active evidence set. |
| Windows backend promotion | `D3D12DrawingBackend` and its helper structs moved from `Irix.Poc` to `Irix.Platform.Windows` without renderer behavior changes. |
| Translator core promotion | `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` moved from `Irix.Poc` to `Irix.Rendering`; `WindowDrawCommandTranslator` remains Poc glue for viewport timing, app/control feedback, diagnostics, allocation attribution, and Counter default composition. |
| Resource cache / stable handles | Glyph entries and atlas pages use stable value handles with generations. The atlas grows on demand to a bounded 48-page budget, tracks Alpha/Bgra page formats, reports resident bytes and fragmentation, and supports format-scoped retained-floor-gated page reuse. |
| Performance allocation phase | Split frame-stage allocation guards and `--diagnose-text-cache` attribution exist. Layout attribution now shows `pipeline.layout` is mostly published arrays/result shell, not node-walk/property-read/clip propagation. Safe empty publication array reuse is complete. Further tree/layout/snapshot optimization is paused until ownership design exists. |
| Post-foundation design start | Style, animation, and GPU composition design contracts now exist. The first implementation path is D3D12-backed transform/opacity plus fixed-clip scroll presentation with runtime `NodeKey` declarations, retained target resolution, compositor-owned ticks, marker event publication, runtime marker dispatch, multi-layer composition frame execution, active hit-test remapping over the retained frame, main-app scroll presentation production, lifecycle invalidation/cancellation, and D3D12 layer content caching. Viewport-space internal offscreen caching was removed from the mainline; secondary paths remain reserved for explicit blockers. |

---

## Active Backlog

### P0 - Local Gate Discipline

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-021 | Local Smoke gate | GitHub Actions quota is exhausted, so local `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke` is the broad-change gate. | Guard `status=Passed`; matrix `degradedRuns=0`, `glyphAtlasInitialized=True`, `finalComposition=D3D12`; soak `deviceLost=False`, `syncWaits=0`, `hardFullWithoutReuse=0`, `RecordFailed=0`. |
| POST-022 | Guarded coverage expansion | Glyph atlas script/format support is guard-gated by oracle/regression/degradation coverage. | Add new coverage when it has matching oracle/regression coverage; reject unguarded coverage drift. |
| POST-023 | Source boundary guards | Active guards keep final text composition on the D3D12 GlyphAtlas path, block runtime shader compile, block raw retained text strings, and prevent premature compositor/runtime extraction. | Guards stay green after renderer, text, diagnostics, or architecture-boundary changes. |

Run `Smoke` before/after broad changes. Do not add artifact-upload work until Actions quota returns.

### P1 - Post-Foundation Architecture Design

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-040 | Style system v0 | `Style-System-v0.md` defines layout, visual, text shaping, composition, and control-state style layers. | Implement against an explicit target layer and invalidation rule; do not add generic compatibility style layers. |
| POST-041 | Animation/composition v0 | `Animation-Composition-v0.md` defines UI-runtime, compositor, hybrid, and backend-internal animation classes. Runtime transform/opacity and fixed-clip scroll declarations now resolve retained `NodeKey` targets into compositor ticks, active hit-test remapping, compositor-produced marker events, runtime marker dispatch through app-owned mapping, PoC commit/cancel/retarget decisions for presented scroll interruption, live wheel retarget from active compositor scroll presentation, main-app scroll presentation production, and lifecycle cancellation for invalid retained frames. | Keep skipped-compositor diagnostics explicit before broader app integration. |
| POST-042 | GPU composition architecture v0 | `GPU-Composition-Architecture-v0.md` defines IR, capabilities, GPU offload phases, and backend mapping notes. First D3D12 transform/opacity, fixed-clip scroll presentation, runtime marker dispatch, multi-layer frame execution, nested/mixed-clip scroll decomposition, main-app scroll presentation production, scroll lifecycle cancellation, and layer content caching exist. | Continue D3D12-first: add composition skip diagnostics next; do not start Vulkan/Metal before the active backend proves those contracts. |
| POST-043 | Scroll compositor design | Fixed-clip scroll presentation is implemented for single-layer and decomposed nested/mixed-clip retained targets; logical scroll remains app/control runtime and presented scroll is compositor-owned. PoC runtime policy now defines commit, cancel, and retarget behavior, live wheel input can retarget from active presentation, the main app path can start compositor scroll presentation ticks, and viewport/tree/layout/text/max-scroll invalidation clears active presentation before render. | Add skipped-compositor diagnostics before broad app integration. |
| POST-044 | Composition IR implementation gate | `D3D12-Composition-Spike-v0.md` now covers `CompositionAnimationDeclaration`, `CompositionAnimationPlan`, `CompositionScrollPresentationDeclaration`, `CompositionScrollPresentationPlan`, compositor-owned ticks, transform/fixed-clip scroll hit-test remapping, animation marker events, runtime marker dispatch, multi-layer frame execution, retained nested/mixed-clip decomposition, D3D12 layer content cache diagnostics, formal D3D12 composition capabilities, diagnostic output, `--diagnose-composition-scroll`, `--diagnose-composition-marker-runtime`, `--diagnose-composition-multilayer`, `--diagnose-composition-layer-cache`, and `--composition-demo` on normal retained UI output. | Keep ticks independent of UI rebuild; next add explicit unsupported/skipped producer diagnostics. |
| POST-045 | D3D12-first GPU offload spike | Active path is direct/layer-content D3D12 composition. Internal offscreen/render-target caching was removed from the mainline because viewport-space caching introduced incorrect fixed-clip scroll semantics. | Consider offscreen only after content-space bounds, origin, clip, invalidation, and hit-test semantics are designed and direct composition still needs it. Add secondary path code only for a documented blocker found during the spike. |

Animation/composition implementation note: when the selected property is compositor-eligible, the first implementation case should target compositor/GPU execution.

### P1 - Framework Boundary / Promotion

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-024 | Keep translator core in Rendering and outer adapter in Poc | `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` now live in `Irix.Rendering`. `WindowDrawCommandTranslator` remains in `Irix.Poc` because it composes viewport callbacks, app/control scroll feedback, diagnostics, allocation attribution, and Counter default style construction. | Do not move the outer adapter or make the core public without a new contract. |
| POST-025 | Keep `D3D12DrawingBackend` in Windows platform | Backend and helper structs now live in `Irix.Platform.Windows`. | Preserve scissor/text clip/device recovery/scale diagnostics tests; do not move it back into `Irix.Poc`. |
| POST-026 | Keep `WindowBackend` isolated | Contract decision is stay: it is a legacy/debug `INativeWindow.SetContent` presentation path. | Do not move it into `Irix.Rendering` or `Irix.Platform.Windows`; replace only if the legacy/debug path becomes unnecessary. |
| POST-027 | Scroll ownership contract | Written as a documentation boundary. `ScrollDiagnostics` is layout observation; `ScrollFeedback` is app/control feedback; `ScrollController`, `ScrollState`, and `ScrollFramePump` remain app runtime state in `Irix.Poc`. | Do not move scroll runtime code until a separate extraction commit chooses a framework runtime owner. |
| POST-028 | Settings provider | Runtime settings wiring remains postponed. | Add only after a concrete app/framework boundary is written; keep the internal default provider until then. |
| POST-029 | Input/control projection contract | Written as a documentation boundary. `InputOwnershipState`, `ControlVisualState*`, and `ActionHitTestResolver` remain Counter/Poc runtime projection for now. | Do not promote input/control code without replacing Counter-specific action mapping and single-pointer assumptions. |

### P1 - Allocation Follow-Up Hold

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-030 | Allocation phase closeout | Latest `--diagnose-text-cache 180` warm scroll sample is the comparison baseline: about `2204 B/frame`; `tree.buildRoot=546` B/frame, `pipeline.layout=501` B/frame, `layout.nodeWalk=0`, record `45 B/frame`. Safe empty publication array reuse is the only optimization from this phase. | Reopen aggressively only with an ownership design and one measured target bucket. |
| POST-031 | Layout builder scratch ownership | Layout full/dirty allocation is stable but still visible in attribution. | Design only. Scratch lifetime and pooling must not leak retained state, stack memory, or rented arrays. |
| POST-032 | Retained snapshot boundary review | Snapshot copy allocation is visible but not dominant. | Design only. Any reuse design preserves `TextSlice`/resource snapshot validity and cross-frame ownership rules. |

Layout publication note: main layout arrays are retained publication state, not disposable same-frame scratch.

`Elements`, `TreeNodes`, `DirtyElementRanges`, and `ScrollDiagnostics` are retained by `LastLayoutResult` / `LastRetainedInputSnapshot`.

Safe reuse is limited to empty/static collections or same-frame scratch that is copied before publication. Do not pool or reuse `VirtualNode` owned arrays, non-empty `LayoutTreeResult` publication arrays, or `TextBufferSnapshot` character arrays in this stage.

### P1 / P2 - Glyph Atlas Follow-Up

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-033 | Local/Manual soak cadence | `Smoke` is routine; `Local` after glyph/page/shaping changes; `Nightly` after page-policy, eviction, or shaping overhauls. | Evidence is updated only when results materially change or cover a representative change. |
| POST-034 | BGRA/TIFF natural font coverage | Default local Segoe UI Emoji naturally covers COLR/layer; Noto probes naturally cover PNG image data. Direct BGRA/TIFF natural font coverage remains unavailable locally. | Add evidence if a real font/environment exposes direct BGRA or TIFF; otherwise keep code-level selector/raster/decode/page-format tests. |
| POST-035 | Entry-level eviction design | Design exists; implementation is not selected until ownership is explicit. | Implement entry LRU/sub-rect free-list only after retained atlas command ownership exposes an oldest retained atlas record serial and page-local free-list ownership is explicit. |
| POST-036 | Pixel/layout oracle | Structural DirectWrite analyzer/glyph oracles exist; pixel/layout oracle is deferred. | Future work remains separate from current regression matrix and must validate the D3D12 atlas path. |

### P2 - Deferred Architecture

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-037 | Unified diagnostics channel | Postponed. | Replace per-component diagnostics only after current snapshots prove insufficient. |
| POST-038 | StyleOnly layout skip | Design only. | Implement after style layer classification, layout dirty ownership, and retained-frame semantics are stable. |
| POST-039 | Second graphics backend | Deferred. | Start only after D3D12 composition contracts and project/layer boundaries are stable. |

---

## Dependency Graph

```text
Completed private checkpoints + renderer foundation
  ├─ POST-021..023 local gates and source boundaries
  ├─ POST-040..045 post-foundation style/animation/GPU composition design and D3D12-first spike
  │    └─ POST-043 scroll compositor design before scroll compositor code
  ├─ POST-024..026 completed/held framework promotion boundaries
  │    ├─ POST-027 scroll ownership
  │    └─ POST-028 settings provider
  ├─ POST-029 input/control projection
  ├─ POST-030..032 allocation follow-up hold
  └─ POST-033..036 glyph atlas follow-up
       └─ POST-035 entry eviction only after retained atlas command ownership is explicit
```

---

## Explicit Non-Goals

- Final text composition stays on the D3D12 GlyphAtlas path.
- No unguarded glyph script/format coverage expansion; new coverage needs matching oracle/regression coverage.
- No entry LRU or sub-rect free-list implementation before the ownership contract exists.
- No pixel oracle or DirectWrite layout renderer as a blocker for the current foundation.
- No retained-array, snapshot, or tree-builder allocation optimization without an ownership design plus attribution.
- No compatibility-driven public API expansion or code migration from `Irix.Poc` without a written contract.
- No compositor/runtime code extraction before the style, animation, and composition design gate is satisfied.
- No generic CPU/compatibility compositor as the first implementation route; use existing normal frame rendering as the explicit secondary path only when the D3D12-first path hits a written blocker.
- No Vulkan/Metal backend implementation in the current planning phase.
