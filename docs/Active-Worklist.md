# Active Worklist

> Current private work items. This file is not a public delivery plan, compatibility contract, or product schedule.

---

## Windows Boundary

The Windows PoC separates target framework, CI toolchain check, and runtime minimum. Windows-targeted projects inherit `IrixWindowsTargetFramework=net10.0-windows10.0.26100.0` and `IrixWindowsSupportedOSPlatformVersion=10.0.15063.0` from `Directory.Build.props`; local builds rely on the .NET SDK to resolve a compatible installed Windows SDK such as 26100 or 28000 and report missing SDKs. The target framework stays on 26100 because .NET SDK 10.0.300 does not yet provide `Microsoft.Windows.SDK.NET.Ref` / TFM support for 28000, and CI verifies that Windows SDK 26100 or newer is installed instead of requiring one exact SDK directory. The 10.0.15063.0 runtime floor is intentional for PerMonitorV2 DPI awareness and display scale support. `IDWriteFactory4` is available within this runtime target and is the baseline DirectWrite factory for the glyph atlas path.

---

## Completed Context

- D3D12 GlyphAtlas text composition is the active Windows text path. DirectWrite/WIC remain source-data providers only.
- `D3D12DrawingBackend` and helper structs live in `Irix.Platform.Windows`.
- `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` live in `Irix.Rendering`; `WindowDrawCommandTranslator` remains Poc glue.
- Glyph entries and atlas pages use stable value handles with generations; page reuse is format-scoped and retained-floor-gated.
- Text-cache allocation attribution exists as optional diagnostics. Further tree/layout/snapshot allocation work is paused until an ownership design names one target bucket.
- Style, animation, and GPU composition contracts exist. The active implementation direction is D3D12-backed transform/opacity, fixed-clip scroll presentation, retained target resolution, marker event publication, multi-layer composition execution, active hit-test remapping, main-app scroll presentation production, compositor-thread scroll retarget sample-and-cancel, reason-typed lifecycle invalidation/cancellation, and D3D12 layer content caching. Recent hardening covers fixed-clip/nested/sibling-layer hit-test remapping, mapped/unmapped marker runtime dispatch, superseded/sample-and-cancel scroll-presentation idle waiters, lifecycle stale queued tick suppression, scroll marker cancellation queue cleanup, and fixed-clip layer-cache hit/empty-intersection behavior.
- The scroll/input runtime owner boundary is written in `Poc-Promotion-Contracts.md`: logical scroll and input ownership belong to app/control runtime; layout feedback, compositor sampling, hit-test service access, input action mapping, and app message dispatch each require explicit adapter contracts before code moves. The first Poc-owned feedback sink, max-scroll feedback dispatch mapper, hit-test service, input action mapper, input-route app-message dispatch mapper, wheel-input scroll presentation dispatch sink, app runtime dispatch sink, and marker-runtime dispatch-through-sink cuts exist.
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
Push/PR CI intentionally stays lightweight while quota is constrained; run the manual `glyph-atlas-smoke` workflow_dispatch suite only when remote confirmation is worth the cost.

### Near-Term Plan

| Order | Work | Acceptance |
|-------|------|------------|
| 1 | Scroll presentation continuity | Preserve the compositor-thread sample-and-cancel retarget invariant: same-target boundary input must not cancel/restart, replacement segments must start from the latest compositor-presented value, sample-and-cancel must complete existing idle waiters, and stale delayed ticks from canceled or replaced segments must not queue after the schedule changes. Keep the sample/cancel, stale delayed tick skip, idle waiter, and boundary coordinator tests green. |
| 2 | Main-app lifecycle matrix | Extend only narrow coverage around concrete bugs in resize, DPI, max-scroll feedback, render invalidation, queued compositor ticks, marker publication, and active hit-test routing. Render invalidation stale delayed tick cancellation has focused compositor-loop coverage; lifecycle scenarios now expose stale queued tick suppression without advancing composition/loop tick counts. Keep lifecycle scenarios diagnostic-visible with stable presented-origin continuity and no generic fallback compositor. |
| 3 | Scroll/input ownership contract | Boundary is written in [Poc-Promotion-Contracts.md](Poc-Promotion-Contracts.md). The first Poc-owned scroll presentation adapter cut exists for runtime dispatch, compositor sampling/start, retained-snapshot access, control feedback delivery, max-scroll feedback message mapping, wheel-input scroll presentation dispatch, app runtime dispatch, and marker-runtime dispatch through that sink; the input route now consumes Poc-owned hit-test service, input action mapper, and app-message dispatch mapper shapes for resolved action activation. Next code moves must return to concrete scroll-presentation continuity and lifecycle bugs rather than moving Counter/Poc runtime types wholesale. |
| 4 | D3D12 direct composition hardening | Keep transform/opacity, fixed-clip scroll, nested/mixed-clip decomposition, marker dispatch, layer content cache, and skip diagnostics on the D3D12-first path. Recent boundary coverage pins fixed-clip/nested/sibling-layer hit-test remapping, mapped/unmapped marker runtime dispatch, scroll marker cancellation queue cleanup, and fixed-clip cache-hit state. Add more tests only for concrete behavior boundaries, especially marker/skip interactions or main-app integration regressions. |
| 5 | Glyph and allocation discipline | Run glyph Smoke for broad rendering changes. Keep allocation optimization paused unless a written ownership design names one measured bucket and proves retained publication safety. |

### Architecture And Composition

| Task | Current state | Acceptance |
|------|---------------|------------|
| Style system | [Style-System.md](Style-System.md) defines layout, visual, text shaping, composition, and control-state style layers. | Implement against an explicit target layer and invalidation rule; do not add generic compatibility style layers. |
| Animation/composition | [Animation-Composition.md](Animation-Composition.md) defines UI-runtime, compositor, hybrid, and backend-internal animation classes. Runtime transform/opacity and fixed-clip scroll declarations resolve retained `NodeKey` targets into compositor ticks. Marker dispatch, scroll interruption policy, live wheel retarget with target probing plus compositor-thread sample-and-cancel, main-app scroll presentation, reason-typed invalidation cancellation, superseded and sample-and-cancel idle waiter completion, scroll runtime cancellation matrix diagnostics, active scroll hit-test/press diagnostics, main-app scroll interaction diagnostics including `EnsureRunning` rapid wheel coalescing, resize/DPI/viewport/tree/layout/text/max-scroll lifecycle cancellation, and stale queued tick suppression, plus skipped-compositor diagnostics exist. | Keep broad app integration on the D3D12-first path; harden cancellation/retarget/interaction semantics when bugs appear. |
| GPU composition architecture | [GPU-Composition-Architecture.md](GPU-Composition-Architecture.md) defines IR, capabilities, GPU offload order, and backend mapping notes. D3D12 transform/opacity, fixed-clip scroll, runtime marker dispatch, marker queue cleanup on scroll cancellation, multi-layer frame execution, nested/mixed-clip decomposition, main-app scroll presentation, reason-typed scroll lifecycle cancellation, layer content caching, and skip diagnostics exist. | Continue D3D12-first. Do not start Vulkan/Metal before the active backend proves those contracts. |
| Scroll compositor | Fixed-clip scroll presentation is implemented for single-layer and decomposed nested/mixed-clip retained targets. Logical scroll remains app/control runtime; presented scroll is compositor-owned, and retarget sample/cancel, cancellation reasons, plus main-app resize/DPI/viewport/tree/layout/text/max-scroll cancellation paths are diagnostic-visible. | Continue hardening interruption, lifecycle cancellation, and hit testing without a generic fallback compositor. |
| D3D12 composition execution | [D3D12-Composition.md](D3D12-Composition.md) covers animation declarations/plans, scroll presentation declarations/plans, compositor-owned ticks, hit-test remapping, marker events, runtime marker dispatch, marker queue cleanup on canceled scroll presentation, multi-layer frame execution, retained nested/mixed-clip decomposition, D3D12 layer content cache diagnostics including display-scale, resource-resolver, resource-frame reset invalidation, disjoint multi-layer hits, overlapping-layer direct fallback, fixed-clip cache-hit state, backend capabilities, skip diagnostics, typed scroll runtime cancellation diagnostics, and visible diagnostic commands. | Keep ticks independent of UI rebuild; expand direct-path coverage only when a concrete behavior needs it. |
| D3D12-first GPU offload | Active path is direct/layer-content D3D12 composition. Internal offscreen/render-target caching is not active because viewport-space caching broke fixed-clip scroll semantics. | Consider offscreen only after content-space bounds, origin, clip, invalidation, and hit-test semantics are designed and direct composition still needs it. |

### Framework Boundary

| Task | Current state | Acceptance |
|------|---------------|------------|
| Translator boundary | Core translator types live in `Irix.Rendering`. `WindowDrawCommandTranslator` remains in `Irix.Poc` because it composes viewport callbacks, app/control scroll feedback, diagnostics, optional allocation attribution, and Counter default style construction. | Do not move the outer adapter or make the core public without a new contract. |
| Windows drawing backend | `D3D12DrawingBackend` and helper structs live in `Irix.Platform.Windows`. | Preserve scissor/text clip/device recovery/scale diagnostics tests; do not move it back into `Irix.Poc`. |
| WindowBackend isolation | `WindowBackend` is a legacy/debug `INativeWindow.SetContent` presentation path. | Do not move it into `Irix.Rendering` or `Irix.Platform.Windows`; replace only if the legacy/debug path becomes unnecessary. |
| Scroll ownership | `ScrollDiagnostics` is layout observation; `ScrollFeedback` is app/control feedback; `ScrollController`, `ScrollState`, `ScrollFramePump`, and `ScrollPresentationCoordinator` remain app runtime state in `Irix.Poc`. The scroll/input runtime owner boundary is written, and `ScrollPresentationCoordinator` now consumes Poc-owned runtime, compositor, and retained-snapshot adapter shapes instead of directly depending on the compositor loop and translator inside its core retarget path. `WindowDrawCommandTranslator` now delivers projected feedback through a Poc-owned `IControlFeedbackSink`. | Do not move scroll runtime code until a separate extraction commit introduces broader app-message dispatch and framework runtime owner contracts. |
| Settings provider | Runtime settings wiring remains postponed. | Add only after a concrete app/framework boundary is written; keep the internal default provider until then. |
| Input/control projection | `InputOwnershipState`, `ControlVisualState*`, and `ActionHitTestResolver` remain Counter/Poc runtime projection. `InputOwnershipState` now consumes a Poc-owned `IInputHitTestService`, and `CounterInputRouter` has Poc-owned input action and app-message dispatch mapper boundaries for resolved `ActionId` activation. The owner boundary still requires broader dispatch coverage before extraction. | Do not promote input/control code without replacing Counter-specific action identity and single-pointer assumptions. |

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
