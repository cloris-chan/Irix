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
| Performance baseline | Split frame-stage allocation guards and `--diagnose-text-cache` attribution exist. Layout attribution now shows `pipeline.layout` is mostly published arrays/result shell, not node-walk/property-read/clip propagation. Record allocation is low. |

---

## Active Backlog

### P0 - Local Gate Discipline

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-021 | Local Smoke gate | GitHub Actions quota is exhausted, so local `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke` is the broad-change gate. | Guard `status=Passed`; matrix `degradedRuns=0`, `glyphAtlasInitialized=True`, `overlaySync=False`; soak `deviceLost=False`, `syncWaits=0`, `hardFullWithoutReuse=0`, `RecordFailed=0`. |
| POST-022 | Coverage freeze | Glyph atlas script/format support is frozen until oracle/regression split is stable. | Only bugfix, guard, diagnostic, test, documentation, or evidence updates unless new coverage has a matching oracle/regression case. |
| POST-023 | Source boundary guards | Active guards block overlay revival, runtime shader compile, `IDWriteTextLayout`, and raw retained text strings in framework/core paths. | Guards stay green after renderer, text, or diagnostics changes. |

Run `Smoke` before/after broad changes. Do not add artifact-upload work until Actions quota returns.

### P1 - Framework Boundary / Promotion

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-024 | Keep translator core in Rendering and outer adapter in Poc | `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` now live in `Irix.Rendering`. `WindowDrawCommandTranslator` remains in `Irix.Poc` because it composes viewport callbacks, app/control scroll feedback, diagnostics, allocation attribution, and Counter default style construction. | Do not move the outer adapter or make the core public without a new contract. |
| POST-025 | Keep `D3D12DrawingBackend` in Windows platform | Backend and helper structs now live in `Irix.Platform.Windows`. | Preserve scissor/text clip/device recovery/scale diagnostics tests; do not move it back into `Irix.Poc`. |
| POST-026 | Keep `WindowBackend` isolated | Contract decision is stay: it is a legacy/debug `INativeWindow.SetContent` presentation path. | Do not move it into `Irix.Rendering` or `Irix.Platform.Windows`; replace only if the legacy/debug path becomes unnecessary. |
| POST-027 | Scroll extraction hold | Scroll feedback is app/control feedback derived from layout diagnostics; scroll state/pump remains in `Irix.Poc`. | Do not split scroll/input until a scroll ownership contract exists. |
| POST-028 | Settings provider | Runtime settings wiring remains postponed. | Add only after a concrete app/framework boundary is written; keep fallback-only internal provider until then. |

### P1 - Measurement-Led Optimization

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-029 | Tree/layout/snapshot allocation pass | Latest `--diagnose-text-cache 180` warm scroll sample is about `2204 B/frame`; `tree.buildRoot=546` B/frame, split mostly into `buttons=318` and `container=227`; text/property/children are currently 0 B/frame in the sample. `pipeline.layout=501` B/frame and `layout.nodeWalk=0`. Record is `45 B/frame`. | One focused change per measured bucket, with updated attribution. Do not optimize glyph renderer or `DrawCommandRecorder` first unless new evidence changes the profile. |
| POST-030 | Layout builder scratch ownership | Layout full/dirty allocation is stable but still visible in attribution. | Scratch lifetime and pooling design does not leak retained state, stack memory, or rented arrays. |
| POST-031 | Retained snapshot boundary review | Snapshot copy allocation is visible but not dominant. | Any reuse design preserves `TextSlice`/resource snapshot validity and cross-frame ownership rules. |

Layout publication note: main layout arrays are retained publication state, not disposable same-frame scratch.

`Elements`, `TreeNodes`, `DirtyElementRanges`, and `ScrollDiagnostics` are retained by `LastLayoutResult` / `LastRetainedInputSnapshot`.

Safe reuse is limited to empty/static collections or same-frame scratch that is copied before publication.

### P1 / P2 - Glyph Atlas Follow-Up

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-032 | Local/Manual soak cadence | `Smoke` is routine; `Local` after glyph/page/shaping changes; `Nightly` after page-policy, eviction, or shaping overhauls. | Evidence is updated only when results materially change or cover a representative change. |
| POST-033 | BGRA/TIFF natural font coverage | Default local Segoe UI Emoji naturally covers COLR/layer; Noto probes naturally cover PNG image data. Direct BGRA/TIFF natural font coverage remains unavailable locally. | Add evidence if a real font/environment exposes direct BGRA or TIFF; otherwise keep code-level selector/raster/decode/page-format tests. |
| POST-034 | Entry-level eviction design | Design exists; implementation is blocked. | Do not implement entry LRU/sub-rect free-list until retained atlas command ownership exposes an oldest retained atlas record serial and page-local free-list ownership is explicit. |
| POST-035 | Pixel/layout oracle | Structural DirectWrite analyzer/glyph oracles exist; pixel/layout oracle is deferred. | Future work remains separate from current regression matrix and does not reintroduce D2D overlay rendering. |

### P2 - Deferred Architecture

| ID | Task | Current status | Acceptance |
|----|------|----------------|------------|
| POST-036 | Unified diagnostics channel | Postponed. | Replace per-component diagnostics only after current snapshots prove insufficient. |
| POST-037 | StyleOnly layout skip | Design only. | Implement after layout dirty ownership and retained-frame semantics are stable. |
| POST-038 | Second graphics backend | Deferred. | Start only after D3D12 contracts and project/layer boundaries are stable. |

---

## Dependency Graph

```text
Completed Private GA + Renderer foundation
  ├─ POST-021..023 local gates and source boundaries
  ├─ POST-024..026 completed/held framework promotion boundaries
  │    ├─ POST-027 scroll extraction
  │    └─ POST-028 settings provider
  ├─ POST-029..031 measured allocation work
  └─ POST-032..035 glyph atlas follow-up
       └─ POST-034 entry eviction only after retained atlas command ownership is explicit
```

---

## Explicit Non-Goals

- No D3D11On12 / Direct2D final text overlay revival.
- No hidden overlay CLI compatibility alias.
- No glyph script/format coverage expansion during the coverage freeze without matching oracle/regression coverage.
- No entry LRU or sub-rect free-list implementation before the ownership contract exists.
- No pixel oracle or DirectWrite layout renderer as a blocker for the current foundation.
- No allocation optimization without attribution.
- No public API expansion or code migration from `Irix.Poc` without a written contract.
