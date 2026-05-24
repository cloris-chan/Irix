# Irix Project Status

> Current developer handoff note for the Irix Windows PoC.
> Last verified: 2026-05-24.

---

## Canonical Docs

| Doc | Purpose |
|-----|---------|
| [Irix_Framework_Design.md](Irix_Framework_Design.md) | Main architecture design, layer boundaries, phase boundaries, ADR index, and v1/vNext scope. |
| [GA-Hardening-Plan.md](GA-Hardening-Plan.md) | Current private-GA baseline, guarded invariants, and accepted constraints. |
| [Post-V1-MVP-Backlog.md](Post-V1-MVP-Backlog.md) | Remaining post-foundation backlog and framework-promotion candidates. |
| [Poc-Promotion-Contracts.md](Poc-Promotion-Contracts.md) | Promotion contracts for `Irix.Poc` runtime adapters, Windows backend adapters, and legacy/debug presentation paths. |
| [Glyph-Atlas-Post-GA-Design.md](Glyph-Atlas-Post-GA-Design.md) | D3D12-only glyph atlas text renderer design, degradation policy, and coverage freeze. |
| [Glyph-Atlas-Regression-Matrix-Evidence-2026-05-23.md](Glyph-Atlas-Regression-Matrix-Evidence-2026-05-23.md) | Local fixed regression matrix and Smoke guard evidence. |
| [Glyph-Atlas-Soak-Memory-Pressure-Evidence-2026-05-23.md](Glyph-Atlas-Soak-Memory-Pressure-Evidence-2026-05-23.md) | Local long soak, memory pressure, page reuse, resident-byte, and fragmentation evidence. |
| [Glyph-Atlas-Color-Format-Smoke-Evidence-2026-05-23.md](Glyph-Atlas-Color-Format-Smoke-Evidence-2026-05-23.md) | Local color glyph layer/bitmap route and WIC decode evidence. |
| [Glyph-Atlas-BiDi-Oracle-Evidence-2026-05-23.md](Glyph-Atlas-BiDi-Oracle-Evidence-2026-05-23.md) | DirectWrite analyzer-level BiDi resolved-level oracle evidence. |
| [Glyph-Atlas-Glyph-Oracle-Evidence-2026-05-23.md](Glyph-Atlas-Glyph-Oracle-Evidence-2026-05-23.md) | Diagnostic-only DirectWrite shaping/layout-data oracle evidence. |
| [Glyph-Atlas-Entry-Eviction-Design.md](Glyph-Atlas-Entry-Eviction-Design.md) | Design-only preconditions and non-goals for future entry-level LRU / sub-rect free-list work. |
| [ADR-Scissor-Clipping-v0.md](ADR-Scissor-Clipping-v0.md) | Clip/scissor/text-clip v0 decision and frozen behavior. |
| [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md) | Layout dirty classification and StyleOnly planning boundary. |
| [Diagnostics-Snapshot-v0.md](Diagnostics-Snapshot-v0.md) | Diagnostics snapshot v0 boundary and formatter contract. |

Historical one-off smoke/default evidence that is superseded by the fixed regression matrix, soak, color-format, oracle, and status docs should not be reintroduced.

---

## Current State

GitHub Actions quota is currently exhausted. `.github/workflows/ci.yml` remains configured, but Actions status is not the source of truth until quota returns. Use local gates:

- Before and after broad changes: `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke`.
- After glyph/page/shaping changes: `.\scripts\glyph-atlas-regression.ps1 -Mode Local`.
- After page-policy, eviction, or shaping overhauls: manually run `.\scripts\glyph-atlas-regression.ps1 -Mode Nightly`.
- Keep local `TestResults\glyph-atlas-regression-*-*.guard.summary.txt` as the active glyph-atlas status source.

| Area | Status |
|------|--------|
| Branch state | `post-ga-renderer-foundation` has been fast-forward merged to `master`. The closeout commit is `7659a15 Document renderer foundation closeout gate`. |
| V1 core | Complete / regression-only. Do not reopen core feature scope for GA cleanup. |
| Windows backend | D3D12 is the active v1 Windows PoC backend. GlyphAtlas is the only active text composition path; D3D11On12 / Direct2D final overlay, sync strategy, explicit overlay mode, and legacy overlay CLI alias are removed. DirectWrite and WIC are allowed only as shaping, metrics, raster, and image source-data paths. |
| Glyph atlas | Coverage surface is frozen until oracle/regression split is stable. Allowed work is bugfix, guard, diagnostic, test, documentation, and local evidence updates. SVG/COLR paint-tree-only glyphs, BiDi beyond current resolved-level projection, unsafe AtlasFull cases, and init/record failure remain explicit D3D12-only degradation. |
| Text/value IR | Framework/core paths must not retain raw text strings. Retained and drawing paths use `TextNodeContent`, `TextBufferSnapshot`, `FrameTextArena`, `TextSlice`, and resolver boundaries. CLI/debug/report formatting strings are output-boundary exceptions. |
| Architecture boundary | `Irix.Poc` is an app, diagnostics, and adapter-glue project. It is not the reusable framework home. `D3D12DrawingBackend` has moved to `Irix.Platform.Windows`; `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` have moved to `Irix.Rendering`; `WindowDrawCommandTranslator` remains the Poc adapter around viewport timing, app/control scroll feedback, diagnostics, allocation attribution, and Counter default composition. `WindowBackend` stays as legacy/debug presentation. |
| Performance | Pre-GA micro-optimization is closed. Latest `--diagnose-text-cache 180` warm scroll baseline is `394216 bytes`, `2190 B/frame`: top level `tree=546`, `diff=273`, `translate=1138`, `render=273` B/frame; pipeline `layout=501`, `record=45`, `hitTargets=136`, `snapshot=273`, `retainedFrame=45` B/frame. Record allocation is not the next target. |
| Docs | Current docs should describe active architecture and remaining work, not post-GA process history. |

---

## Active Source Guards

Keep guards green for these non-negotiable boundaries:

- No D3D11On12 / Direct2D final composition, factory/device/context path, `TextOverlaySyncStrategy`, `D3D12TextRenderer`, `IDWriteTextLayout`, or runtime shader compile in active renderer source.
- No raw retained text string path in framework/core rendering or layout state.
- No public string style/value factory path, legacy attribute-era API, public `VirtualNodeProperty` construction, silent `PropertyValue` getters, ref-struct leakage into retained/batch state, or framework/backend `record struct` outside the existing PoC authoring exception.

---

## Next Reasonable Work

| Priority | Work | Boundary |
|----------|------|----------|
| P0 | Keep local Smoke gate authoritative | Run `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke` for broad changes while Actions quota is unavailable. |
| P1 | Text-cache allocation hardening | Return to measurement-led allocation work. Start from the largest attributed tree/layout/snapshot buckets, not glyph renderer or draw recording. |
| P1 | Framework promotion hold | `D3D12DrawingBackend` and translator core promotion are complete. Do not move `WindowBackend`; do not split scroll/input until a scroll ownership contract exists. |
| P1 | Glyph atlas maintenance | Stay within the coverage freeze: bugfixes, guards, diagnostics, and tests only unless a matching oracle/regression case is added first. |
| P2 | Entry-level eviction | Keep design-only until retained atlas command ownership and sub-rect free-list ownership are explicit. |
| P2 | Pixel/layout oracle | Separate future work; do not block the current D3D12-only renderer foundation. |
