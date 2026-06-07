# Diagnostics Snapshot

> Current data boundary for CLI diagnostics and debug UI rows. Change this only when the diagnostics channel, tests, and docs are migrated together.

## Goal

Diagnostics snapshots provide an internal data model behind stdout diagnostics and debug UI rows. They keep current text output unless a task intentionally migrates diagnostics text, tests, and docs together.

The model answers:

- Which snapshot type backs each diagnostics surface.
- Which component owns production of that snapshot.
- Which PoC-only static fields may be adapted temporarily.

## Non-Goals

- No new diagnostics surfaces.
- No unified diagnostics channel, event bus, registry, subscription API, or debug UI replacement.
- No diagnostics runner migration.
- No scroll/input/clip/backend ownership move.
- No intentional CLI text change.
- No StyleOnly fast path in `RenderPipeline.Build`.

## Snapshot Surface

| Snapshot | Producer owner | Current consumers | Boundary |
|----------|----------------|-------------------|----------|
| `RenderingPipelineDiagnosticSnapshot` | `Irix.Rendering` counters from `RenderPipeline` / `DrawingBackendCompositor` | `--diagnose` compositor and layout pipeline blocks | Pipeline counters must come from pipeline/compositor state, not retained PoC statics. |
| `PartialApplyHandoffDiagnosticSnapshot` | `DrawingBackendCompositor.LastHandoffResult` plus production owner feed stamps | `--diagnose` compositor block and focused tests | Internal diagnostics-only status for selected segmented render-source handoff. It reports disabled, executed, fallback, and rejected states without changing backend execution contracts or introducing a public partial-apply API. |
| `DrawMaterialOutputDiagnostics` | `ColorOutputMapping.SdrSrgb` plus the active D3D12 material capability policy | `--diagnose` backend/device block and focused tests | Internal diagnostics-only status for the current SDR material mapper. It reports selected material kind, backend capability, fallback reason, and fallback counts without adding public material authoring or changing backend execute contracts. |
| `BackendClipTextDiagnosticSnapshot` | Backend adapter owner; currently `Irix.Platform.Windows.D3D12DrawingBackend` plus `D3D12Renderer` device state | `--diagnose` scissor/text clip smokes and debug clip row | Device errors remain typed until CLI/debug formatting. |
| `ViewportDiagnosticsSnapshot` | Platform/render bridge owner; current data crosses window, renderer, and translator | `--diagnose-resize` and debug viewport row | Dimensions must come from window/renderer/translator source-of-truth fields. |
| `ScrollDiagnosticsSnapshot` | Scroll service owner; currently PoC scroll pump/controller | `--diagnose-scroll` and debug scroll row | PoC statics are read-only adapters until scroll ownership is extracted. |
| `InputDiagnosticsSnapshot` | Input routing/focus owner; currently PoC input ownership/router | `--diagnose-input` and debug input row | Ownership state is value data; diagnostics history remains bounded. |
| `StyleOnlyPatchPlanDiagnosticSnapshot` | `Irix.Rendering.StyleOnlyPatchPlanBuilder` | `--diagnose` style-only plan smoke | Planner data is explicit input/output and does not need PoC statics. |
| `StyleTransitionCompletionPumpDiagnosticSnapshot` | `Irix.Poc.StyleTransitionCompletionPump` plus `StyleTransitionCompletionTracker` | Formatter/source guards and focused tests | Internal Poc-only lifecycle status for the narrow Counter style transition completion pump. It reports observation state only; it does not add a public transition API, timeline scheduler, or compositor/runtime extraction. |
| `StyleTransitionBatchActivationDiagnosticSnapshot` | `Irix.Poc.StyleTransitionRuntimeCoordinator` result plus `StyleTransitionCompletionTracker` / `DrawingBackendCompositor` state | Formatter/source guards and focused tests | Internal Poc-only activation/fallback status for routed Counter multi-target batches. It observes activation and cleanup results; it does not add a public transition API, generic scheduler, or runtime extraction. |
| `DebugUiDiagnosticsSnapshot` | `DefaultDebugDiagnosticsSnapshotBridge` | Current debug header rows | Bridge is read-only and local; it must not mutate scroll/input/backend state. |

## Diagnostic Text Guard

- Existing `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, and `--diagnose-input` output text is guarded by tests, not a public compatibility promise.
- Existing `ProgramDiagnosticsTests` formatter/smoke assertions are local regression guards.
- CLI implementations build snapshot values first, then format through the same formatter logic.
- Partial-apply handoff status is one stable machine-readable line with `handoffKind`, `reason`, `ownerKind`, `planKind`, `fallbackReason`, `runtimeOwnerEnabled`, `fallbackApplied`, `ownerStatePreserved`, `batchFrameId`, `batchCommandCount`, and `dirtyRanges`.
- Material output status is one stable machine-readable line with `outputKind`, `backendCapabilities`, `selectedMaterialKind`, `fallbackReason`, `fallbackApplied`, `commandCount`, `solidColorCommands`, `linearGradientCommands`, and `fallbackCommands`. D3D12 FillRect may report `backendCapabilities=SolidColor, LinearGradient` and `fallbackReason=None` for clamp-free per-corner linear-gradient SDR rasterization or bounded segmented clamp fallback, while unsupported material paths still report fallback without making material authoring public.
- Style transition completion pump status is one stable machine-readable line with `isRunning`, `hasActiveTransition`, `activeTarget`, `activeInstance`, `activeOwnerCount`, `activeOwnerKind`, `tickMode`, `lastResult`, `lastDrainedEvents`, `lastCommitResult`, `lastCommitTarget`, `trackerResult`, `trackerTarget`, `trackerInstance`, `tickCount`, `commitCount`, `drainedEvents`, `hasError`, and `error`.
- Style transition batch activation status is one stable machine-readable line with `activationKind`, `activationReason`, `runtimePreflight`, `runtimeReady`, `runtimeBlocked`, `presentationPreflight`, `presentationAccepted`, `presentationRejected`, `declarationCount`, `trackedOwnerCount`, `presentationStateChanged`, `cleanupResult`, `cleanupTarget`, `cleanupApplied`, `activeAfterCleanup`, `activeOwnerCountAfterCleanup`, and `presentationPlanAfterCleanup`.
- Any intentional CLI text change must be staged separately with explicit test updates and a migration note.

## Debug UI Bridge

The debug UI bridge captures current Viewport, Scroll, Input, ClipMode, and LayoutDirty rows through `DebugUiDiagnosticsSnapshot` and formats them through `DebugDiagnosticsFormatter`.

Bridge rules:

- It may temporarily adapt existing PoC-owned state and `Program.Diag*` statics.
- It must stay read-only.
- It must not dispatch runtime messages, trigger renders, or mutate scroll/input/backend state.
- It must not become a framework diagnostics API.

## Files That Stay Put

Snapshot extraction does not move runtime ownership or core behavior. These files stay where they are until a separate migration contract exists:

- `src/Irix.Poc/Program.cs` diagnostics runners
- `src/Irix.Poc/ScrollController.cs`
- `src/Irix.Poc/ScrollFramePump.cs`
- `src/Irix.Poc/CounterInputRouter.cs`
- `src/Irix.Poc/InputOwnershipState.cs`
- `src/Irix.Platform.Windows/D3D12DrawingBackend.cs`
- `src/Irix.Rendering/RenderPipeline.cs`

## Completion State

Snapshot type extraction is complete for the CLI-facing PoC snapshots. `DiagnosticsFormatter` no longer depends on `Program.XSnapshot` nested types. The debug bridge covers the current debug header rows. Unified diagnostics channel work remains postponed.
