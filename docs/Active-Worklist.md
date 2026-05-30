# Active Worklist

> Current private work items. This file is not a public delivery plan, compatibility contract, or product schedule.

---

## Windows Boundary

The Windows PoC separates target SDK from runtime minimum. Windows-targeted projects inherit `IrixWindowsTargetFramework=net10.0-windows10.0.26100.0` and `IrixWindowsSupportedOSPlatformVersion=10.0.15063.0` from `Directory.Build.props`; CI checks for .NET 10 and Windows SDK 10.0.26100.0 before restore/build. The 10.0.15063.0 runtime floor is intentional for PerMonitorV2 DPI awareness and display scale support. `IDWriteFactory4` is available within this runtime target and is the baseline DirectWrite factory for the glyph atlas path.

---

## Completed Context

- D3D12 GlyphAtlas text composition is the active Windows text path. DirectWrite/WIC remain source-data providers only.
- `D3D12DrawingBackend` and helper structs live in `Irix.Platform.Windows`.
- `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` live in `Irix.Rendering`; `WindowDrawCommandTranslator` remains Poc glue.
- Glyph entries and atlas pages use stable value handles with generations; page reuse is format-scoped and retained-floor-gated.
- Text-cache allocation attribution exists as optional diagnostics. Further tree/layout/snapshot allocation work is paused until an ownership design names one target bucket.
- Style, animation, and GPU composition contracts exist. The active implementation direction is D3D12-backed transform/opacity, fixed-clip scroll presentation, retained target resolution, marker event publication, multi-layer composition execution, active hit-test remapping, main-app scroll presentation production, lifecycle invalidation/cancellation, and D3D12 layer content caching.
- Viewport-space internal offscreen caching was removed from the mainline. Secondary paths are reserved for written blockers.

---

## Current Work

### Local Gates

| Task | Current state | Acceptance |
|------|---------------|------------|
| Local Smoke gate | GitHub Actions quota is exhausted, so local `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke` is the broad-change gate. | Guard `status=Passed`; matrix `degradedRuns=0`, `glyphAtlasInitialized=True`, `finalComposition=D3D12`; soak `deviceLost=False`, `syncWaits=0`, `hardFullWithoutReuse=0`, `RecordFailed=0`. |
| Guarded glyph coverage | Glyph atlas script/format support is guard-gated by oracle/regression/degradation coverage. | Add new coverage only when it has matching oracle/regression coverage; reject unguarded coverage drift. |
| Source boundary guards | Active guards keep final text composition on D3D12 GlyphAtlas, block runtime shader compile, block raw retained text strings, and prevent premature compositor/runtime extraction. | Guards stay green after renderer, text, diagnostics, or architecture-boundary changes. |

Run `Smoke` before/after broad changes. Do not add artifact-upload work until Actions quota returns.

### Architecture And Composition

| Task | Current state | Acceptance |
|------|---------------|------------|
| Style system | [Style-System.md](Style-System.md) defines layout, visual, text shaping, composition, and control-state style layers. | Implement against an explicit target layer and invalidation rule; do not add generic compatibility style layers. |
| Animation/composition | [Animation-Composition.md](Animation-Composition.md) defines UI-runtime, compositor, hybrid, and backend-internal animation classes. Runtime transform/opacity and fixed-clip scroll declarations resolve retained `NodeKey` targets into compositor ticks. Marker dispatch, scroll interruption policy, live wheel retarget, main-app scroll presentation, and invalidation cancellation exist. | Keep skipped-compositor diagnostics explicit before broader app integration. |
| GPU composition architecture | [GPU-Composition-Architecture.md](GPU-Composition-Architecture.md) defines IR, capabilities, GPU offload order, and backend mapping notes. D3D12 transform/opacity, fixed-clip scroll, runtime marker dispatch, multi-layer frame execution, nested/mixed-clip decomposition, main-app scroll presentation, scroll lifecycle cancellation, and layer content caching exist. | Continue D3D12-first; add composition skip diagnostics next. Do not start Vulkan/Metal before the active backend proves those contracts. |
| Scroll compositor | Fixed-clip scroll presentation is implemented for single-layer and decomposed nested/mixed-clip retained targets. Logical scroll remains app/control runtime; presented scroll is compositor-owned. | Add skipped-compositor diagnostics before broad app integration. |
| D3D12 composition execution | [D3D12-Composition.md](D3D12-Composition.md) covers animation declarations/plans, scroll presentation declarations/plans, compositor-owned ticks, hit-test remapping, marker events, runtime marker dispatch, multi-layer frame execution, retained nested/mixed-clip decomposition, D3D12 layer content cache diagnostics, backend capabilities, and visible diagnostic commands. | Keep ticks independent of UI rebuild; make unsupported/skipped producer paths diagnostic-visible. |
| D3D12-first GPU offload | Active path is direct/layer-content D3D12 composition. Internal offscreen/render-target caching is not active because viewport-space caching broke fixed-clip scroll semantics. | Consider offscreen only after content-space bounds, origin, clip, invalidation, and hit-test semantics are designed and direct composition still needs it. |

### Framework Boundary

| Task | Current state | Acceptance |
|------|---------------|------------|
| Translator boundary | Core translator types live in `Irix.Rendering`. `WindowDrawCommandTranslator` remains in `Irix.Poc` because it composes viewport callbacks, app/control scroll feedback, diagnostics, optional allocation attribution, and Counter default style construction. | Do not move the outer adapter or make the core public without a new contract. |
| Windows drawing backend | `D3D12DrawingBackend` and helper structs live in `Irix.Platform.Windows`. | Preserve scissor/text clip/device recovery/scale diagnostics tests; do not move it back into `Irix.Poc`. |
| WindowBackend isolation | `WindowBackend` is a legacy/debug `INativeWindow.SetContent` presentation path. | Do not move it into `Irix.Rendering` or `Irix.Platform.Windows`; replace only if the legacy/debug path becomes unnecessary. |
| Scroll ownership | `ScrollDiagnostics` is layout observation; `ScrollFeedback` is app/control feedback; `ScrollController`, `ScrollState`, and `ScrollFramePump` remain app runtime state in `Irix.Poc`. | Do not move scroll runtime code until a separate extraction commit chooses a framework runtime owner. |
| Settings provider | Runtime settings wiring remains postponed. | Add only after a concrete app/framework boundary is written; keep the internal default provider until then. |
| Input/control projection | `InputOwnershipState`, `ControlVisualState*`, and `ActionHitTestResolver` remain Counter/Poc runtime projection. | Do not promote input/control code without replacing Counter-specific action mapping and single-pointer assumptions. |

### Allocation Hold

| Task | Current state | Acceptance |
|------|---------------|------------|
| Allocation attribution baseline | Latest `--diagnose-text-cache 180` warm scroll sample is the comparison point: about `2204 B/frame`; `tree.buildRoot=546` B/frame, `pipeline.layout=501` B/frame, `layout.nodeWalk=0`, record `45 B/frame`. Safe empty publication array reuse is the only optimization from that work. | Reopen aggressively only with an ownership design and one measured target bucket. |
| Layout builder scratch ownership | Layout full/dirty allocation is stable but still visible in attribution. | Design first. Scratch lifetime and pooling must not leak retained state, stack memory, or rented arrays. |
| Retained snapshot boundary review | Snapshot copy allocation is visible but not dominant. | Design first. Any reuse design preserves `TextSlice`/resource snapshot validity and cross-frame ownership rules. |

Layout publication note: main layout arrays are retained publication state, not disposable same-frame scratch. `Elements`, `TreeNodes`, `DirtyElementRanges`, and `ScrollDiagnostics` are retained by `LastLayoutResult` / `LastRetainedInputSnapshot`. Safe reuse is limited to empty/static collections or same-frame scratch that is copied before publication.

### Glyph Atlas Follow-Up

| Task | Current state | Acceptance |
|------|---------------|------------|
| Local/manual soak cadence | `Smoke` is routine; `Local` after glyph/page/shaping changes; `Nightly` after page-policy, eviction, or shaping overhauls. | Evidence is updated only when results materially change or cover a representative change. |
| BGRA/TIFF natural font coverage | Default local Segoe UI Emoji naturally covers COLR/layer; Noto probes naturally cover PNG image data. Direct BGRA/TIFF natural font coverage remains unavailable locally. | Add evidence if a real font/environment exposes direct BGRA or TIFF; otherwise keep code-level selector/raster/decode/page-format tests. |
| Entry-level eviction design | Design exists; implementation is not selected until ownership is explicit. | Implement entry LRU/sub-rect free-list only after retained atlas command ownership exposes an oldest retained atlas record serial and page-local free-list ownership is explicit. |
| Pixel/layout oracle | Structural DirectWrite analyzer/glyph oracles exist; pixel/layout oracle is deferred. | Future work remains separate from current regression matrix and must validate the D3D12 atlas path. |

### Deferred Architecture

| Task | Current state | Acceptance |
|------|---------------|------------|
| Unified diagnostics channel | Postponed. | Replace per-component diagnostics only after current snapshots prove insufficient. |
| StyleOnly layout skip | Design only. | Implement after style layer classification, layout dirty ownership, and retained-frame semantics are stable. |
| Second graphics backend | Deferred. | Start only after D3D12 composition contracts and project/layer boundaries are stable. |

---

## Explicit Non-Goals

- Final text composition stays on the D3D12 GlyphAtlas path.
- No unguarded glyph script/format coverage expansion; new coverage needs matching oracle/regression coverage.
- No entry LRU or sub-rect free-list implementation before the ownership contract exists.
- No pixel oracle or DirectWrite layout renderer as a blocker for current D3D12 work.
- No retained-array, snapshot, or tree-builder allocation optimization without an ownership design plus attribution.
- No compatibility-driven public API expansion or code migration from `Irix.Poc` without a written contract.
- No compositor/runtime code extraction before the style, animation, and composition contracts are ready.
- No generic CPU/compatibility compositor as the first implementation route; use existing normal frame rendering as the explicit secondary path only when the D3D12-first path hits a written blocker.
- No Vulkan/Metal backend implementation in the current worklist.
