# Diagnostics Snapshot v0 Design

> 本文记录 diagnostics snapshot 的 v0 数据边界与已落地样板。不改变 `--diagnose*` / `--debug-ui` 的文本输出。当前 CLI diagnostics surface 已经 snapshot-first 闭环，formatter 已抽出，debug UI snapshot bridge 已轻接 layout dirty 行；统一 diagnostics channel 尚未完成。

## 1. 目标

Diagnostics snapshot v0 的目标是在现有 stdout diagnostics 和未来统一 diagnostics channel 之间加一层内部数据模型。v0 只回答三个问题：

- 每类 diagnostics 应该形成什么 snapshot。
- Snapshot 由谁产生、谁消费。
- 迁移期间 PoC 静态字段是否可以参与，以及参与边界在哪里。

## 2. 非目标

- 不新增未选定的 snapshot 类型。
- 不迁移 diagnostics runner。
- 不迁出 scroll/input/clip/backend 文件。
- 不改变 `ProgramDiagnosticsTests` 的现有文本断言。
- 不把 StyleOnly fast path 接入 `RenderPipeline.Build`。
- 不把 debug UI 一次性改成新的 overlay 系统。

## 3. Snapshot 类型清单

| Snapshot | 所属能力 | v0 字段草案 | 当前文本入口 | 状态 |
|----------|----------|-------------|--------------|------|
| `RenderingPipelineDiagnosticSnapshot` | render pipeline / compositor | render count, partial apply count, full apply count, empty frame count, partial hit rate, compositor dirty command ranges, backend dirty command ranges, dirty range alignment, layout command count, clipped command count, layout rebuild count/reason/classifications, hit target summary, scroll container diagnostics | `--diagnose` compositor + layout pipeline blocks, future `--debug-ui` pipeline rows | Implemented in `DiagnosticsSnapshots.cs`; formatter consumes snapshot for compositor counters, dirty ranges, layout counters, hit target detail, and scroll container summary. |
| `BackendClipTextDiagnosticSnapshot` | backend clip / scissor / text clip | backend clip mode, clipped command count, empty intersection skipped count, scissor state change count, last effective scissor, last effective text clip, text clip skipped count, device removed, device error reason | `--diagnose` pipeline scissor/text clip, clip scissor, empty scissor, text clip blocks; future `--debug-ui` clip rows | Implemented in `DiagnosticsSnapshots.cs`; backend device, clip mode, scissor, empty scissor, text clip, and pipeline smoke formatters consume snapshot and CLI text is unchanged. |
| `ViewportDiagnosticsSnapshot` | viewport / resize physical v0 | window physical bounds, renderer swapchain bounds, translator viewport, layout viewport, last applied pending resize, render count, layout rebuild count/reason, viewport matches renderer, layout uses renderer size, scale mode, screen scale, DPI awareness, coordinate space | `--diagnose-resize`, future `--debug-ui` viewport bridge | Implemented in `DiagnosticsSnapshots.cs`; formatter consumes snapshot and CLI text is unchanged. |
| `ScrollDiagnosticsSnapshot` | scroll pump / scroll model | dispatched frame count, render wait ms, last dt, drained pixels, last frame drained pixels, pending pixels, frame queued, tick loop running, applied scroll Y, target position, max scroll state | `--diagnose-scroll`, `DefaultDebugDiagnosticsSnapshotBridge` | Implemented in `DiagnosticsSnapshots.cs`; formatter consumes snapshot and CLI text is unchanged. |
| `InputDiagnosticsSnapshot` | input ownership / routed input | hovered target, focused target, pressed target, captured target, hover change count, pointer pressed, ownership events, mapped messages, button visual state lines, input dirty reason lines | `--diagnose-input`, future `--debug-ui` input bridge | Implemented in `DiagnosticsSnapshots.cs`; snapshot carries final ownership, ordered diagnostic lines, ownership lines, event lines, button visual state lines, and dirty reason lines. |
| `StyleOnlyPatchPlanDiagnosticSnapshot` | style-only plan smoke | case name, eligible, fallback reason, dirty element ranges, dirty command ranges, patched hit target count | `--diagnose` StyleOnly Patch Plan Diagnostics block | Implemented as first v0 snapshot sample; smoke builds snapshots before formatting. |

## 4. Provider 边界

| Snapshot | Producer owner | Current v0 producer adapter | Consumers | PoC static fields |
|----------|----------------|-----------------------------|-----------|-------------------|
| `RenderingPipelineDiagnosticSnapshot` | `Irix.Rendering` for `RenderPipeline` / `DrawingBackendCompositor` counters | `Irix.Poc.Program.RunDiagnosticMode` assembles the snapshot from `RenderPipeline`, `DrawingBackendCompositor`, and `D3D12DrawingBackend` until channel exists | CLI diagnostics, future debug UI pipeline rows, tests | Allowed only as a temporary read adapter for debug UI rows; not the source of truth for pipeline counters. |
| `BackendClipTextDiagnosticSnapshot` | backend adapter owner; currently `Irix.Poc.D3D12DrawingBackend`, future backend adapter layer | `Program` smoke runners ask `D3D12DrawingBackend` and `D3D12Renderer` for counters/device state after each smoke | CLI clip/text diagnostics, tests | Allowed for `DiagBackendClipMode` only as a debug UI bridge; backend counters must come from backend instance state. |
| `ViewportDiagnosticsSnapshot` | platform/render bridge owner; current data crosses `WindowsPlatformWindow`, `D3D12Renderer`, `WindowDrawCommandTranslator` | `RunResizeDiagnosticMode` assembles the CLI snapshot; debug UI still uses `CounterViewportDiagnostics` through the bridge aggregate | `--diagnose-resize`, future debug UI viewport bridge, tests | Allowed for debug UI dispatch gating only; dimensions must come from window/renderer/translator, not cached statics. |
| `ScrollDiagnosticsSnapshot` | scroll service owner; currently PoC scroll pump/controller | `RunScrollDiagnosticModeAsync` assembles from `ScrollFramePump` and the local scripted `ScrollState`; `DefaultDebugDiagnosticsSnapshotBridge` assembles a read-only debug snapshot from model scroll state and existing statics | `--diagnose-scroll`, debug bridge, tests | Allowed in v0 because `ScrollFramePump` is still PoC-owned; static access must stay read-only and local to diagnostics. |
| `InputDiagnosticsSnapshot` | input routing/focus owner; currently PoC input ownership state/router | `BuildInputDiagnosticsSnapshot` assembles from `InputOwnershipState`, `CounterInputRouter`, button state derivation, and dirty reason smoke | `--diagnose-input`, future debug UI input bridge, tests | Allowed in v0 because input ownership is still PoC-owned; do not expose statics as framework API. |
| `StyleOnlyPatchPlanDiagnosticSnapshot` | `Irix.Rendering.StyleOnlyPatchPlanBuilder` and plan data | `Program.BuildStyleOnlyPatchPlanSmokeDiagnosticLines` can adapt `StyleOnlyPatchPlan` into snapshot data before formatting | `--diagnose` style-only plan block, tests | Not needed; plan data should be produced from explicit planner inputs. |

## 5. CLI 闭环状态

- `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, and `--diagnose-input` now assemble snapshot data before formatting their stable stdout lines.
- `DiagnosticsFormatter` owns CLI text formatting for the snapshot-backed surface and depends on standalone snapshot types, not `Program.XSnapshot` nested records.
- `DiagnosticsSnapshots.cs` owns `ViewportDiagnosticsSnapshot`, `ScrollDiagnosticsSnapshot`, `InputDiagnosticsSnapshot`, `BackendClipTextDiagnosticSnapshot`, and `RenderingPipelineDiagnosticSnapshot`.
- `Program.cs` still owns scripted diagnostic runners and snapshot production adapters.
- CLI snapshot v0 is closed for the current text surface: every existing `--diagnose*` block either formats from a snapshot directly or builds snapshot lines before writing stdout.
- Debug UI is partially bridge-backed: the layout dirty row now receives `CounterLayoutDiagnostics` through `DefaultDebugDiagnosticsSnapshotBridge` / `DebugUiDiagnosticsSnapshot`; other rows still read existing model state and `Program.Diag*` statics.
- Unified diagnostics channel is not implemented. No event bus, subscription API, or debug overlay replacement exists in this stage.

## 6. CLI 文本冻结规则

- Existing `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, and `--diagnose-input` output text remains frozen.
- Existing `ProgramDiagnosticsTests` formatter/smoke assertions remain the compatibility contract.
- Snapshot v0 is an internal data layer behind those formatters, not a replacement for the text contract.
- CLI implementations build snapshot values first, then call the same formatter logic to preserve exact lines.
- Any intentional CLI text change must be staged separately with explicit test updates and a migration note.

## 7. Debug UI Snapshot Bridge 设计

Debug UI 后续应通过一个 snapshot bridge 获取诊断数据，而不是继续直接读取散落的 statics。当前阶段只接入 layout dirty row，不替换 overlay、不改变 debug UI 文本。

Implemented minimal bridge contract and default implementation: `DebugUiDiagnosticsSnapshotBridge.cs`. `DefaultDebugDiagnosticsSnapshotBridge` reads current PoC model state plus existing `Program.Diag*` statics and returns `DebugUiDiagnosticsSnapshot`.

```csharp
internal interface IDebugDiagnosticsSnapshotBridge
{
	DebugUiDiagnosticsSnapshot Capture();
}

internal readonly record struct DebugUiDiagnosticsSnapshot(
	CounterViewportDiagnostics Viewport,
	CounterLayoutDiagnostics Layout,
	ScrollDiagnosticsSnapshot Scroll,
	OwnershipSnapshot InputOwnership,
	DrawingBackendClipMode BackendClipMode);

internal sealed class DefaultDebugDiagnosticsSnapshotBridge(
	CounterViewportDiagnostics viewport,
	CounterLayoutDiagnostics layout,
	ScrollState scroll) : IDebugDiagnosticsSnapshotBridge;
```

Bridge rules:

- The bridge may temporarily adapt existing PoC-owned state and `Program.Diag*` statics, but those statics should become implementation details rather than UI-facing API.
- Debug UI rendering should consume `DebugUiDiagnosticsSnapshot` or narrower per-row snapshots, then use formatter/helper methods for stable row text.
- The bridge must stay read-only and must not dispatch runtime messages, trigger renders, or mutate scroll/input/backend state.
- Further wiring should replace one row at a time and keep overlay text unchanged.

## 8. 已落地样板

Implemented first sample: `StyleOnlyPatchPlanDiagnosticSnapshot`.

Reasons:

- It is already data-shaped: `StyleOnlyPatchPlan` exposes eligibility, fallback reason, dirty element ranges, dirty command ranges, and patched hit target count.
- It does not need PoC statics.
- It does not touch live D3D12 device/window state.
- It has a small formatter surface and existing tests around `BuildStyleOnlyPatchPlanDiagnosticLine` and smoke lines.
- It keeps consolidation away from scroll/input/clip/backend migration risk.

Implemented second sample: `ViewportDiagnosticsSnapshot`. It reuses the stable resize diagnostic field shape and keeps `RunResizeDiagnosticMode` intact; only the formatter data layer changed.

Implemented third sample: `BackendClipTextDiagnosticSnapshot`. It covers backend clip mode, scissor/text clip counters, effective scissor/text clip, text skip count, and device removed/error state. The pipeline scissor, pipeline text clip, clip scissor, empty scissor, and text clip smoke formatters now receive snapshot data first, while the `--diagnose` text remains unchanged.

Implemented fourth sample: `RenderingPipelineDiagnosticSnapshot`. The `--diagnose` compositor and layout pipeline blocks now format from snapshot data: render/partial/full/empty counters, partial hit rate, dirty ranges, dirty range alignment, backend clipped command count, layout command/clipped counts, layout rebuild count/reason/classifications, hit target detail, and scroll container summary.

Implemented fifth sample: `ScrollDiagnosticsSnapshot`. `--diagnose-scroll` now builds a snapshot from the scripted scroll pump run and formats the existing text from it. The snapshot also carries frame queued, tick loop, applied scroll Y, target, and max scroll state even though the frozen CLI text does not print those extra fields.

Implemented sixth sample: `InputDiagnosticsSnapshot` minimal. `--diagnose-input` now builds a snapshot containing the final ownership read model, ordered diagnostic lines, ownership transition lines, event lines, button visual state lines, and dirty reason lines before formatting the existing text.

Snapshot type extraction is complete for the v0 CLI-facing PoC snapshots: `DiagnosticsFormatter` no longer references `Program.XSnapshot` nested types.

Debug bridge partial wiring is complete for the layout dirty row: `CounterApplication` captures `DebugUiDiagnosticsSnapshot` through `DefaultDebugDiagnosticsSnapshotBridge` and uses its `Layout` value to generate the existing row text.

## 9. 暂不迁出文件

Snapshot v0 does not move runtime ownership or core behavior. These files stay where they are during this preparation stage:

- `src/Irix.Poc/Program.cs` diagnostics runners
- `src/Irix.Poc/ScrollController.cs`
- `src/Irix.Poc/ScrollFramePump.cs`
- `src/Irix.Poc/CounterInputRouter.cs`
- `src/Irix.Poc/InputOwnershipState.cs`
- `src/Irix.Poc/D3D12DrawingBackend.cs`
- `src/Irix.Rendering/RenderPipeline.cs`

Further implementation steps should add snapshot data next to the current owner and adapt existing formatters without changing observable output.

## 10. Completion Checklist

| Task | Status |
|------|--------|
| Define snapshot v0 types | Covered by the snapshot type table. |
| Set provider boundaries | Covered by the producer/adapter/consumer/static field table. |
| Keep CLI text frozen | Covered by CLI freeze rules. |
| Pick minimum implementation candidate | `StyleOnlyPatchPlanDiagnosticSnapshot`, `ViewportDiagnosticsSnapshot`, `BackendClipTextDiagnosticSnapshot`, `RenderingPipelineDiagnosticSnapshot`, `ScrollDiagnosticsSnapshot`, and minimal `InputDiagnosticsSnapshot` are implemented. |
| Close CLI snapshot v0 loop | Done for `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, and `--diagnose-input`; formatter logic lives in `DiagnosticsFormatter`. |
| Extract snapshot type files | Done in `DiagnosticsSnapshots.cs`; `DiagnosticsFormatter` no longer depends on `Program.XSnapshot`. |
| Design debug UI snapshot bridge | Done in `DebugUiDiagnosticsSnapshotBridge.cs`; default bridge is unit-testable. |
| Partially wire debug UI bridge | Done for the layout dirty row; overlay text remains unchanged. |
| Do not move runtime ownership | Covered by the no-move list. |