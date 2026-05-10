# Diagnostics Snapshot v0 Design

> 本文记录 diagnostics snapshot 的 v0 数据边界与已落地样板。不移动文件，不改变 `--diagnose*` / `--debug-ui` 的文本输出。当前 CLI diagnostics surface 已经 snapshot-first 闭环；debug UI snapshot bridge 和统一 diagnostics channel 尚未完成。

## 1. 目标

Diagnostics snapshot v0 的目标是在现有 stdout diagnostics 和未来统一 diagnostics channel 之间加一层内部数据模型。v0 只回答三个问题：

- 每类 diagnostics 应该形成什么 snapshot。
- Snapshot 由谁产生、谁消费。
- 迁移期间 PoC 静态字段是否可以参与，以及参与边界在哪里。

## 2. 非目标

- 不新增未选定的 snapshot 类型。
- 不拆分 `Program.cs`，不迁移 diagnostics runner。
- 不迁出 scroll/input/clip/backend 文件。
- 不改变 `ProgramDiagnosticsTests` 的现有文本断言。
- 不把 StyleOnly fast path 接入 `RenderPipeline.Build`。
- 不把 debug UI 改成新的 overlay 系统。

## 3. Snapshot 类型清单

| Snapshot | 所属能力 | v0 字段草案 | 当前文本入口 | 状态 |
|----------|----------|-------------|--------------|------|
| `RenderingPipelineDiagnosticSnapshot` | render pipeline / compositor | render count, partial apply count, full apply count, empty frame count, partial hit rate, compositor dirty command ranges, backend dirty command ranges, dirty range alignment, layout command count, clipped command count, layout rebuild count/reason/classifications, hit target summary, scroll container diagnostics | `--diagnose` compositor + layout pipeline blocks, `--debug-ui` layout dirty row | Implemented for the `--diagnose` compositor/layout pipeline text surface; formatter consumes snapshot for compositor counters, dirty ranges, layout counters, hit target detail, and scroll container summary. |
| `BackendClipTextDiagnosticSnapshot` | backend clip / scissor / text clip | backend clip mode, clipped command count, empty intersection skipped count, scissor state change count, last effective scissor, last effective text clip, text clip skipped count, device removed, device error reason | `--diagnose` pipeline scissor/text clip, clip scissor, empty scissor, text clip blocks; `--debug-ui` clip mode row | Implemented as third v0 snapshot sample; backend device, clip mode, scissor, empty scissor, text clip, and pipeline smoke formatters consume snapshot and CLI text is unchanged. |
| `ViewportDiagnosticsSnapshot` | viewport / resize physical v0 | window physical bounds, renderer swapchain bounds, translator viewport, layout viewport, last applied pending resize, render count, layout rebuild count/reason, viewport matches renderer, layout uses renderer size, scale mode, screen scale, DPI awareness, coordinate space | `--diagnose-resize`, `--debug-ui` viewport row | Implemented as second v0 snapshot sample; formatter consumes snapshot and CLI text is unchanged. |
| `ScrollDiagnosticsSnapshot` | scroll pump / scroll model | dispatched frame count, render wait ms, last dt, drained pixels, last frame drained pixels, pending pixels, frame queued, tick loop running, applied scroll Y, target position, max scroll state | `--diagnose-scroll`, `--debug-ui` scroll row | Implemented for `--diagnose-scroll`; formatter consumes snapshot and CLI text is unchanged. |
| `InputDiagnosticsSnapshot` | input ownership / routed input | hovered target, focused target, pressed target, captured target, hover change count, pointer pressed, ownership events, mapped messages, button visual state lines, input dirty reason lines | `--diagnose-input`, `--debug-ui` input row | Implemented minimal for `--diagnose-input`; snapshot carries final ownership, ordered diagnostic lines, ownership lines, event lines, button visual state lines, and dirty reason lines. |
| `StyleOnlyPatchPlanDiagnosticSnapshot` | style-only plan smoke | case name, eligible, fallback reason, dirty element ranges, dirty command ranges, patched hit target count | `--diagnose` StyleOnly Patch Plan Diagnostics block | Implemented as first v0 snapshot sample; smoke builds snapshots before formatting. |

## 4. Provider 边界

| Snapshot | Producer owner | Current v0 producer adapter | Consumers | PoC static fields |
|----------|----------------|-----------------------------|-----------|-------------------|
| `RenderingPipelineDiagnosticSnapshot` | `Irix.Rendering` for `RenderPipeline` / `DrawingBackendCompositor` counters | `Irix.Poc.Program.RunDiagnosticMode` assembles the snapshot from `RenderPipeline`, `DrawingBackendCompositor`, and `D3D12DrawingBackend` until channel exists | CLI diagnostics, debug UI, tests | Allowed only as a temporary read adapter for debug UI rows; not the source of truth for pipeline counters. |
| `BackendClipTextDiagnosticSnapshot` | backend adapter owner; currently `Irix.Poc.D3D12DrawingBackend`, future backend adapter layer | `Program` smoke runners ask `D3D12DrawingBackend` and `D3D12Renderer` for counters/device state after each smoke | CLI clip/text diagnostics, tests | Allowed for `DiagBackendClipMode` only as a debug UI bridge; backend counters must come from backend instance state. |
| `ViewportDiagnosticsSnapshot` | platform/render bridge owner; current data crosses `WindowsPlatformWindow`, `D3D12Renderer`, `WindowDrawCommandTranslator` | `Program.CreateViewportDiagnostics` and `BuildResizeViewportDiagnosticLines` already act as the adapter shape | `--diagnose-resize`, debug UI viewport row, tests | Allowed for debug UI dispatch gating only; dimensions must come from window/renderer/translator, not cached statics. |
| `ScrollDiagnosticsSnapshot` | scroll service owner; currently PoC scroll pump/controller | `RunScrollDiagnosticModeAsync` assembles from `ScrollFramePump` and the local scripted `ScrollState`; debug UI still reads the existing static bridge | `--diagnose-scroll`, debug UI scroll row, tests | Allowed in v0 because `ScrollFramePump` is still PoC-owned; static access must stay read-only and local to diagnostics. |
| `InputDiagnosticsSnapshot` | input routing/focus owner; currently PoC input ownership state/router | `BuildInputDiagnosticsSnapshot` assembles from `InputOwnershipState`, `CounterInputRouter`, button state derivation, and dirty reason smoke | `--diagnose-input`, debug UI input row, tests | Allowed in v0 because input ownership is still PoC-owned; do not expose statics as framework API. |
| `StyleOnlyPatchPlanDiagnosticSnapshot` | `Irix.Rendering.StyleOnlyPatchPlanBuilder` and plan data | `Program.BuildStyleOnlyPatchPlanSmokeDiagnosticLines` can adapt `StyleOnlyPatchPlan` into snapshot data before formatting | `--diagnose` style-only plan block, tests | Not needed; plan data should be produced from explicit planner inputs. |

## 5. CLI 闭环状态

- `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, and `--diagnose-input` now assemble snapshot data before formatting their stable stdout lines.
- `DiagnosticsFormatter` owns CLI text formatting for the snapshot-backed surface. `Program.cs` still owns scripted diagnostic runners and snapshot production adapters.
- CLI snapshot v0 is closed for the current text surface: every existing `--diagnose*` block either formats from a snapshot directly or builds snapshot lines before writing stdout.
- Debug UI is not snapshot-first yet. It still reads `CounterViewportDiagnostics`, `CounterLayoutDiagnostics`, and the existing `Program.Diag*` static bridge.
- Unified diagnostics channel is not implemented. No event bus, subscription API, or debug overlay replacement exists in this stage.

## 6. CLI 文本冻结规则

- Existing `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, and `--diagnose-input` output text remains frozen.
- Existing `ProgramDiagnosticsTests` formatter/smoke assertions remain the compatibility contract.
- Snapshot v0 is an internal data layer behind those formatters, not a replacement for the text contract.
- CLI implementations build snapshot values first, then call the same formatter logic to preserve exact lines.
- Any intentional CLI text change must be staged separately with explicit test updates and a migration note.

## 7. Debug UI Snapshot Bridge 设计

Debug UI 后续应通过一个 snapshot bridge 获取诊断数据，而不是继续直接读取散落的 statics。当前阶段只冻结设计，不接线、不替换 overlay、不改变 debug UI 文本。

Implemented minimal bridge contract: `DebugUiDiagnosticsSnapshotBridge.cs`. It is intentionally not wired into the overlay yet.

```csharp
internal interface IDebugDiagnosticsSnapshotBridge
{
	DebugUiDiagnosticsSnapshot Capture();
}

internal readonly record struct DebugUiDiagnosticsSnapshot(
	Program.ViewportDiagnosticsSnapshot? Viewport,
	CounterLayoutDiagnostics? Layout,
	Program.ScrollDiagnosticsSnapshot? Scroll,
	Program.InputDiagnosticsSnapshot? Input,
	Program.BackendClipTextDiagnosticSnapshot? BackendClipText);
```

Bridge rules:

- The bridge may temporarily adapt existing PoC-owned state and `Program.Diag*` statics, but those statics should become implementation details rather than UI-facing API.
- Debug UI rendering should consume `DebugUiDiagnosticsSnapshot` or narrower per-row snapshots, then use formatter/helper methods for stable row text.
- The bridge must stay read-only and must not dispatch runtime messages, trigger renders, or mutate scroll/input/backend state.
- Introducing the bridge is a separate stage from CLI snapshot v0 closure.

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

## 9. 暂不迁出文件

Snapshot v0 does not move files. These files stay where they are during this preparation stage:

- `src/Irix.Poc/Program.cs`
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
| Design debug UI snapshot bridge | Covered by the bridge design section; not wired into debug UI yet. |
| Do not move files | Covered by the no-move list. |