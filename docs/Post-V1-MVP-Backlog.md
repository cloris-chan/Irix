# Post-V1 / MVP Backlog

> V1 core and the post-GA renderer foundation are complete for the current private repository milestone. This document tracks only remaining work. Completed history is compressed so the backlog stays actionable.

---

## Windows Version Boundary

Irix v1 Windows PoC separates target SDK from runtime minimum. Windows-targeted projects inherit `IrixWindowsTargetFramework=net10.0-windows10.0.26100.0` and `IrixWindowsSupportedOSPlatformVersion=10.0.15063.0` from `Directory.Build.props`; CI checks for .NET 10 and Windows SDK 10.0.26100.0 before restore/build. The 10.0.15063.0 runtime floor is intentional for PerMonitorV2 DPI awareness and display scale support. `IDWriteFactory4` is available within this runtime target and is the baseline DirectWrite factory for the glyph atlas path.

---

## Completed Milestones

| Milestone | Summary |
|-----------|---------|
| Private GA | Tagged as `v1.0-private-ga`; default-on partial apply, D3D12 segmented ownership, device-lost recovery, platform smokes, Windows SDK CI checks, performance guards, GPU memory pressure diagnostics, and command allocator reset failure handling are complete for the private milestone. |
| Renderer foundation | D3D12-only GlyphAtlas text composition is default. D3D11On12 / Direct2D overlay final composition, sync strategy, explicit overlay mode, native generation entries, and legacy overlay CLI alias are removed. DirectWrite/WIC remain source-data paths only. Regression matrix, soak, color-format, BiDi oracle, glyph oracle, and entry-eviction design docs are the active evidence set. |
| Windows backend promotion | `D3D12DrawingBackend` and its helper structs moved from `Irix.Poc` to `Irix.Platform.Windows` without renderer behavior changes. |
| Translator core promotion | `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` moved from `Irix.Poc` to `Irix.Rendering`; `WindowDrawCommandTranslator` remains Poc glue for viewport timing, app/control feedback, diagnostics, allocation attribution, and Counter default composition. |
| Resource cache / stable handles | Glyph entries and atlas pages use stable value handles with generations. The atlas grows on demand to a bounded 48-page budget, tracks Alpha/Bgra page formats, reports resident bytes and fragmentation, and supports format-scoped retained-floor-gated page reuse. |
| Performance allocation phase | Split frame-stage allocation guards and `--diagnose-text-cache` attribution exist. Layout attribution now shows `pipeline.layout` is mostly published arrays/result shell, not node-walk/property-read/clip propagation. Safe empty publication array reuse is complete. Further tree/layout/snapshot optimization is paused until ownership design exists. |
| Post-foundation design start | Style, animation, and GPU composition design contracts now exist as design-only docs. No runtime/compositor code extraction is implied by those docs. |

---

## Active Backlog

### P0 - Local Gate Discipline

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-021 | Local Smoke gate | GitHub Actions quota is exhausted, so local `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke` is the broad-change gate. | Guard `status=Passed`; matrix `degradedRuns=0`, `glyphAtlasInitialized=True`, `overlaySync=False`; soak `deviceLost=False`, `syncWaits=0`, `hardFullWithoutReuse=0`, `RecordFailed=0`. |
| POST-022 | Coverage freeze | Glyph atlas script/format support is frozen until oracle/regression split is stable. | Only bugfix, guard, diagnostic, test, documentation, or evidence updates unless new coverage has a matching oracle/regression case. |
| POST-023 | Source boundary guards | Active guards block overlay revival, runtime shader compile, `IDWriteTextLayout`, raw retained text strings, and premature compositor/runtime extraction. | Guards stay green after renderer, text, diagnostics, or architecture-boundary changes. |

Run `Smoke` before/after broad changes. Do not add artifact-upload work until Actions quota returns.

### P1 - Post-Foundation Architecture Design

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-040 | Style system v0 | `Style-System-v0.md` defines layout, visual, text shaping, composition, and control-state style layers. | Keep code changes blocked until a target layer and invalidation rule are explicitly chosen. |
| POST-041 | Animation/composition v0 | `Animation-Composition-v0.md` defines UI-runtime, compositor, hybrid, and backend-internal animation classes. Scroll is marked as the first future hybrid case. | Do not implement animation scheduler or scroll compositor until the first narrow implementation case is selected. |
| POST-042 | GPU composition architecture v0 | `GPU-Composition-Architecture-v0.md` defines future composition IR, backend capabilities, GPU offload phases, and D3D12/Vulkan/Metal mapping notes. | Do not start Vulkan/Metal or advanced GPU paths until the D3D12 composition contract is stable. |
| POST-043 | Scroll compositor design | Not yet written. Current docs say logical scroll remains app/control runtime and presented scroll should become compositor animation. | Write a dedicated design before moving scroll code or adding compositor scroll behavior. |
| POST-044 | Composition IR implementation gate | Not started. | First code change must be minimal, D3D12-backed, and should not alter current draw command renderer behavior without tests. |

### P1 - Framework Boundary / Promotion

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-024 | Keep translator core in Rendering and outer adapter in Poc | `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` now live in `Irix.Rendering`. `WindowDrawCommandTranslator` remains in `Irix.Poc` because it composes viewport callbacks, app/control scroll feedback, diagnostics, allocation attribution, and Counter default style construction. | Do not move the outer adapter or make the core public without a new contract. |
| POST-025 | Keep `D3D12DrawingBackend` in Windows platform | Backend and helper structs now live in `Irix.Platform.Windows`. | Preserve scissor/text clip/device recovery/scale diagnostics tests; do not move it back into `Irix.Poc`. |
| POST-026 | Keep `WindowBackend` isolated | Contract decision is stay: it is a legacy/debug `INativeWindow.SetContent` presentation path. | Do not move it into `Irix.Rendering` or `Irix.Platform.Windows`; replace only if the legacy/debug path becomes unnecessary. |
| POST-027 | Scroll ownership contract | Written as a documentation boundary. `ScrollDiagnostics` is layout observation; `ScrollFeedback` is app/control feedback; `ScrollController`, `ScrollState`, and `ScrollFramePump` remain app runtime state in `Irix.Poc`. | Do not move scroll runtime code until a separate extraction commit chooses a framework runtime owner. |
| POST-028 | Settings provider | Runtime settings wiring remains postponed. | Add only after a concrete app/framework boundary is written; keep fallback-only internal provider until then. |
| POST-029 | Input/control projection contract | Written as a documentation boundary. `InputOwnershipState`, `ControlVisualState*`, and `ActionHitTestResolver` remain Counter/Poc runtime projection for now. | Do not promote input/control code without replacing Counter-specific action mapping and single-pointer assumptions. |

### P1 - Allocation Follow-Up Hold

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-030 | Allocation phase closeout | Latest `--diagnose-text-cache 180` warm scroll sample is the comparison baseline: about `2204 B/frame`; `tree.buildRoot=546` B/frame, `pipeline.layout=501` B/frame, `layout.nodeWalk=0`, record `45 B/frame`. Safe empty publication array reuse is the only optimization from this phase. | Do not continue small fixes against retained arrays or snapshots. Reopen only with an ownership design and one measured bucket. |
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
| POST-035 | Entry-level eviction design | Design exists; implementation is blocked. | Do not implement entry LRU/sub-rect free-list until retained atlas command ownership exposes an oldest retained atlas record serial and page-local free-list ownership is explicit. |
| POST-036 | Pixel/layout oracle | Structural DirectWrite analyzer/glyph oracles exist; pixel/layout oracle is deferred. | Future work remains separate from current regression matrix and does not reintroduce D2D overlay rendering. |

### P2 - Deferred Architecture

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-037 | Unified diagnostics channel | Postponed. | Replace per-component diagnostics only after current snapshots prove insufficient. |
| POST-038 | StyleOnly layout skip | Design only. | Implement after style layer classification, layout dirty ownership, and retained-frame semantics are stable. |
| POST-039 | Second graphics backend | Deferred. | Start only after D3D12 composition contracts and project/layer boundaries are stable. |

---

## Dependency Graph

```text
Completed Private GA + Renderer foundation
  ├─ POST-021..023 local gates and source boundaries
  ├─ POST-040..044 post-foundation style/animation/GPU composition design
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

- No D3D11On12 / Direct2D final text overlay revival.
- No hidden overlay CLI compatibility alias.
- No glyph script/format coverage expansion during the coverage freeze without matching oracle/regression coverage.
- No entry LRU or sub-rect free-list implementation before the ownership contract exists.
- No pixel oracle or DirectWrite layout renderer as a blocker for the current foundation.
- No retained-array, snapshot, or tree-builder allocation optimization without an ownership design plus attribution.
- No public API expansion or code migration from `Irix.Poc` without a written contract.
- No compositor/runtime code extraction before the style, animation, and composition design gate is satisfied.
- No Vulkan/Metal backend implementation in the current planning phase.
