# Post-Foundation Roadmap

> Planning note for the architecture phase after the renderer-foundation, promotion, and allocation-attribution work.

## Current Baseline

The current baseline is stable enough to stop broad renderer migration and allocation spelunking:

- D3D12 is the only active Windows final-composition backend.
- GlyphAtlas is the only active text-composition path; DirectWrite/WIC remain source-data paths only.
- `D3D12DrawingBackend` has moved to `Irix.Platform.Windows`.
- `TranslatorCore`, `TranslatorInput`, `TranslatorOutput`, and `TranslatorRetainedState` have moved to `Irix.Rendering`.
- `WindowDrawCommandTranslator` remains PoC glue for viewport timing, app/control feedback, diagnostics, allocation attribution, and Counter default composition.
- Allocation measurement is paused for this stage. Remaining large buckets are retained/publication ownership costs, not accidental temporary garbage.

This phase should not reopen renderer migration, GlyphAtlas coverage, or retained-array allocation optimization without a specific contract.

## Next Architecture Tracks

| Track | Purpose | First artifact | Code movement |
|-------|---------|----------------|---------------|
| Style system | Define layout, visual, text, control-state, and composition style boundaries. | [Style-System-v0.md](Style-System-v0.md) | No initial code movement. |
| Animation system | Define UI-runtime animation vs compositor/GPU animation ownership. | [Animation-Composition-v0.md](Animation-Composition-v0.md) | Internal transform/opacity `CompositionAnimationPlan` exists; runtime declaration is next. |
| GPU composition | Define a platform-neutral composition model and drive implementation through D3D12/GPU-backed layer updates. | [GPU-Composition-Architecture-v0.md](GPU-Composition-Architecture-v0.md), [D3D12-Composition-Spike-v0.md](D3D12-Composition-Spike-v0.md) | D3D12-backed compositor tick for transform/opacity exists. |
| Scroll/compositor bridge | Use scroll as the first hybrid case: runtime owns logical target; compositor owns presented offset. | Future scroll compositor design | After layer identity and hit-test mapping are contracted. |
| Runtime/control extraction | Decide whether scroll/input/control state needs a new runtime/control package. | Future ownership design | Only after Counter-specific assumptions are removed. |

## Design Order

1. **Style system v0**: classify which properties affect layout, drawing, text shaping, control state, or compositor presentation.
2. **Animation/composition model**: use the property classification to decide which animations are UI-runtime animations and which can run independently in the compositor.
3. **GPU composition architecture**: define composition IR, layer boundaries, backend capability reporting, and GPU offload phases.
4. **Scroll compositor design**: specify logical vs presented scroll, hit-test mapping, invalidation, and compositor timeline ownership.
5. **D3D12-first implementation spike**: current transform/opacity tick is in place. Continue with stable retained layer identity, runtime animation declaration, then scroll presentation. Add fallback compatibility only when the D3D12 path exposes an explicit short-term blocker.

## Principles

- Prefer design contracts before code migration from `Irix.Poc`.
- Keep the first implementation GPU-first and D3D12-backed; do not spend the first implementation pass on a generic compatibility compositor.
- Do not add a cross-platform backend before the D3D12 composition contract proves the layer model against the active backend.
- Do not make every visual property a layout property. Layout invalidation and composition invalidation must be separate.
- Do not drive compositor-eligible animations by rebuilding `VirtualNode` / layout / draw commands every frame.
- Do not put app/control state inside `Irix.Rendering` just because layout produces observation data.
- Do not expose pooled mutable arrays or GPU transient buffers through retained publication contracts.

## Non-Goals For This Phase

- No Vulkan or Metal implementation.
- No full theme/resource dictionary implementation.
- No generalized animation public API.
- No generic CPU/compatibility compositor as the first implementation route.
- No new GlyphAtlas script/format expansion.
- No second text composition path.
- No retained-array pooling or snapshot reuse without ownership design.
- No `WindowBackend` promotion.
- No scroll/input code extraction until ownership contracts are ready.

## Code Extraction Gate

A type may move out of `Irix.Poc` only when all are true:

1. It has a written ownership contract.
2. It does not depend on `CounterApplication`, `CounterMessage`, `CounterStylePreset`, or Counter-specific `ActionId` routing.
3. It does not own Win32, D3D12, DirectWrite, WIC, or platform renderer resources unless the target is `Irix.Platform.Windows`.
4. It does not mutate app/control state from `Irix.Rendering`.
5. It has targeted tests and does not require broad behavior changes in the same commit.
6. The move is mechanical or has an explicit behavior-change acceptance plan.

## Local Gates

- Broad docs/architecture-only changes: markdown link check and targeted documentation review are sufficient.
- Runtime/control/rendering code changes: full `dotnet test --no-build -c Release --verbosity normal` after build.
- Glyph, page policy, shaping, or D3D12 renderer changes: run `scripts/glyph-atlas-regression.ps1 -Mode Smoke` locally while Actions quota is unavailable.
