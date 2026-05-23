# Diagnostics Snapshot v0

> v0 data boundary for CLI diagnostics and debug UI rows. This line is sealed; future work should fix regressions only unless a new diagnostics channel design is opened.

## Goal

Diagnostics snapshot v0 adds an internal data model behind existing stdout diagnostics and debug UI rows. It does not change the stable text output for `--diagnose*` or `--debug-ui`.

The model answers:

- Which snapshot type backs each diagnostics surface.
- Which component owns production of that snapshot.
- Which PoC-only static fields may be adapted temporarily.

## Non-Goals

- No new diagnostics surfaces.
- No unified diagnostics channel, event bus, registry, subscription API, or debug overlay replacement.
- No diagnostics runner migration.
- No scroll/input/clip/backend ownership move.
- No intentional CLI text change.
- No StyleOnly fast path in `RenderPipeline.Build`.

## Snapshot Surface

| Snapshot | Producer owner | Current consumers | Boundary |
|----------|----------------|-------------------|----------|
| `RenderingPipelineDiagnosticSnapshot` | `Irix.Rendering` counters from `RenderPipeline` / `DrawingBackendCompositor` | `--diagnose` compositor and layout pipeline blocks | Pipeline counters must come from pipeline/compositor state, not retained PoC statics. |
| `BackendClipTextDiagnosticSnapshot` | Backend adapter owner; currently `Irix.Poc.D3D12DrawingBackend` plus `D3D12Renderer` device state | `--diagnose` scissor/text clip smokes and debug clip row | Device errors remain typed until CLI/debug formatting. |
| `ViewportDiagnosticsSnapshot` | Platform/render bridge owner; current data crosses window, renderer, and translator | `--diagnose-resize` and debug viewport row | Dimensions must come from window/renderer/translator source-of-truth fields. |
| `ScrollDiagnosticsSnapshot` | Scroll service owner; currently PoC scroll pump/controller | `--diagnose-scroll` and debug scroll row | PoC statics are read-only v0 adapters until scroll ownership is extracted. |
| `InputDiagnosticsSnapshot` | Input routing/focus owner; currently PoC input ownership/router | `--diagnose-input` and debug input row | Ownership state is value data; diagnostics history remains bounded. |
| `StyleOnlyPatchPlanDiagnosticSnapshot` | `Irix.Rendering.StyleOnlyPatchPlanBuilder` | `--diagnose` style-only plan smoke | Planner data is explicit input/output and does not need PoC statics. |
| `DebugUiDiagnosticsSnapshot` | `DefaultDebugDiagnosticsSnapshotBridge` | Current debug header rows | Bridge is read-only and local; it must not mutate scroll/input/backend state. |

## Text Freeze

- Existing `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, and `--diagnose-input` output text remains frozen.
- Existing `ProgramDiagnosticsTests` formatter/smoke assertions remain the compatibility contract.
- CLI implementations build snapshot values first, then format through the same formatter logic.
- Any intentional CLI text change must be staged separately with explicit test updates and a migration note.

## Debug UI Bridge

The debug UI bridge captures current Viewport, Scroll, Input, ClipMode, and LayoutDirty rows through `DebugUiDiagnosticsSnapshot` and formats them through `DebugDiagnosticsFormatter`.

Bridge rules:

- It may temporarily adapt existing PoC-owned state and `Program.Diag*` statics.
- It must stay read-only.
- It must not dispatch runtime messages, trigger renders, or mutate scroll/input/backend state.
- It must not become a framework diagnostics API.

## Files That Stay Put

Snapshot v0 does not move runtime ownership or core behavior. These files stay where they are until a separate migration contract exists:

- `src/Irix.Poc/Program.cs` diagnostics runners
- `src/Irix.Poc/ScrollController.cs`
- `src/Irix.Poc/ScrollFramePump.cs`
- `src/Irix.Poc/CounterInputRouter.cs`
- `src/Irix.Poc/InputOwnershipState.cs`
- `src/Irix.Poc/D3D12DrawingBackend.cs`
- `src/Irix.Rendering/RenderPipeline.cs`

## Completion State

Snapshot type extraction is complete for the v0 CLI-facing PoC snapshots. `DiagnosticsFormatter` no longer depends on `Program.XSnapshot` nested types. Debug bridge v0 is complete for the current debug header rows. Unified diagnostics channel work remains postponed.
