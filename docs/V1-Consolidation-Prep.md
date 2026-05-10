# v1 Architecture Consolidation Prep

> 本文是盘点与迁移准备，不是迁移实施。当前阶段不改变运行行为，不移动稳定代码，只记录模块边界、诊断入口、测试分组与未来收敛方向。

## 1. 当前模块边界

| 模块 | 当前职责 | 边界判断 |
|------|----------|----------|
| `Irix.Core` | MVU contracts、`Runtime`、`Command`、message dispatcher、`VirtualNode` model、diff/patch、`RetainedTree`、patch batch ownership | 框架核心层。应保持无平台、无 drawing backend、无 PoC app 语义。 |
| `Irix.Rendering` | layout tree build、draw command recording、render pipeline、compositor loop、drawing backend compositor、retained command/frame、style preset、control visual state、dirty classification、StyleOnly planning helpers | 框架 rendering 层。应保持无 Win32/D3D12 window lifecycle 依赖；可以依赖 `Irix.Core`、`Irix.Drawing`、`Irix.Platform` abstractions。 |
| `Irix.Platform.Windows` | Windows host/thread/window、screen enumeration、raw input event source、window class/messages、D3D12 renderer、2D rectangle renderer、DWrite/D2D text renderer、CsWin32 interop | Windows platform/backend primitive 层。应保持无 Counter sample、无 diagnostic CLI、无 app-specific routing。 |
| `Irix.Poc` | Counter sample app、Counter input routing、input ownership state、scroll controller/pump、window draw translator、visual compositor glue、D3D12 drawing backend adapter、diagnostic CLI/smoke runners、debug UI rows | PoC integration layer and current debt collection point. It proves the full path, but it mixes app behavior, general-looking helpers, backend adapter logic, and diagnostic stdout. |

### 边界结论

- Framework 层已经基本成形：`Irix.Core` owns MVU/tree/diff, `Irix.Rendering` owns layout/render-frame planning, `Irix.Platform.Windows` owns Win32/D3D12 primitives.
- `Irix.Poc` 当前承担了三类债务：sample app semantics、cross-layer wiring、diagnostic entrypoints。
- 后续 consolidation 应先建立统一 diagnostics channel，再决定哪些 PoC helpers 值得提升到 framework 层；不要先做文件搬迁。

## 2. 诊断入口盘点

| 入口 | 当前归属 | 覆盖内容 | 后续 channel 方向 |
|------|----------|----------|-------------------|
| `--diagnose` | `Irix.Poc.Program.RunDiagnosticMode` | D3D12 text cache/device、style preset、compositor partial apply、layout pipeline、StyleOnly plan smoke、pipeline scissor/text clip smoke、backend scissor/text clip smoke | 拆成 diagnostics providers，再由统一 runner 汇总输出；当前 stdout contract 保持冻结。 |
| `--diagnose-resize` | `Irix.Poc.ResizeDiagnosticRunner.Run` | window physical size、renderer swapchain size、translator/layout viewport、pending resize apply、viewport dirty reason、physical-pixels mode | Second runner split sample. Keep as PoC smoke; future migration waits until viewport/platform diagnostics ownership is settled. |
| `--diagnose-scroll` | `Irix.Poc.ScrollDiagnosticRunner.RunAsync` | scroll pump frame count、render wait、dt、drained pixels、pending pixels | First runner split sample. Keep as PoC diagnostic; future migration waits until scroll controller/pump ownership is settled. |
| `--diagnose-input` | `Irix.Poc.InputDiagnosticRunner.RunAsync` | input ownership transitions、button visual priority、keyboard/pointer mapping、dirty reason smoke | Third runner split sample. Keep as PoC diagnostic; future migration waits until input model ownership is settled. |
| `--debug-ui` | `CounterApplication.BuildDiagnosticHeaderRows` plus `Program` diagnostic readouts | in-app scroll/input/clip mode/viewport/layout dirty rows | 未来由统一 diagnostics snapshot 驱动 debug overlay；现在保持 Counter sample 内部实现。 |
| StyleOnly plan smoke | `Irix.Poc.StyleOnlyPatchPlanSmokeDiagnostics.BuildDiagnosticLines` plus `StyleOnlyPatchPlanBuilder` | hover-only eligible、layout-affecting fallback、dirty element/command ranges、patched hit target count | Planning logic stays in `Irix.Rendering`; PoC smoke host is split out while stdout remains frozen。 |
| Scissor/text clip smoke | `Program` smoke runners plus `D3D12DrawingBackend` diagnostics | FillRect scissor, empty intersection skip, D2D text clip, pipeline clip propagation | Backend counters can later move with backend adapter; stable smoke strings stay frozen until channel replacement is ready。 |

### 统一 diagnostics channel 的最小方向

- 先定义 data snapshot / event shape，不改变现有 CLI output。
- Provider 粒度按 owner 分：rendering pipeline、drawing backend、platform viewport、input、scroll。
- CLI 和 debug UI 只消费 snapshot，不直接读取 scattered statics。
- 迁移时保留当前 `--diagnose*` 文本测试，新增 channel tests 后再替换 formatter。

### Program diagnostics runner split 顺序

Diagnostics snapshot v0 and debug bridge v0 are sealed; unified diagnostics channel is paused. The current consolidation line is runner split only: move scripted diagnostics out of `Program.cs` one small runner at a time while preserving stdout and overlay contracts.

Recommended order:

1. `--diagnose-scroll` → `ScrollDiagnosticRunner` (done; smallest async runner, no window/D3D12 setup).
2. `--diagnose-resize` → `ResizeDiagnosticRunner` (done; focused window/viewport smoke, still smaller than full `--diagnose`).
3. `--diagnose-input` → `InputDiagnosticRunner` (done; ownership and dirty-reason scripted flows are out of `Program.cs`).
4. StyleOnly plan smoke helpers → `StyleOnlyPatchPlanSmokeDiagnostics` (done; PoC smoke host only, rendering planner unchanged).
5. Full `RunDiagnosticMode` last; it still touches text cache, style preset, compositor, layout, scissor, text clip, and backend counters in one flow.

Rules for this split:

- Do not move scroll/input/backend/runtime code while splitting runners.
- Do not change `DiagnosticsFormatter`, `DebugDiagnosticsFormatter`, or snapshot fields unless fixing a regression.
- Do not enable StyleOnly fast-path during runner split.
- Run full tests plus `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, `--diagnose-input`, and a `--debug-ui` smoke after each extracted runner.

## 3. 测试分组盘点

| 分组 | 当前主要测试文件 | 覆盖重点 |
|------|------------------|----------|
| scroll | `ScrollFramePumpTests.cs`, `CounterInputRouterTests.cs`, `WindowLayoutPipelineTests.cs`, `ProgramDiagnosticsTests.cs` | pump coalescing/dt/render wait、wheel-to-pixel conversion、scroll model update、scroll container diagnostics、runtime scroll dirty reason、`--diagnose-scroll` output。 |
| input | `CounterInputRouterTests.cs`, `ProgramDiagnosticsTests.cs`, `WindowLayoutPipelineTests.cs` | pointer/keyboard mapping、hover/focus/press/capture ownership、button visual state priority、routed input model update、debug UI visibility、input dirty reason smoke。 |
| clip | `DrawingScissorTests.cs`, `D3D12DrawingBackendScissorTests.cs`, `DrawingBackendCompositorTests.cs`, `WindowLayoutPipelineTests.cs`, `ProgramDiagnosticsTests.cs` | effective clip math、integer scissor conversion、backend scissor/text clip counters、hit-test clip bounds、root/nested clip semantics、pipeline/backend smoke formatter strings。 |
| viewport | `WindowLayoutPipelineTests.cs`, `ProgramDiagnosticsTests.cs`, `TextRenderingCorrectnessTests.cs` | retained layout reuse vs viewport rebuild、translator resize source-of-truth、physical viewport diagnostics、debug UI viewport row、layout scaling with viewport width。 |
| layout dirty | `WindowLayoutPipelineTests.cs`, `ProgramDiagnosticsTests.cs` | `StyleOnly` / `TextSizeAffecting` / `LayoutAffecting` / `TreeStructure` / `ViewportChanged` classification、mixed priority、real PoC runtime dirty reason、debug UI layout dirty row。 |
| style-only plan | `WindowLayoutPipelineTests.cs`, `ProgramDiagnosticsTests.cs` | style-only eligibility, stable command range mapping, hit target metadata patch, plan fallback reasons, plan diagnostic formatter/smoke。 |
| retained frame | `DrawingBackendCompositorTests.cs`, `FrameDrawingResourcesTests.cs`, `BatchOwnershipTests.cs`, `RetainedTreeTests.cs`, `WindowVisualCompositorTests.cs` | partial apply resource identity, dirty range propagation, frame resource pooling/retain/release/frame id, retained tree dirty set behavior, compositor ownership/disposal。 |

### 维护建议

- 先保留现有文件，按 test name / region / future trait 分组即可；不要为了分组先搬测试文件。
- diagnostics formatter tests 继续集中在 `ProgramDiagnosticsTests.cs`，直到 channel 形状稳定。
- rendering pipeline invariants 继续留在 `WindowLayoutPipelineTests.cs`，避免 StyleOnly fast-path 前拆散上下文。

## 4. 暂不移动清单

| 范围 | 暂不移动内容 | 原因 |
|------|--------------|------|
| scroll | `ScrollController`, `ScrollFramePump`, Counter scroll messages, scroll diagnostics wiring | 刚稳定了 pump/diagnostic/scroll dirty baseline；先不要把 PoC scroll control 提升成 framework API。 |
| input | `CounterInputRouter`, `InputOwnershipState`, `Program.TryMapInputForRuntime`, Counter ownership messages | 目前仍绑定 Counter action ids 和 button state；先不要抽象成通用 input framework。 |
| clip | `DrawingScissor`, `D3D12DrawingBackend` scissor/text clip logic, compositor hit-test clip behavior, root clip semantics | clip/scissor/text clip v0 已冻结；移动会同时触碰 drawing、backend、layout、hit-test 多条稳定线。 |
| render pipeline | `RenderPipeline`, `LayoutTreeBuilder`, `DrawCommandRecorder`, retained frame apply, `StyleOnlyPatch*` helpers | layout dirty and StyleOnly plan 仍是 planning/diagnostic stage；不要借 consolidation 接入 fast path 或 partial layout。 |

## 5. 未来迁出 `Irix.Poc` 的候选

| 候选 | 当前文件 | 未来可能归属 | 迁出前置条件 |
|------|----------|--------------|--------------|
| diagnostics runner / formatter | `Program.cs` | future diagnostics layer or dedicated test/diagnostic host | 统一 snapshot/channel 形状确定，并且现有 `--diagnose*` 文本 contract 有兼容适配。 |
| D3D12 drawing backend adapter | `D3D12DrawingBackend.cs` | `Irix.Platform.Windows` or future backend adapter assembly | backend API 边界稳定，clip diagnostics provider 已抽出，PoC 不再直接承载 backend counters。 |
| window-to-render translator glue | `WindowDrawCommandTranslator.cs`, `WindowVisualCompositor.cs`, `WindowBackend*.cs` | rendering/platform bridge layer | window lifecycle、viewport source-of-truth、compositor ownership API 固化。 |
| generic visual state / style defaults | `CounterStylePreset.cs` and pieces of Counter button style usage | `Irix.Rendering` design system / controls layer | 控件/theme scope 明确；不和 Counter sample 文案/action ids 绑定。 |
| scroll primitives | `ScrollController.cs`, `ScrollFramePump.cs` | controls/input service layer | scroll model API、animation policy、system scroll settings boundary 明确。 |
| input ownership primitives | `InputOwnershipState.cs` plus reusable parts of `CounterInputRouter.cs` | input routing/focus service layer | action id routing、focus model、capture semantics 从 Counter messages 中解耦。 |

## 6. 应保持框架层的内容

| 层 | 保持内容 |
|----|----------|
| `Irix.Core` | MVU contracts/runtime, virtual node model, differ, patch batch ownership, retained tree dirty index behavior。 |
| `Irix.Drawing` | draw primitives, command batches, frame resource arenas, drawing backend contracts, scissor math。 |
| `Irix.Rendering` | layout result models, layout builder, render pipeline, draw command recorder, compositor abstractions, retained command/frame, render style preset, dirty classification, StyleOnly planning helpers。 |
| `Irix.Platform.Windows` | Win32 host/window/thread/input source/screen enumeration and low-level D3D12/D2D/DWrite renderer primitives。 |

## 7. 推荐收敛顺序

1. Treat [Diagnostics-Snapshot-v0.md](Diagnostics-Snapshot-v0.md) as sealed; only repair regressions in that line.
2. Continue Program diagnostics runner split in the order above, keeping CLI output and debug overlay rows unchanged.
3. Leave unified diagnostics channel, event bus, registry, and provider replacement paused until runner debt is lower.
4. Promote only generic scroll/input primitives after names and contracts no longer reference Counter sample behavior.
5. Move one axis at a time and run full tests plus `--diagnose`, `--diagnose-resize`, `--diagnose-scroll`, `--diagnose-input`, and a `--debug-ui` smoke after each move.

## 8. 当前完成标准对照

| 短期任务 | 状态 |
|----------|------|
| 模块边界盘点 | 已覆盖 `Irix.Core` / `Irix.Rendering` / `Irix.Platform.Windows` / `Irix.Poc` 当前职责与 PoC 债务。 |
| 诊断入口盘点 | 已列出 `--diagnose*`、`--debug-ui`、StyleOnly plan smoke、scissor/text clip smoke 的当前归属和 channel 方向。 |
| 测试分组盘点 | 已按 scroll/input/clip/viewport/layout dirty/style-only plan/retained frame 分组。 |
| 标记不移动清单 | 已明确 scroll/input/clip/render pipeline 核心代码暂不移动。 |
| consolidation prep 文档 | 本文即输出；当前阶段不改运行行为。 |