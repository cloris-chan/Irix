# Irix 项目进度与待办

> 面向开发者、Copilot/Codex 等 AI 工具的当前状态说明。目标是帮助接手者快速判断“哪些已经落地、哪些只是设计、下一步应该从哪里开始”。
> 📅 **最后验证日期：** 2026-05-13。本文档描述的代码状态以此日期为准。
>
> 📐 **架构设计详见：** [Irix_Framework_Design.md](/d:/source/Irix/docs/Irix_Framework_Design.md)。本文档不重复设计细节，仅记录实现状态与待办。
---

## 0. 当前阶段速查

### 已完成阶段

| 阶段线 | 当前状态 | 快速入口 |
|--------|----------|----------|
| Scroll v0 | ✅ 完成：wheel/raw scroll、首帧 delta、scroll pump diagnostic、可见/裁剪元素诊断已覆盖 | 本文 `Scroll 手动验证清单`、`--diagnose-scroll` |
| Input ownership v0 | ✅ 完成：hover/focus/press/capture ownership、event log、`--diagnose-input` 已覆盖 | `ProgramDiagnosticsTests`、`--diagnose-input` |
| Button visual state v0 | ✅ 完成：Normal/Focused/Hovered/Pressed 优先级与颜色诊断已固定 | `BuildStylePresetDiagnosticLines` |
| Style preset v0 | ✅ 完成：默认布局 metrics 与 button color priority 诊断已固定 | `--diagnose` style preset block |
| Clip / scissor / text clip v0 | ✅ 完成：clip bounds 传递、hit-test clip、FillRect scissor、D2D text clip、empty intersection skip 已验证 | [ADR-Scissor-Clipping-v0.md](ADR-Scissor-Clipping-v0.md) |
| Viewport / resize physical v0 | ✅ 完成：renderer swapchain size 是 layout viewport source of truth，resize diagnostic 已固定 | 本文 `Viewport / resize physical v0`、`--diagnose-resize` |
| Root `ScrollContainer` clip semantics v0 | ✅ 完成：root clip 使用 viewport 边界，content 仍从 padding 后开始 | 本文 `Root ScrollContainer clip semantics v0` |
| Layout dirty diagnostics v1 | ✅ 完成：dirty reason、`--debug-ui` row、`--diagnose-input` dirty reason、真实 PoC path tests 已覆盖 | [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md) |
| StyleOnly plan diagnostics | ✅ 完成：`--diagnose` 已输出 hover-only eligible 与 layout-affecting fallback smoke | [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md#plan-builder-boundary) |

### ADR / 设计入口索引

| 文档 | 用途 |
|------|------|
| [Irix_Framework_Design.md](Irix_Framework_Design.md) | 总体架构、版本边界、主 ADR 索引、v1/v2 范围 |
| [ADR-Scissor-Clipping-v0.md](ADR-Scissor-Clipping-v0.md) | clip/scissor/text clip v0 决策、smoke baseline、冻结边界 |
| [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md) | layout dirty 分类、StyleOnly patch v0 设计、plan diagnostics、未来 fast-path 接入点 |
| [V1-Consolidation-Prep.md](V1-Consolidation-Prep.md) | v1 architecture consolidation prep：模块边界、诊断入口、测试分组、暂不移动清单 |
| [V1-API-Control-Boundary-Prep.md](V1-API-Control-Boundary-Prep.md) | v1 API/control boundary prep：controls、input、window glue 的 ownership 与命名边界 |
| [V1-Translator-Feedback-Contract-Prep.md](V1-Translator-Feedback-Contract-Prep.md) | v1 translator feedback contract prep：promotion gates、style/viewport/retained-tree/pipeline/feedback 边界 |
| [V1-Scroll-Settings-Provider-Prep.md](V1-Scroll-Settings-Provider-Prep.md) | v1 scroll settings provider prep：Windows wheel lines/chars 来源、fallback defaults、非 Windows 行为 |
| [V1-Retained-Partial-Apply-Prep.md](V1-Retained-Partial-Apply-Prep.md) | v1 retained / partial apply prep：已有资产、blocked decisions、最小安全实现线 |
| [V1-Partial-Apply-Preflight-Design.md](V1-Partial-Apply-Preflight-Design.md) | v1 partial apply preflight：resource snapshot / composite resolver 选型、internal scaffold、hit target metadata projection、接入前 gates |
| [V1-Partial-Apply-Runtime-Integration-Checkpoint.md](V1-Partial-Apply-Runtime-Integration-Checkpoint.md) | v1 partial apply runtime checkpoint：gate evidence 分层、第一 runtime seam 决策、backend adapter 方向 |
| [Diagnostics-Snapshot-v0.md](Diagnostics-Snapshot-v0.md) | diagnostics snapshot v0：snapshot 类型、provider 边界、CLI 文本冻结、最小实现候选 |
| [RetainedElementTree-Design.md](RetainedElementTree-Design.md) | 真正 retained element tree / local patch apply 的草案 |
| [Post-V1-MVP-Backlog.md](Post-V1-MVP-Backlog.md) | Post-V1 / MVP 任务表：default-on、D3D12 segmented、GA hardening、translator/scroll/typed id |
| [Default-On-Partial-Apply-Prep.md](Default-On-Partial-Apply-Prep.md) | default-on partial apply 前置设计：go/no-go gates、D3D12 验证、resource lifecycle、rollback |
| [D3D12-Segmented-Ownership-Prep.md](D3D12-Segmented-Ownership-Prep.md) | D3D12 segmented ownership 盘点：per-segment execute、dirty ranges、text cache、device-lost |
| [GA-Hardening-Plan.md](GA-Hardening-Plan.md) | GA 硬化清单：device lost recovery、display matrix、stability、performance、platform integration |
| [Project_Status_and_Todo.md](Project_Status_and_Todo.md) | 当前实现状态、阶段冻结线、短期候选任务 |

### 当前冻结线

| 冻结线 | 当前规则 |
|--------|----------|
| Diagnostics consolidation | ✅ 完成 / regression-only：Program diagnostics runner split、snapshot v0、debug UI bridge v0、formatter contracts 已封版；不新增 diagnostics channel / event bus / registry |
| Clip / scissor / text clip v0 | 只修 bug / regression；不扩 nested clip stack、默认启用策略、text batching、theme/control scope |
| Viewport / resize physical v0 | 只修 source-of-truth / resize regression；不引入 per-monitor DPI 切换、logical layout、multi-window scale 策略 |
| Layout dirty diagnostics v1 | 只修现有输出或分类 regression；不扩诊断面，不做 partial layout，不跳过 `StyleOnly` layout |
| StyleOnly plan diagnostics | 只修 formatter/smoke regression；不继续扩 formatter，不接入 `RenderPipeline.Build`，不替换 retained frame apply |
| V1 API/control boundary prep | design inventory complete / regression-only；`ControlVisualState projection helper` 已实现且仍为 PoC-owned |
| ControlVisualState / action attribute helpers | ✅ 完成 / regression-only：internal PoC projection helper、ActionId attribute helper、button attribute bundle helper；PoC source raw `ActionId` 构造已清；target/action 继续用 string；不改 renderer / VirtualNode wire contract |
| Scroll feedback vocabulary v0 | ✅ 第一小步完成：`ScrollFeedback` / `ScrollContainerMetrics` side-channel 由 translator 生成；旧 `Action<double>` max-scroll callback 仍保留；不移动 controller/state/pump |
| Translator feedback contract prep | ✅ internal seam 完成 / no behavior change：`TranslatorRenderPipelineFactory` 可替换 pipeline creation；默认仍走 `CounterStylePreset.Default`；translator 仍留在 `Irix.Poc` |
| Scroll settings provider design | ✅ decision recorded：runtime 继续 postponed；若重开，先做 fallback-only internal provider；不读 Win32、不接 controller |
| Retained / partial apply prep | ✅ V1 core gate-driven complete：每个 local reason 有 regression test；segmented reader edge guards、resource segment / lifecycle model、`SegmentedRetainedFrameOwner` shadow owner + opt-in diagnostic harness、`SegmentedRetainedFrameRuntimeOwner`、default-off `RetainedRenderFrameSegmentOwnership`、default-off `RetainedRenderFrameHandoffHarness`、default-off `DrawingBackendCompositor` selected render-source path、strict freshness/range/segment guards、multi-`FrameDrawingResources` exact-once lifecycle、owner-side hit-test ownership、handoff counter semantics、segment-local dirty-range routing、default-off `SegmentedRetainedFrameProductionOwnerFeed`、`DrawingBackendCompositorShadowProbe`、fallback reporting reasons、accepted partial four-piece atomicity、failed partial owner-state preservation、style-only default-off pre-switch、production runtime evidence 与 no-change coverage 已落地；`PartialApplyIntegrationGateChecklist.CanHookUpPartialApply=true`，但仍不默认启用、不改 public API / `IDrawingBackend.Execute` / D3D12 / CLI diagnostics |

### 下一步候选

- 当前主线：[Translator feedback contract draft](V1-Translator-Feedback-Contract-Prep.md) 已完成 internal factory seam；默认行为不变，translator 仍留在 `Irix.Poc`，不新增 framework API。
- 已封版：`v1 API/control boundary prep` 为 design inventory complete / regression-only；只修 boundary 文档 regression。
- 已实现：`ControlVisualState projection helper`、`Control action attribute helper`、`Button attribute bundle helper` 仍为 PoC-owned internal code；PoC source raw `ActionId` 构造已清；target/action 继续用 string，不引入 typed id wrappers。
- 已实现：`ScrollFeedback` / `ScrollContainerMetrics` vocabulary v0 作为 translator side-channel；旧 `Action<double>` callback 继续驱动 runtime 行为。
- 已记录：[Scroll settings provider design](V1-Scroll-Settings-Provider-Prep.md) 决策为 runtime 继续 postponed；若重开，先做 fallback-only internal provider，不读 Win32、不接线到 controller。
- 已记录：[Retained / partial apply prep](V1-Retained-Partial-Apply-Prep.md)、[Partial apply preflight design](V1-Partial-Apply-Preflight-Design.md) 与 [runtime integration checkpoint](V1-Partial-Apply-Runtime-Integration-Checkpoint.md)：default-off selected segmented render-source path 已从可执行推进到 gate-driven core complete；所有 partial apply integration gates 已 satisfied，`CanHookUpPartialApply=true`；default-off 行为与现有 production path 等价；enabled internal path 覆盖 selected execution、fallback、stale/missing/rejected owner、malformed guard、backend throw、hit-test ownership、counter semantics、dirty-range routing、resource lifecycle、retained root update 与 diagnostics no-change。
- **已修复（2026-05-13）：** D3D12 resolver misrouting bug — `D3D12DrawingBackend._resources` 被每次 `Execute` 覆写，导致 `EndFrame` 用最后一个 resolver 解析所有 text run 的 style。修复：`TextData` 新增 `ResolvedStyle` + `Resolver` 字段，`Execute` 中 eager resolve style 并存储 per-text-run resolver。Device-removed guard — `DrawingBackendCompositor.RenderAsync` non-handoff path 补 try/finally。详见 [D3D12-Segmented-Ownership-Prep.md](D3D12-Segmented-Ownership-Prep.md)。
- **已验证（2026-05-13）：** D3D12 text cache safety — cache keys 使用 `TextStyle` value equality（非 `ResourceHandle`），跨 resolver 安全。Default-on go/no-go checklist — **GO**：Gate 1-5 全部满足，Counter PoC D3D12 smoke test 通过（多刷新率，无渲染错误，无 crash）。HiDPI 下功能正常但因缺 app.manifest 被系统拉伸略模糊（非阻塞 visual quality issue）。详见 [Default-On-Partial-Apply-Prep.md](Default-On-Partial-Apply-Prep.md)。
- **已翻转（2026-05-13）：** Default-on partial apply — `Program.cs` 预设启用 partial apply（`--no-partial-apply` 可显式关闭）。所有 435 tests 通过。
- **已实现（2026-05-13）：** Platform-neutral display scale pipeline — `DisplayScale` 类型（`Irix.Drawing`），compositor 持有 scale boundary，layout 在 logical units 工作，draw commands/hit targets/text styles 缩放回 physical pixels。`WM_DPICHANGED` 运行时处理：窗口移动到不同 DPI 屏幕或系统缩放变化时，compositor/translator 自动更新 scale 并触发 relayout。`TextStyle.FontSize` 按 `DisplayScale` 缩放，确保文字与矩形视觉比例一致。所有 460 tests 通过。
- **手测通过（2026-05-13）：** Display scale pipeline — 100% / 150% / 200% DPI 下文字、按钮、hit-test、scroll、resize、partial apply 均正常；运行时改系统缩放后下一幀 relayout 正确，文字/矩形/hit-test 不错位。39 DisplayScale regression tests 覆盖 command rect、clip bounds、hit target、text font size、logical viewport（1.0/1.25/1.5/2.0）。所有 476 tests 通过。
- 暂缓：typed id wrappers、scroll extraction、settings provider、pure controller extraction、state ownership、pump/scheduler、translator promotion；StyleOnly 只新增 internal/default-off pre-switch，不跳过 layout，不接入 public API，不改 `RenderPipeline.Build`。
- 暂缓：unified diagnostics channel / event bus / registry；Program diagnostics runner split 已封版为 regression-only。
- **下一步：** Default-on partial apply go/no-go 已达 GO 结论；下一步可执行 default-on 翻转（单选项变更）。Post-V1 backlog 已拆为 4 batch，GA hardening first batch 已规划 5 项实现步骤。详见 [Post-V1-MVP-Backlog.md](Post-V1-MVP-Backlog.md)、[GA-Hardening-Plan.md](GA-Hardening-Plan.md)。

### IRIX-V1 收口任务表

| 任务ID | 优先级 | 主题 | 状态 | 下一步 |
|--------|--------|------|------|--------|
| IRIX-V1-001 | P0 | Diagnostics consolidation | Regression-only | 只修 `--diagnose*` / debug UI / formatter regression；不新增 diagnostics channel。 |
| IRIX-V1-002 | P0 | Controls-boundary helpers | Regression-only | Helper 保持 PoC-owned internal；不继续拆、不提升 framework。 |
| IRIX-V1-003 | P0 | Scroll feedback vocabulary v0 | Regression-only | `ScrollFeedback` side-channel 只补测/修 regression；旧 `Action<double>` 继续驱动 runtime。 |
| IRIX-V1-004 | P1 | Translator feedback contract / internal seam | Internal seam 完成 | `TranslatorRenderPipelineFactory` 已允许 internal style/pipeline creation 替换；默认仍走 `CounterStylePreset.Default`，不做 promotion。 |
| IRIX-V1-005 | P1 | Scroll settings provider design | Decision 完成 | Runtime 继续 postponed；若重开，第一步只做 fallback-only internal provider；不抽 controller/state/pump。 |
| IRIX-V1-006 | P1 | Translator feedback regression tests | Focused tests 完成 | 覆盖 default factory 等价、style source 等价、pipeline creation 不改变 layout viewport / scroll feedback / diagnostics。 |
| IRIX-V1-007 | P2 | Typed identity wrappers | Postponed | 保留设计词汇；不新增 public API，string id 行为不变。 |
| IRIX-V1-008 | P2 | Scroll extraction | Postponed | 不抽 `ScrollController` / `ScrollState` / `ScrollFramePump`。 |
| IRIX-V1-009 | P2 | `WindowDrawCommandTranslator` promotion | Postponed / seam only | Translator 留在 `Irix.Poc`；style/pipeline creation 只是 internal seam，viewport/retained-tree/typed feedback 仍未 promotion。 |
| IRIX-V1-013 | P2 | Retained planner / partial apply V1 core | **Default-on (2026-05-13)** | Default-on selected segmented render-source path；go/no-go 全部满足；`--no-partial-apply` 可回退；不改 public API / `IDrawingBackend.Execute` / D3D12 架構。 |
| IRIX-V1-014 | P0 | Display scale pipeline | **Complete (2026-05-13)** | 平台中性 `DisplayScale` 模型；compositor-owned scale boundary；text style/font scaling；`WM_DPICHANGED` runtime handling；hit-test/clip/scroll scale consistency；39 DisplayScale regression tests。详见下方手测记录。 |
| IRIX-V1-010 | P3 | StyleOnly fast-path | Default-off pre-switch only | `StyleOnlyFastPathOptions` 已作为 internal/default-off pre-switch；不跳过 layout，不接入 `RenderPipeline.Build`，不启用新行为。 |
| IRIX-V1-011 | P3 | Unified diagnostics channel / event bus / registry | Postponed | 不新增全局 diagnostics abstraction。 |
| IRIX-V1-012 | P0 | 当前进度判断 | V1 core complete | PoC v1 core architecture-complete / default-off；所有 V1 core gate satisfied，`CanHookUpPartialApply=true`；MVP/GA 仍未完成。 |

---

### V1 Core Completion Report（2026-05-13）

**结论：PoC V1 core 架构闭环已完成（default-off），不等价于 MVP/GA。**

V1 core 的完成边界是：internal/default-off selected segmented render-source path 能在所有 integration gate 满足时执行，且不改变任何现有 public API、backend contract、CLI 输出或默认行为。

| 维度 | 状态 | 说明 |
|------|------|------|
| `CanHookUpPartialApply` | `true` | 8/8 gate satisfied，hardcoded，无 runtime mutation |
| Default-off 行为等价 | 已验证 | disabled feed/compositor 与 direct pipeline 完全等价（435 tests pass） |
| Selected path | internal/default-off | 仅 `StyleOnlyFastPathOptions.Enabled` 或 `DrawingBackendCompositorHandoffOptions.Enabled` 激活 |
| Gate evidence 一致性 | 已验证 | 4 个 gate checklist tests 覆盖 satisfied/postponed/evidence 分层 |
| `RenderPipeline.Build` | 未改变 | layout 仍全量 rebuild，StyleOnly 不跳过 layout |
| `RetainedRenderFrame.TryReadFrame` | 未改变 | selected path 不写入 retained frame |
| `IDrawingBackend.Execute` | 未改变 | per-segment execute 通过 adapter wrapper，不改 backend 签名 |
| D3D12 | 未触及 | segmented ownership 仅在 test backend 验证 |
| CLI diagnostics | 未改变 | handoff result 仅 internal，不出现在 `--diagnose` 输出 |

**V1 core 完成 ≠ MVP/GA：** 以下仍需后续阶段完成（见 [Post-V1-MVP-Backlog.md](Post-V1-MVP-Backlog.md)、[Default-On-Partial-Apply-Prep.md](Default-On-Partial-Apply-Prep.md)、[D3D12-Segmented-Ownership-Prep.md](D3D12-Segmented-Ownership-Prep.md)、[GA-Hardening-Plan.md](GA-Hardening-Plan.md)）。

---

### Post-V1 / MVP Shortlist

下一阶段任务按优先级排列。所有任务均不在 V1 core 范围内。

| 优先级 | 任务线 | 内容 | 前置条件 |
|--------|--------|------|----------|
| P0 | Default-on partial apply | 将 selected segmented render-source path 从 default-off 提升为 default-on；需要 GA 级别的 platform matrix 验证 | D3D12 segmented ownership、GA hardening |
| P0 | D3D12 segmented ownership | 让 D3D12 backend 正确处理 per-segment execute + segment-local dirty ranges；验证 GPU resource lifecycle | D3D12 device-lost recovery |
| P0 | GA hardening | 平台矩阵测试（240Hz/120Hz/60Hz、DPI scaling、multi-monitor）、device-lost recovery、性能 profiling | 上述两项 |
| P1 | Translator promotion | 将 `WindowDrawCommandTranslator` 从 `Irix.Poc` 提升到 framework 层；typed feedback contract | Translator feedback contract prep |
| P1 | Typed id wrappers | 将 string ActionId / target identity 替换为 typed wrappers | API control boundary prep |
| P1 | Scroll extraction | 将 `ScrollController` / `ScrollState` / `ScrollFramePump` 从 PoC 抽取到 framework | Scroll settings provider design |
| P1 | Settings provider | 读取 Windows `SPI_GETWHEELSCROLLLINES` 等系统设置；fallback-only internal provider | Scroll extraction |
| P2 | Unified diagnostics channel | 替代 current per-component diagnostics；event bus / registry | Diagnostics consolidation freeze |
| P2 | StyleOnly layout skip | 真正跳过 `StyleOnly` layout rebuild；在 dirty classify 后、layout rebuild 前 fast-path | Default-on partial apply |
| P2 | Retained element tree | 真正的 retained element tree + local patch apply | RetainedElementTree-Design draft |
| P3 | Resource cache / stable global handles | D3D12-specific 资源缓存与跨帧稳定 handle | D3D12 segmented ownership |

---

## 1. 项目定位

Irix 当前是一个**早期原型期**的原生 .NET UI 框架项目。

**核心方向概要**（详细论述见 [设计文档 §1~§3](/d:/source/Irix/docs/Irix_Framework_Design.md)）：

- v1 / Windows-only PoC 以 `D3D12` 为唯一图形后端（[ADR-001](/d:/source/Irix/docs/Irix_Framework_Design.md#附录-b架构决策记录索引-adr)）
- Drawing 层采用 `DrawCommand + IDrawingBackend` 隔离具体 backend API（[ADR-002](/d:/source/Irix/docs/Irix_Framework_Design.md#附录-b架构决策记录索引-adr)）
- UI 交付层分两条线：`Local UI Remoting`（免费/开源）与 `Remote UI Delivery`（商业版）
- `MVVM Bridge` 仅为编译期 authoring layer（[ADR-007](/d:/source/Irix/docs/Irix_Framework_Design.md#附录-b架构决策记录索引-adr)）

**Phase / 版本映射**（详见 [设计文档 §12](/d:/source/Irix/docs/Irix_Framework_Design.md#12-分阶段交付计划)）：

| Phase | 版本 | 当前状态 |
|-------|------|---------|
| Phase 1 | v1.0 基础 | 🚧 进行中（核心闭环已打通） |
| Phase 2 | v1.0 MVP | 🚧 部分能力提前验证（局部 diff、D3D12 矩形/文本、CI） |
| Phase 3 | v1.0 GA | ❌ 未开始（产品化硬化尚未开始） |
| Phase 4 | v1.x | ❌ 未开始 |
| Phase 5 | v2.0 | ❌ 未开始 |

---

## 2. 当前代码真实状态

下面这部分描述的是**当前仓库代码已实现的事实**，不是设计目标。

### 已有内容

- `Irix.Core`
  - 已有 `IApplication<TModel, TMessage>`、`Runtime<TModel, TMessage>`、`Command<TMessage>`、`IMessageDispatcher<TMessage>`
  - 已有基础 `VirtualNode` / `VirtualNodePatch` 数据模型
  - 已有 `PatchBatch` / `PatchMemoryOwner` / `IVirtualNodePatchSink`
- `Irix.Platform`
  - 已有平台抽象定义
- `Irix.Platform.Windows`
  - 已有 Windows 宿主 PoC
  - 已有屏幕枚举、窗口线程、原生窗口创建、输入事件流、拓扑变化通知
  - Windows-targeted projects 使用 `net10.0-windows10.0.17763.0` 编译，并声明 Windows 10.0.10240+ 为最低 API 边界
  - Win32 互操作优先走 `CsWin32`
  - `Irix.Platform.Windows` 已启用 `CsWin32RunAsBuildTask=true`，CsWin32 绑定以实体 `obj/.../Generated/CsWin32/Windows.Win32.NativeMethods.g.cs` 参与编译，并显式排除 CsWin32 analyzer/source-generator 资产，避免依赖易失效的 Roslyn `roslyn-source-generated://` 虚拟文档
  - 已有 `D3D12Renderer`，使用 CsWin32 生成的裸指针 COM 包装（`allowMarshaling: false`），支持设备创建、交换链、清屏、矩形 + 文本帧合成、呈现、resize
  - 已有 `D3D12Renderer2D`，运行时 HLSL 编译 + 顶点缓冲区渲染彩色矩形
  - 已有 `D3D12TextRenderer`，通过 D3D11On12 + Direct2D + DirectWrite 在 D3D12 back buffer 上叠加文本
  - 已有 `D3D12DrawingBackend`（Irix.Poc），Phase 3：FillRect → D3D12 矩形渲染，DrawTextRun → DirectWrite 文本渲染
- `Irix.Rendering`
  - 已有 `ICompositor`
  - 已有 `CompositorLoop`，负责异步消费 `PatchBatch`；普通 diff patch 可通过 `PublishAndWaitRenderAsync` 等待对应真实 `RenderAsync` 完成；合并式 `RequestRenderAsync` / `RequestRenderAndWaitAsync` 仅用于 resize 等不改变 VirtualNode 树的显式重绘请求，避免连续 `WM_SIZE` 产生无界空 patch 队列
  - 已有 `ConsoleCompositor` 与 `CompositeCompositor`
  - 已有 `LayoutTreeBuilder`、`LayoutElement`、`DrawCommandRecorder` 过渡骨架
  - `RenderPipeline` 已引入 retained layout：缓存 `LayoutElement[]`，树/视口不变时复用
  - 已有 `RenderFrameBatch` / `HitTestTarget` / `FrameDrawingResources`，并行承载命中数据、frame-local 文本内容与文本样式资源
  - 已有 `DrawingBackendCompositor`，桥接 `ICompositor` → `IDrawingBackend`，缓存命中目标
- `Irix.Drawing`
  - 已拆出独立项目骨架
  - 已有 `DrawCommand`、`FrameContext`、`DrawCommandBatch`、`IDrawingBackend` 最小类型
  - `DrawCommand` 已移除内联 `string? Text`，改为 `TextSlice` 引用 frame-local 文本 arena；`ResourceHandle` 已用于 `TextStyle` 资源句柄
- `Irix.Poc`
  - 已有 Counter 示例应用
  - 已有 `WindowVisualCompositor`，能消费当前 `RenderFrameBatch` 并更新 PoC Window 内容与命中目标
  - 已有 `WindowBackend`，可将 `DrawCommand + HitTestTarget[]` 翻译成 PoC Window 内容元素与命中目标
  - 已有 `PoCDrawingBackend`，首次实现 `IDrawingBackend` 接口，验证 DrawCommand → WindowContentElement 链路
  - 已有 `D3D12DrawingBackend`，D3D12 渲染路径已接入 PoC（Phase 3: 矩形 + DirectWrite 文本渲染）
  - 已将 Counter 示例中的输入映射抽到独立 `CounterInputRouter`
  - 已打通：窗口创建 -> 输入 -> runtime dispatch -> patch 发布 -> PoC 可视化
- `Irix.Core.Tests`
  - 已有最基础 runtime 测试
  - 已有 layout / draw pipeline / `WindowBackend` 基础测试与最近的回归测试

### 关键进展与仍需补齐的内容

- D3D12 渲染已接入 PoC：`D3D12Renderer` 使用 CsWin32 生成的裸指针 COM 包装（不再手写 vtable），`D3D12DrawingBackend` 已支持 FillRect 矩形渲染与 DirectWrite 文本叠加
- 还没有 Skia + D3D12 集成
- retained layout 与 draw command pipeline 已有最小闭环，但尚未实现正式 retained element tree、增量 layout dirty 标记和局部 patch 应用
- `VirtualNodeDiffer` 已实现局部 diff：递归深比较 + keyed reconciliation + Update/Add/Remove patches；`default` 树边界处理已完善；279 个测试用例覆盖各场景
- `DrawCommand` 已移除内联 `string? Text`，改为 `TextSlice` + `IFrameResourceResolver` 传递文本内容；`ResourceHandle` 已回归资源职责并用于 `TextStyle`
- DirectWrite backend 已缓存 bounded `IDWriteTextFormat` 与 bounded `IDWriteTextLayout`；显式 glyph atlas/cache 尚未实现，当前仍委托 DirectWrite 内部 glyph rasterization/cache
- 渲染热路径仍有托管分配：`DrawCommandRecorder` 每帧从 `FrameDrawingResources` 静态池 Rent，`RenderFrameBatch.Dispose()` 归还；`D3D12DrawingBackend` 使用 `FrameRenderList<T>`（ArrayPool 背板），每帧 Reset 而非 new；`DrawCommand` 录制走小批量 `stackalloc` + 大批量 pooled owner。`FrameTextArena.Seal()` 从 `ArrayPool` 租用 `char[]` 而非生成 `string`。热路径每帧仅剩 `ArrayPool` rent/return（非 GC 分配）
- `PatchBatch` 已携带 `Root` 属性，消费者不再需要从 `Memory` 中反推根节点
- 测试覆盖已扩展至 279 个测试（含 diff、DrawCommand 文本传递、FrameTextArena、FrameDrawingResources、arena reuse、pool Rent/Return、TextSlice 生命周期、patch 应用、文本渲染正确性、CompositorLoop 合并重绘请求、普通 diff patch render wait、Runtime.DispatchAndWaitAsync render completion wait、render request 与 empty diff 区分、RetainedTree patch apply（去重升序 dirty set）、LayoutTree 中间结构（DFS index → element range 映射、VirtualNodeKind 语义）、增量布局 dirty range 计算与合并（父子重叠/相邻区间合并）、DrawCommand range 映射（element→command range）、dirty command range 计算与传递、RangeUtils 工具类、RetainedCommandBuffer 局部替换、RetainedRenderFrame 纯 TryApplyPartial 失败路径、Dispose 安全释放、资源一致性保护与零分配读取、资源 generation 跟踪与显式所有权、FrameDrawingResources Retain/Release/Return 幂等性、DrawingBackendCompositor retained frame 与 partial apply pilot、cross-frame partial guard、compositor 诊断计数、clip scissor capability diagnostic、layout dirty v0/v1 diagnostics、retained layout rebuild reason、retained layout、DrawingBackendCompositor、所有权转移、ScrollFrame 首帧 delta、Counter 默认可滚动内容、Windows raw wheel 方向、scroll diagnostic smoke、input ownership v0、model-owned input visual refresh、input diagnostic smoke、input ownership event log、Button visual state v0、style preset v0、Counter debug UI gating、style preset diagnostics、debug viewport diagnostics row、root ScrollContainer viewport clip v0、scissor effective clip 纯计算、scissor 整数转换、FillRect scissor smoke、pipeline-driven scissor smoke、pipeline text clip smoke、empty-intersection skip、run-length scissor state changes、Diagnostic/Scissor mode 差异、D2D text clip v0、text clip smoke、empty text clip skip、default/full viewport text clip、resize viewport consistency diagnostic、synthetic resize layout viewport consistency、repeated same-size resize rebuild guard、render wait 计入真实 dt 与低频 render completion 回归等）
- `CompositorLoop` 已实现两类 render wait：普通 diff patch 的 `PublishAndWaitRenderAsync` 在对应真实 `RenderAsync` 完成后 complete，供 `Runtime.DispatchAndWaitAsync` 等待本次状态更新的真实帧；`RequestRenderAndWaitAsync` 保留给 resize / retained repaint 等无 diff 的显式重绘请求
- D3D12 resize 已改为 UI 线程只记录 pending size，Compositor 翻译/布局前应用 pending resize，并以 renderer 实际 swapchain 尺寸作为 layout viewport；fence event 由 renderer 持有 SafeHandle 且使用 auto-reset event，避免 GC 后 `E_HANDLE` 与 stale fence wait；交互运行默认关闭 ConsoleCompositor trace，swapchain 使用非拉伸 scaling；D3D12 窗口启用 external rendering 模式，避免 Win32 GDI `WM_PAINT`/erase 与 swapchain present 竞争
- `RenderPipeline` 已引入 retained layout：缓存上一帧的 `LayoutElement[]`，仅在树或视口变化时重新布局，否则复用缓存并重新录制 DrawCommand
- `IDrawingBackend` 已首次落地实现：`PoCDrawingBackend`（Irix.Poc）+ `DrawingBackendCompositor`（Irix.Rendering），验证了从 `RenderFrameBatch` → `IDrawingBackend` → `INativeWindow` 的完整链路

**数据流各阶段验证速查**（详见 [设计文档 §4.1](/d:/source/Irix/docs/Irix_Framework_Design.md#41-关键数据流本地模式v1)）：

| 阶段 | 状态 |
|------|------|
| 输入采集 → MPSC | ✅ 已验证 |
| 消息派发 → Update | ✅ 已验证 |
| View 构建 | ⚠️ 部分 |
| Diff / Patch | ✅ 局部 diff 已实现：Update/Add/Remove + keyed reconciliation；Move 仍待优化 |
| 布局 | ⚠️ Retained layout 已引入，未脱离硬编码常量 |
| 命令录制 | ⚠️ 基础可用，文本内容与 `TextStyle` 已通过 `FrameDrawingResources` 分离 |
| 帧消费 (CompositorLoop) | ✅ 已验证（含合并式显式重绘请求与 render completion await） |
| GPU 渲染 | ✅ D3D12 矩形渲染 + DirectWrite 文本叠加已接入 PoC |
| PoC 可视化 | ✅ 已验证 |

---

## 3. 已实现模块概览

### 3.1 Core

当前核心运行时已经具备最小 MVU 闭环：

- 初始化 model
- 构建初始 view tree
- 派发 message
- 执行 `Update`
- 重新构建 tree
- 生成 patch
- 发布到 patch sink

限制：

- `VirtualNodeDiffer` 已实现局部 diff 与 keyed reconciliation；`Move` 优化和下游增量 patch 应用尚未落地
- Drawing 层抽象已落地为 `DrawCommand + IDrawingBackend + FrameDrawingResources`，文本内容与 `TextStyle` 资源模型已经分离；仍需继续稳定画刷、图片、路径、裁剪与透明度模型

关键文件：

- [Runtime.cs](/d:/source/Irix/src/Irix.Core/Runtime.cs)
- [VirtualNodeModels.cs](/d:/source/Irix/src/Irix.Core/VirtualNodeModels.cs)
- [VirtualNodeDiffer.cs](/d:/source/Irix/src/Irix.Core/VirtualNodeDiffer.cs)

### 3.2 Windows 平台宿主

当前 Windows PoC 已支持：

- 创建主窗口
- 枚举屏幕
- 收集原始输入事件
- 监听拓扑变化
- 基础窗口生命周期管理

限制：

- 仍是 PoC 宿主，不是最终平台层
- 多屏子视口、Ghost Event Window 都还不是正式交付状态

关键文件：

- [WindowsPlatformHost.cs](/d:/source/Irix/src/Irix.Platform.Windows/WindowsPlatformHost.cs)
- [WindowsNativeWindow.cs](/d:/source/Irix/src/Irix.Platform.Windows/WindowsNativeWindow.cs)
- [WindowsScreenEnumerator.cs](/d:/source/Irix/src/Irix.Platform.Windows/WindowsScreenEnumerator.cs)

### 3.3 Rendering / PoC 可视化

当前渲染层已经有“PoC 可视化层 + 初步 D3D12 GPU backend + layout/draw pipeline 骨架”，但还不是正式产品级 rendering 架构。

`WindowVisualCompositor` 当前主要负责 PoC backend 可视化：

1. 消费 `RenderFrameBatch`
2. 生成 PoC Window 内容元素
3. 维护命中目标，并明确与 `DrawCommand` 分离传递
4. 空帧到来时主动清空窗口元素与命中目标，避免上一帧命中信息残留

布局与命令录制已经开始沉到 `Irix.Rendering`；D3D12 backend 已能渲染矩形和文本，但离正式 retained tree、增量布局、裁剪、透明度和资源缓存还有距离。

最近已确认并修复的 PoC 问题：

- `WindowDrawCommandTranslator` 若误用 `new LayoutStyle()`，会因为 `record struct` 默认值全为 `0`，导致文本/按钮高度都退化为 `0`
- `WindowBackend` / `WindowsNativeWindow` 现已补通颜色传递，PoC Window 不再忽略 `DrawCommand.Color`
- `DrawCommand` 不再携带 PoC 专用的点击 `ActionId`；命中目标改为通过 `RenderFrameBatch` 并行传递，避免污染正式绘制边界

关键文件：

- [DrawingPrimitives.cs](/d:/source/Irix/src/Irix.Drawing/DrawingPrimitives.cs)
- [IDrawingBackend.cs](/d:/source/Irix/src/Irix.Drawing/IDrawingBackend.cs)
- [LayoutTreeBuilder.cs](/d:/source/Irix/src/Irix.Rendering/LayoutTreeBuilder.cs)
- [DrawCommandRecorder.cs](/d:/source/Irix/src/Irix.Rendering/DrawCommandRecorder.cs)
- [CompositorLoop.cs](/d:/source/Irix/src/Irix.Rendering/CompositorLoop.cs)
- [WindowVisualCompositor.cs](/d:/source/Irix/src/Irix.Poc/WindowVisualCompositor.cs)

### 3.4 Tests

当前测试状态：

- `Irix.Core.Tests` 已有 runtime 测试、diff 测试、layout/draw pipeline 基础测试与最近的回归测试
- 已补 `VirtualNodeDiffer` 局部 diff、keyed reconciliation 与空 PatchBatch 检测测试
- 已补 `PatchBatch.Root` 属性验证
- 已补 `WindowDrawCommandTranslator` 默认布局回归测试
- 已补 `WindowBackend` 颜色映射断言
- 已补 `WindowVisualCompositor` 命中边界与空帧清理测试
- 已补 Counter PoC 输入路由映射测试
- 已补 `PatchBatch` / `DrawCommandBatch` / `RenderFrameBatch` / `CompositorLoop` 基础所有权与释放路径测试
- 已补 `CompositorLoop` 所有权释放与合并式重绘请求测试
- 还没有异常/取消路径测试

当前已知行为记录：

- `PatchBatch.Dispose()` 后再次访问 `Memory`，当前实现会因切片边界失效而抛 `ArgumentOutOfRangeException`
- `DrawCommandBatch.Dispose()` 后再次访问 `Memory`，当前实现返回空内存
- `CompositorLoop` 在正常渲染路径中会负责释放传入的 `PatchBatch` 与翻译产出的 `RenderFrameBatch`（其内部持有 `DrawCommandBatch`）

关键文件：

- [RuntimeTests.cs](/d:/source/Irix/tests/Irix.Core.Tests/RuntimeTests.cs)

---

## 4. 当前架构决策

> 详细论述与权衡分析见 [设计文档附录 B：ADR 索引](/d:/source/Irix/docs/Irix_Framework_Design.md#附录-b架构决策记录索引-adr)。此处仅列出当前生效状态与尚未落地的决策。

### 已确认（详见 ADR-001 ~ ADR-016）

- D3D12 作为 v1 唯一图形后端 / Skia 仅作为 backend adapter（ADR-001, ADR-006）
- DrawCommand + IDrawingBackend 隔离层（ADR-002）
- IMemoryOwner 所有权转移模型（ADR-003）
- HitTestTarget 与 DrawCommand 并行传递（ADR-004）
- 单线程 Update 串行执行（ADR-005）
- MVVM Bridge 为编译期前端（ADR-007）
- Local UI Remoting 为免费/开源方向（ADR-008）
- 不做运行时 XAML/IXAML 解析（ADR-009）
- VirtualNode 采用轻量不可变结构（ADR-010）
- DrawCommand 不内联文本，通过 frame-local `TextSlice + IFrameResourceResolver` 传递文本内容（ADR-011, ADR-015）
- TextStyle 通过 `ResourceHandle` 引用，DirectWrite backend 缓存 `IDWriteTextFormat` / `IDWriteTextLayout`（ADR-016）
- PatchBatch 携带 Root 属性，消费者直接使用而非从 Memory 反推（ADR-012）
- D3D12 互操作使用 CsWin32 `allowMarshaling: false` 生成的裸指针 COM 包装；Windows 平台项目使用 `CsWin32RunAsBuildTask=true` 将绑定生成为实体 `obj` 编译输入，并排除 CsWin32 analyzer/source-generator 资产，减少 Roslyn source-generated virtual document 噪音（ADR-013）
- Windows PoC 文本渲染使用 DirectWrite / Direct2D over D3D11On12（ADR-014）
- 文本内容优先使用 frame-local arena，不在早期阶段引入无边界全局字符串池（ADR-015）
- `TextStyle` 使用 `ResourceHandle`，backend 负责缓存对应原生文本资源（ADR-016）

### 未确认或尚未落地

- `Skia + D3D12` 的最终 backend adapter 方案
- 本机 IPC 选型：
  - 命名管道
  - loopback gRPC
  - 其他本机 RPC 封装
- `DrawCommand` 的最终字段设计
- retained tree / layout tree 的具体对象模型
- `IXAML` 的最小语法子集与 typed context 规则
- binding / converter 的最小支持集合
- visual state / template / resource 的受限模型

---

## 5. 推荐阅读顺序

新开发者或 AI 工具建议按以下顺序建立上下文：

1. [Irix_Framework_Design.md](/d:/source/Irix/docs/Irix_Framework_Design.md)
2. [Runtime.cs](/d:/source/Irix/src/Irix.Core/Runtime.cs)
3. [VirtualNodeModels.cs](/d:/source/Irix/src/Irix.Core/VirtualNodeModels.cs)
4. [LayoutTreeBuilder.cs](/d:/source/Irix/src/Irix.Rendering/LayoutTreeBuilder.cs)
5. [DrawCommandRecorder.cs](/d:/source/Irix/src/Irix.Rendering/DrawCommandRecorder.cs)
6. [WindowVisualCompositor.cs](/d:/source/Irix/src/Irix.Poc/WindowVisualCompositor.cs)
7. [WindowsPlatformHost.cs](/d:/source/Irix/src/Irix.Platform.Windows/WindowsPlatformHost.cs)

---

## 6. 最近最值得做的事

优先级按当前建议顺序排列。

### P0

- 在 `Irix.Drawing` 中继续稳定资源模型：画刷、图片、路径、裁剪与透明度；显式 glyph atlas/cache 留待自研 glyph 路径需要时再设计
- 继续把 `LayoutTreeBuilder` / `DrawCommandRecorder` 从 PoC 规则里抽成更通用的 pipeline
- 让 `WindowVisualCompositor` 保持为纯 PoC/backend 层，不再回流正式职责
- 梳理 `record struct` 风格配置对象的默认值策略，避免再次出现 `new XxxStyle()` 触发零值布局

### P1

- ✅ `VirtualNodeDiffer` 已从 `ReplaceRoot` 提升到局部 diff（Update/Add/Remove + keyed reconciliation），并纳入当前 279 个全量测试覆盖
- 增加 `PatchBatch` / `IMemoryOwner<T>` 异常、取消、释放路径测试
- 增加输入路由和命中测试的最小测试覆盖

### P2

- ✅ D3D12 基础渲染循环已搭建（CsWin32 裸指针 COM 包装，不再手写 vtable）
- ✅ `D3D12DrawingBackend` 已实现 `IDrawingBackend` 的 D3D12 路径并接入 PoC
- ✅ Phase 2: D3D12 矩形绘制已实现（`D3D12Renderer2D`：运行时 HLSL 编译 + 顶点缓冲区）
- ✅ Phase 3: D3D12 文本渲染已实现（D3D11On12 + Direct2D + DirectWrite overlay）
- 明确 `SkiaBackend` 只位于 backend adapter 层
- 为 `Local UI Remoting` 起草最小协议：`InputEvent`、`VirtualNodePatch`、`Ack / SeqId`
- 收敛轻量 `MVVM bridge` 的最小 binding 语法、`IXAML` 子集与代码生成边界

---

## 7. 当前待办清单

### Rendering / Drawing

- [x] 新建 `DrawCommand` 数据模型
- [x] 新建 `IDrawingBackend`
- [x] 新建 `FrameContext`
- [x] 新建 `DrawCommandBatch`
- [x] 实现 `WindowBackend`，使其消费 `DrawCommandBatch + HitTestTarget[]`
- [x] 将 `WindowVisualCompositor` 从 patch 直接消费改成 draw command 消费
- [x] 建立 `VirtualNodePatch -> LayoutTreeBuilder -> DrawCommandRecorder -> RenderFrameBatch -> WindowBackend` 过渡链
- [x] 打通 PoC Window 对 `DrawCommand.Color` 的映射
- [x] 搭 `D3D12` 基础渲染循环
- [x] 实现 `D3D12DrawingBackend`（`IDrawingBackend` 的 D3D12 实现，已接入矩形与文本）
- [x] 从手写 vtable 迁移到 CsWin32 生成的裸指针 COM 包装
- [x] 将 Windows 平台 CsWin32 生成模式切到 build task，生成实体 `Windows.Win32.NativeMethods.g.cs`，并从 PackageReference 资产中排除 CsWin32 analyzer/source-generator，避免编辑器反复请求过期 `roslyn-source-generated://` 文档
- [x] Phase 2: D3D12 矩形绘制（`D3D12Renderer2D`：运行时 HLSL 编译 + 顶点缓冲区）
- [x] 移除 D3D12 viewport 硬编码，接入真实窗口尺寸 + resize 支持
- [x] 添加 GitHub Actions CI（build + test + AOT check）
- [x] Phase 3: D3D12 文本渲染（D3D11On12 + Direct2D + DirectWrite overlay）
- [x] 将文本内容从 `ResourceHandle` 分离为 frame-local `FrameDrawingResources + TextSlice/IFrameResourceResolver`
- [x] 建立 `TextStyle` resource cache 与 DirectWrite bounded `TextFormat/TextLayout` cache
- [x] D3D12 device-removed 检测：`BeginFrame`/`RenderFrame`/`ClearAndPresent`/`ApplyResize` 中 `_deviceRemoved` 守卫 + try-catch `COMException` + `SucceededOrMarkDeviceRemoved` 显式 HRESULT 检查
- [x] D3D12 diagnostics 透传：`D3D12Renderer.GetTextDiagnostics()` / `ResetTextDiagnostics()` 暴露 `TextRendererDiagnostics`
- [x] Resize 稳定化：pending resize 模式 + `CompositorLoop.RequestRenderAsync` 合并重绘 + 布局前 apply + D2D target clear + D3D11 flush + fence event SafeHandle/auto-reset 修复 + swapchain 非拉伸 scaling + external rendering 跳过 GDI paint/erase
- [x] 交互体验清理：默认交互路径不再挂 `ConsoleCompositor`，需要 trace 时使用 `--console`；窗口创建后先启用 external rendering 并立即 `Show()`，再进行 D3D12/DirectWrite 初始化，减少 Windows app-starting 忙碌光标反馈时间且不覆盖系统 resize 光标
- [x] TextSlice 生命周期测试：frame resource return/arena reset 后旧 slice 必须失效
- [x] Patch diff 等价性测试：验证 Update/Add/Remove/ReplaceRoot/Keyed reconciliation 的布局等价性（注：非 RetainedTree.Apply）
- [x] 文本渲染正确性测试：英文、中文、emoji、混合 unicode、空文本、超长文本、按钮居中、DPI 缩放
- [x] D3D12 诊断 smoke mode：`--diagnose` 渲染 3 帧并输出 TextRendererDiagnostics 缓存统计；`--diagnose-resize` 循环 resize/render 并强制 GC，覆盖 fence handle 生命周期与 resize stress 路径
- [ ] 设计显式 glyph atlas/cache（仅当后续脱离 DirectWrite 或需要跨 backend glyph 资源复用时推进）
- [ ] device-lost recovery：重建设备、交换链、所有 GPU 资源（当前仅 fail-fast + `_deviceRemoved` 标志位 + `DeviceErrorReason` 字符串；设备丢失后所有 `BeginFrame`/`RenderFrame`/`ClearAndPresent` 跳过执行，不自动重建设备，不连续失败计数）

### Core

- [ ] 为 `VirtualNode` 增加更清晰的属性建模策略
- [ ] 设计 retained element tree
- [ ] 设计 layout tree
- [ ] 让 `LayoutTreeBuilder` 脱离 PoC 特有布局常量和控件假设
- [x] 将 diff 从 `ReplaceRoot` 提升到最小可用局部 diff
- [x] 增加 keyed reconciliation 设计草案并落地基础实现

### Input / Platform

- [ ] 明确焦点模型
- [ ] 明确命中测试与 layout box 的关系
- [ ] 明确是否继续保留 `Ghost Event Window` 为 v1.1 实验特性
- [ ] 多屏固定拓扑支持设计收口

### UI Delivery

- [ ] 起草 `Local UI Remoting` 协议
- [ ] 确定本机 IPC 方案
- [ ] 明确插件握手与能力协商
- [ ] 明确宿主的资源配额与安全边界
- [ ] 保证 `Local UI Remoting` 与 `Remote UI Delivery` 共用核心 UI 协议模型

### MVVM Bridge / DSL

- [ ] 定义 `IXAML` 的最小语法子集与 typed context 规则
- [ ] 定义 `OneWay` / `TwoWay` / `Command` binding 的代码生成模型
- [ ] 明确 converter 的最小支持集合，避免引入通用运行时 binding engine
- [ ] 明确 visual state 简化策略，优先编译为条件属性或条件分支
- [ ] 输出 bridge 非目标清单：不做 `DependencyProperty`、运行时 `XAML` 解析、完整模板/资源 runtime

### Tests

- [x] 为 diff 增加测试
- [x] 为 `PatchBatch.Dispose()` 路径增加测试
- [ ] 为 runtime command 执行增加测试
- [x] 为 Counter PoC 输入路由增加最小测试
- [ ] 为 hit testing 增加测试
- [x] 为 layout / draw pipeline 增加基础测试
- [x] 为 PoC 渲染回归增加最小测试
- [x] 为 `WindowVisualCompositor` 命中测试增加最小覆盖
- [x] 为 `CompositorLoop` 所有权转移增加最小测试
- [x] `CompositorLoop` 合并式 render request 行为测试：连续请求只排队一次、渲染中后补一帧、普通 empty diff 不等同 render request；`RequestRenderAndWaitAsync` 与普通 diff `PublishAndWaitRenderAsync` 都在对应 `RenderAsync` 完成后才 complete（279 个测试，全部通过）
- [x] 引入最小 `RetainedTree`：单次 DFS 遍历应用 ReplaceRoot/Update/Add/Remove patch，返回去重升序 dirty 节点索引集合；13 个测试覆盖 replace root、update、add、remove、keyed reconciliation、多 patch 组合、empty batch、diff→apply 等价性、dirty 排序去重、layout dirty v0、RenderPipeline dirty-driven rebuild、Translator RetainedTree 集成
- [x] `RenderPipeline` 接入 `RetainedTree`：Translator 持有 RetainedTree，diff batch 调用 Apply 并传递 dirty set，render request 只复用 retained tree；LayoutTreeBuilder 接受 dirty nodes 参数（v0 全量重建）
- [x] Layout dirty v0：`LayoutTreeBuilder.Build(root, viewport, dirtyNodes)` 接口已落地，当前为全量重建，dirty set 透传用于后续增量布局
- [x] Layout dirty v1 诊断闭环完成：见 [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md)；`RenderPipeline` 记录 `LastLayoutRebuildReason`、`LayoutRebuildCount` 与 `LastDirtyClassifications`，`--debug-ui` 显示 layout dirty 行且手测 hover/press/scroll/resize dirty reason 已通过；`--diagnose-input` 输出 hover-only / press / release dirty reason；真实 PoC 路径测试覆盖 hover-only `StyleOnly`、press `StyleOnly`、scroll `LayoutAffecting`、resize `ViewportChanged`、release `TextSizeAffecting` 与混合 dirty 优先级；本阶段冻结诊断扩展，当前仍全量 rebuild，不跳过 `StyleOnly`，不做 partial layout，不改 `LayoutTreeBuilder`
- [x] 建立 LayoutTree 中间结构：`LayoutTreeNode` 记录 DFS index、element kind、element range；`LayoutTreeResult` 携带 flat elements、tree nodes、dirty element ranges
- [x] dirty layout v1：dirty 非空时，构建 layout tree 并计算 dirty node 对应的 layout element ranges；单个 Text update 只影响 1 个 element range
- [x] dirty → affected layout range 映射：`RenderPipeline.LastDirtyElementRanges` 暴露最近一次 Build 的 dirty element ranges，可定位需要重录的 draw commands
- [x] 建立 DrawCommand range 映射：`DrawCommandRecorder` 输出 `ElementCommandRange[]`（element index → command range），Button → 2 commands，Text/Rectangle → 1 command
- [x] 增量录制 v0：dirty element range 非空时生成完整 command batch，额外输出 dirty command ranges（`DrawCommandRecordResult.DirtyCommandRanges`），为 partial redraw / command patch 做准备
- [x] Dirty range 合并：父子同时 dirty 时合并重叠区间，相邻区间合并，输出最小 dirty set
- [x] 抽取 `RangeUtils` 工具类：统一 element range 和 command range 的合并/映射/查找逻辑
- [x] `RenderFrameBatch` 携带 `DirtyCommandRanges` 字段：compositor/backend 能拿到 dirty command range
- [x] `DrawingBackendCompositor` 记录 `LastDirtyCommandRanges`：暂时仍全量渲染，但 diagnostics/log/test 能看到 dirty ranges
- [x] `RetainedRenderFrame` 资源一致性保护：`TryApplyPartial` 为纯函数，失败只返回 `false` 不做 side effect；检查 `ReferenceEquals` + `FrameId` generation 一致 + command count 匹配；fallback ownership 仅在 compositor 一处处理（ReleaseResources + ApplyFull + RetainResources）
- [x] `RetainedRenderFrame` 零分配读取：`TryReadFrame` 暴露 `ReadOnlySpan<DrawCommand>` + `IFrameResourceResolver`，backend 可直接读取 retained frame 而不经过 `ToBatch()` 复制
- [x] `DrawingBackendCompositor` 持有 `RetainedRenderFrame`：每次 `RenderAsync` 更新 retained frame（full 或 partial），backend 通过 `TryReadFrame` 消费，`LastPartialApplySucceeded` 暴露 partial apply 结果
- [x] Partial apply 策略：当前 compositor 跨帧 full apply；partial 仅限同 frame scope（same `FrameDrawingResources` instance + same `FrameId` generation + same command count + dirty ranges 非空）；`TryApplyPartial` 纯函数，失败不污染状态；compositor 是 fallback 唯一处理点
- [x] 资源所有权模型：`RetainedRenderFrame` 不默认接管资源；`ApplyFull` 仅存储引用；compositor 显式调用 `RetainResources` / `ReleaseResources` 管理生命周期；`Dispose()` 内部调用 `ReleaseResources()` 兜底，直接 dispose 不泄漏 retained resources
- [x] 资源 generation 跟踪：`FrameDrawingResources.FrameId` 在每次 `Rent()` 时递增，`Return()` 时保留；compositor 用 `_lastAppliedFrameId` 做跨帧 guard，防止 recycled pooled instance 被误判为同一 frame scope
- [x] 跨帧 partial apply 禁用：compositor 层在 `Invalidate` 或 FrameId 不匹配时强制 fallback full apply，正确性优先
- [x] `TryApplyPartial` 失败路径测试：same resources + dirty ranges + command count mismatch 时返回 `false` 且不污染 retained buffer/resources；`Dispose()` 安全释放 retained resources 回池
- [x] `FrameDrawingResources` Retain/Release/Return 幂等性测试：double Release、Release after Return、Retain then batch Dispose、FrameId 递增 — 不会 double-return / 负引用计数
- [x] `DrawingBackendCompositor` 诊断计数：`RenderCount`、`PartialApplyCount`、`FullApplyCount`、`EmptyFrameCount`、`LastDirtyCommandRanges`；`--diagnose` 模式输出 partial hit rate 与 dirty ranges
- [x] ADR-017 跨帧资源策略设计草案：明确未来 partial rendering 需要稳定 text handle / resource snapshot / per-frame full resources 之一；v1 保持 full apply，partial 仅限同 frame scope pilot
- [x] `FrameDrawingResources` API 收紧：`Reset()` 改为 `internal`，防止外部误清 retained arena；`Dispose()` 加 retained 安全释放（`Release()` before dispose），避免池腐蚀
- [x] `IDirtyRangeAware` 可选接口：backends 可实现 `SetDirtyCommandRanges` 获取只读 dirty ranges；`D3D12DrawingBackend` 已实现；compositor 在 `Execute` 前传播 dirty ranges；不改 `IDrawingBackend` 签名，渲染行为不变
- [x] 热路径分配复查：`TryApplyPartial` 移除不必要的 `_hitTargets = [.. batch.HitTargets]`（partial apply 不改变 hit targets）；`ApplyFull` 和 compositor 的 hit target 分配保留（must own / thread-safety）；`ToBatch().ToArray()` 仅测试路径；`DrawCommandRecorder` stackalloc→ToArray 保留（stackalloc 无法逃逸方法）
- [x] `Reset()` guard 语义修正：retained 状态下 `Reset()` 抛 `InvalidOperationException`；`Rent()` 做防御性全量重置（`_sealed`/`_retained`/arena/styles），防止并行测试通过静态池泄漏 sealed 状态
- [x] `IDirtyRangeAware` 接线测试：`DirtyRangeTrackingBackend` 验证 compositor 在 `Execute` 前传播 dirty ranges，backend 接收到的 ranges 与 compositor 一致
- [x] `--diagnose` 输出 compositor 与 backend dirty ranges 对比，`Dirty ranges aligned: True` 确认两者一致；D3D12 仍 full execute，dirty ranges 仅用于诊断
- [x] 收敛布局硬编码：`LayoutStyle` 新增 `RectangleHeight` 参数，替换 `LayoutTreeBuilder` 中硬编码的 `48`；所有布局参数（padding/spacing/height/width）均来自 `LayoutStyle` 输入
- [x] 最小裁剪模型：`LayoutElement` 和 `DrawCommand` 新增 `ClipBounds` 字段；`LayoutTreeBuilder` 为 ScrollContainer 子元素计算 clip bounds（容器可见区域）；`DrawCommandRecorder` 将 clip 传递到 DrawCommand；`HitTestTarget` 携带 clip bounds
- [x] D3D12 clip 只读诊断：`D3D12DrawingBackend` 记录 `ClippedCommandCount`（非默认 clip 的命令数）；`--diagnose` 输出 clipped commands 计数；GPU scissor 未实现，clip 数据仅用于诊断
- [x] 命中测试 clip 对齐：`DrawingBackendCompositor.TryGetActionIdAt` 检查 `HitTestTarget.ClipBounds`，clipped 区域外的点击被拒绝；3 个测试覆盖 clip bounds 传递、DrawCommand 携带 clip、HitTestTarget 结构
- [x] 真实 clip hit-test 测试：通过 `RenderPipeline` → `DrawingBackendCompositor` 构造带 clip 的 button，`TryGetActionIdAt` 验证 clip 内命中、clip 外不命中（bounds check + clip check）
- [x] Clip intersection v0：`LayoutTreeBuilder` 为 ScrollContainer 子元素计算 clip 时与 parent clip 求交集（`IntersectRect`）；嵌套 ScrollContainer clip 不越界；2 个测试覆盖
- [x] Layout-driven diagnose：`--diagnose` 新增一帧从 VirtualNode → Layout → Pipeline → Compositor 生成的 clipped commands（`Layout clipped commands: 3`）；clip 链路端到端验证
- [x] ADR-018 D3D12 scissor 设计草案：三条候选路线（per-command scissor / batch by clip / D2D text clip）；v1 仅传递 clip 数据用于诊断和 hit-test，GPU scissor 留待 profiler 触发
- [x] Scroll offset v0：`ScrollContainer` 支持 `ScrollY` 属性；布局时子元素 y 坐标应用 `-ScrollY` offset，clip 保持容器可见区不变；cursorY 在容器布局后恢复到容器底部
- [x] Clip hit-test 精度测试：构造 viewport 120×60 使 button bounds 超出 clip 区域；验证“bounds 内 clip 外”点击被 clip check 拒绝（而非 bounds check）
- [x] Nested clip intersection 精确断言：断言交集结果等于预期矩形 `[16, 16, 268, 184]`，而非仅 `<= viewport`
- [x] Hit-test + scroll 联动测试：`ScrollY=30` 使第一个 button 部分滚出 clip；验证 clip 外不命中、clip 内命中；第二个 button 滚入可见区后可命中
- [x] ScrollY v0 清理：删除 `savedCursorY`；`ScrollY` 负值 clamp 到 0；`Math.Clamp(scrollY, 0, maxScrollY)` 防止超大滚动
- [x] 容器高度 v0：`ScrollContainer` 支持 `Height` 属性；无属性时 root 使用 viewport 高度，nested 使用剩余 viewport 或三行文本高度兜底；root clip 与 padded content 起点已分离，容器布局后 cursorY 恢复到 `contentTop + containerVisibleHeight + spacing`
- [x] Scroll bounds clamp：根据 content height 和 visible height 计算 `MaxScrollY`，布局时 clamp `ScrollY` 到 `[0, MaxScrollY]`；`OffsetElementY` 在 clamp 后偏移子元素 y 坐标
- [x] Scroll 诊断：`LayoutTreeResult.ScrollDiagnostics` 暴露每个 ScrollContainer 的 `VisibleHeight`、`ContentHeight`、`ScrollY`、`MaxScrollY`；`RenderPipeline.LastLayoutResult` 可访问；`--diagnose` 输出 scroll container 状态
- [x] 抽取 `LayoutContext` 结构体：替代 `availableWidth`/`viewportHeight`/`clipBounds`/`depth`/`style`/`scrollDiags` 参数列表；`Depth` 字段区分 root（0）和 nested（1+）；`ResolveImplicitVisibleHeight` 根据 depth 决定隐式可见高度语义
- [x] Nested container 高度语义：root 默认剩余 viewport，nested 无 `Height` 属性时用剩余 viewport 或 `Style.TextHeight * 3` 兜底；显式 `Height` 会被可用隐式高度约束，避免容器越过当前 viewport 策略
- [x] Scroll clamp 测试：覆盖 `ScrollY < 0`（clamp 到 0）、`ScrollY > MaxScrollY`（clamp 到 max）、显式 `Height` 小于内容高度；4 个新测试验证 diagnostics 中 scrollY 被正确 clamp
- [x] 可见元素诊断：`ScrollContainerDiag` 新增 `VisibleElementCount`/`ClippedElementCount`；XML doc 明确语义：VisibleElementCount = 与可见区域相交的元素数，ClippedElementCount = 完全在可见区域外的元素数；`--diagnose` 输出 `elements=2/2 visible`
- [x] 鼠标滚轮事件接入 v0：`CounterInputRouter` 将 `PointerWheel` delta 映射为 `WheelRaw(rawDelta)`，不做整数截断；`HandleInput` 将 raw delta 按 `SystemScrollSettings` + `ScrollMetrics` 换算为像素并累加到 `ScrollFramePump` pending accumulator
- [x] Scroll action 建模：`CounterMessage.ScrollFrame(delta, dt)` 走 MVU update；先将本帧 coalesced delta 应用到 `TargetPosition`，再用真实 `dt` 推进 `Position`；`BuildView` 将 `ScrollY` 作为 `ScrollContainer` 属性传入布局
- [x] Scroll wheel 单测：wheel delta 120/-120/240/30 映射为 raw wheel message；Windows raw `+120` 表示向上滚动（减少 `ScrollY`），raw `-120` 表示向下滚动（增加 `ScrollY`）；ScrollY 出现在 view 属性中并产生正确 scroll diagnostics
- [x] ScrollController 纯函数实现：`ApplyScrollDelta` 累计 raw/pixel/line/page delta（subpixel accumulator），换算 whole pixel 到 `TargetPosition`；`Tick(dt)` 指数 ease `Position` → `TargetPosition`；`SnapThreshold` 自动停止动画；`GetScrollY` 返回整数布局偏移
- [x] Raw wheel delta 保真：`CounterInputRouter` 发送 `WheelRaw(rawDelta)` 不做整数截断；高精度触摸板小 delta 保留 Windows 方向语义：`+30×4` 表示向上滚动 `-54px`，`-30×4` 表示向下滚动 `+54px`
- [x] Smooth scroll 动画：`ScrollState` 持有 `Accumulator`/`TargetPosition`/`Position`/`IsAnimating`/`HasMaxScrollY`；`ScrollFramePump` 使用 `Stopwatch` 记录两次 `ScrollFrame` dispatch 之间的真实时间（包含 render completion wait），不再以固定 16ms/60fps 作为动画节奏假设；动画结束后连续 idle 自然退出
- [x] ScrollDelta 结构化：`CounterInputRouter` 发送 `ScrollDelta(ScrollDeltaUnit.WheelRaw, rawDelta)`，不做整数截断；`ScrollDeltaUnit` 枚举支持 `Line`/`Pixel`/`Page`/`WheelRaw`
- [x] ScrollMetrics：`LineExtent`/`PageExtent`/`ViewportExtent`/`ContentExtent`；controller 不再硬编码 40px，通过 `ConvertToPixels(delta, metrics, settings)` 换算
- [x] SystemScrollSettings：PoC 默认 `LinesPerWheelNotch=3`、`WheelUnitsPerNotch=120`；Windows 平台后续可读取 `SPI_GETWHEELSCROLLLINES`
- [x] ScrollState 改为 double：`TargetPosition`/`Position`/`Accumulator` 全部 double 精度；小 delta 不因 int target 丢失
- [x] ApplyScrollDelta：根据 `ScrollDeltaUnit` + `ScrollMetrics` + `SystemScrollSettings` 换算到 pixel target；WheelRaw 保留 Windows raw delta 方向：`+120` → `-54px`（向上），`-120` → `+54px`（向下）
- [x] Scroll 精度测试：Windows raw `+120`/`-120` 方向、`+30×4`/`-30×4` 等价一刻度、小 delta 累计、Line/Pixel/Page 换算、backward-compatible ApplyWheel
- [x] ScrollFrame pump 单实例 guard：`ScrollFramePump.EnsureRunning` 保证同时只有一个 pump 在跑；首轮不做 pending 探测，直接 drain pending 并 dispatch `ScrollFrame(delta, dt: 0)`，单刻度 target=54px 不再被吞
- [x] Scroll dispatch frame-paced：每轮流程为 drain pending → 用 `Stopwatch` 计算距离上一条 `ScrollFrame` 的真实 `dt` → dispatch 一个 `ScrollFrame` → `Runtime.DispatchAndWaitAsync` 等待本次 diff patch 对应的真实 render completion；pump 不再额外调用 `RequestRenderAndWaitAsync`，每个 `ScrollFrame` 只触发一次实际 render；`dt` 明确包含上一帧 render wait，避免超小 dt 导致动画拖几分钟
- [x] Scroll 集成测试：向下单刻度（Windows raw `-120`）target=54px、双刻度 target=108px、正负抵消 target=0、单刻度动画收敛 scrollY=54、双刻度动画收敛 scrollY=108、debug 显示包含 target/pos/acc/applied
- [x] Debug 显示：PoC 文本临时显示 `ScrollY: applied=54 target=54.0 pos=53.87 max=unknown/0(known-zero) acc=0.000 anim=True pendingPx=0 drained=54 frames=1 waitMs=4.2 dt=0.016 frameQueued=False tickLoop=False`，可直接看出 input 是否更新 target、动画是否推进 position、max 是未知还是已知不可滚动、pending 是否未 drain、ScrollFrame 是否排队、是否双 render、render wait 是否过长
- [x] Wheel coalescing：`HandleInput` 不直接 dispatch scroll delta；raw delta 累加到 `ScrollFramePump.PendingPixels`（Interlocked CAS），只启动/唤醒 pump。快速滚轮 100 个事件不会产生 100 条 `ScrollFrame`
- [x] Per-frame drain：animation loop 每帧读取并清空 pending delta，合成单个 `ScrollFrame(delta, dt)` 消息。上一帧未处理/未渲染完成时，新 wheel 只累加 pending，不追加旧帧队列
- [x] Clamp target 到 layout max：`ScrollState.MaxScrollY` 由 layout pipeline 通过 `RenderPipeline.LastMaxScrollY` → `WindowDrawCommandTranslator.LastMaxScrollY` → `postFrameCallback` → `UpdateMaxScrollY` 反馈回 model。`ApplyScrollDelta` 在 `HasMaxScrollY=true` 时将 `TargetPosition` clamp 到 `[0, MaxScrollY]`；`WithMaxScrollY` 在更新时重新 clamp 并设置 `HasMaxScrollY=true`
- [x] MaxScrollY 集成测试：scroll way past content 后 target clamp 到 MaxScrollY；MaxScrollY 更新后 target 被重新 clamp
- [x] ScrollFrame 统一消息：`ScrollFrame(delta, dt)` 合并 scroll delta + animation tick，取代独立的 `Scroll`、`ScrollDeltaMsg` 和 `ScrollTick` 消息
- [x] Scroll backpressure：`ScrollFramePump` 持有 `IsFrameQueued` 标志；上一条 `ScrollFrame` 未处理并完成 render wait 前，新 wheel 只进入 pending accumulator，不向 Runtime 追加更多 scroll messages
- [x] Runtime scroll dispatch wait：`Runtime.DispatchAndWaitAsync` 供 PoC pump 使用，通过普通 diff patch 的 `PublishAndWaitRenderAsync` 等待本次状态更新对应的真实 render completion；scroll frame 不再混用 `RequestRenderAndWaitAsync`
- [x] `HasMaxScrollY` 状态标志：`ScrollState` 新增 `HasMaxScrollY`，区分"布局尚未报告 max"（`false`，target 不 clamp）和"已报告"（`true`，clamp 到 `[0, MaxScrollY]`）；`WithMaxScrollY(0)` 正确锁定 target 到 0
- [x] Scroll 诊断字段扩展：debug 显示新增 `max=unknown/0(known-zero)`、`pendingPx`、`frameQueued`、`tickLoop`，可现场区分 pending 未 drain、frame 排队、max clamp 问题
- [x] Counter 默认 scroll 内容：Counter PoC 默认内容高度超过 960×540 默认窗口的可见高度，初始 layout 反馈 `MaxScrollY >= 54`，避免首个 wheel delta 被 known-zero max 直接 clamp 成无反应
- [x] Scroll 回归测试：新增覆盖首笔 pending delta 不丢、Windows 向下单刻度 target=54、默认窗口下 Counter view 可滚动、layout max 回传后单刻度仍得到 target=54、快速 100 次 wheel 在 render wait 阻塞期间合并为少量 ScrollFrame、低频 render completion 不堆积旧帧、普通 diff patch render wait、`RequestRenderAndWaitAsync` 保留给无 diff repaint、第二帧 `dt` 包含 render wait、raw wheel 方向、`--diagnose-scroll` synthetic pump 输出；全量 279 个测试通过
- [x] `RetainedCommandBuffer`：全量 batch + dirty replacement ranges，内存层验证局部替换（v0，不接 D3D12）
- [x] 明确 retained command 资源生命周期：`RetainedCommandBuffer` 为帧作用域，`TextSlice` 仅在 `FrameDrawingResources` 存活期间有效；partial apply 仅限同帧资源作用域内
- [x] `RetainedRenderFrame`：组合 retained command buffer、resource resolver、dirty command ranges、hit targets；提供 `ApplyFull`、`ApplyPartial`、`Invalidate`、`ToBatch`
- [x] `RenderPipeline` 内部接入 `RetainedRenderFrame` v0：pipeline 输出完整 `RenderFrameBatch`，内部维护 retained frame，用 dirty ranges 更新
- [x] `RetainedTree.Apply` 语义固化：Update 保留 children、Add/Remove DFS index 语义、dirty index 含义已补注释和文档

---

## 7a. 诊断基线（2026-05-10）

以下为 `Irix.Poc --diagnose`、`Irix.Poc --diagnose-resize`、`Irix.Poc --diagnose-scroll` 与 `Irix.Poc --diagnose-input` 的输出基线，供后续对比。

### Viewport / resize / display scale

平台中性 display scale 模型已完成（2026-05-13）。Compositor 是 scale boundary：接收 physical viewport + `DisplayScale`，转换为 logical viewport 供 layout 使用，再将 draw commands 缩放回 physical 坐标给 backend。Layout、hit-test 在 logical units 工作；D3D12 backend 接收 physical pixels。

`DisplayScale`（`Irix.Drawing`）是平台中性类型，只有 `ScaleX`/`ScaleY` 两个 float，不包含 Windows DPI 概念。`IScreenInfo.Scale` 从 `GetDpiForMonitor` 计算得出，在平台层转为 `DisplayScale`，不传出 Windows DPI 原值。`FrameContext.Scale` 替代原来的 `DpiScale`（dead code），携带 scale 信息给 backend。

```text
Platform → [PhysicalViewport + DisplayScale] → Compositor
Compositor → [LogicalViewport] → LayoutPipeline → [DrawCommands in logical units]
Compositor → [DrawCommands × Scale] → Backend → [Physical pixels]
```

Pipeline:
```text
WM_SIZE / GetClientRect physical client size
  -> WindowsNativeWindow.Region.PhysicalBounds = latest window physical client size
  -> INativeWindow.SizeChanged(width, height)
  -> D3D12Renderer.Resize(width, height) records pending resize
  -> CompositorLoop.RequestRenderAsync()
  -> WindowDrawCommandTranslator.Translate()
       -> prepareFrame: D3D12Renderer.ApplyPendingResize()
       -> viewportProvider: PixelRectangle(physical bounds)
       -> physical→logical viewport conversion (÷ DisplayScale)
       -> RenderPipeline.Build(root, logicalViewport)
       -> draw commands scaled back to physical (× DisplayScale)
  -> DrawingBackendCompositor.RenderAsync()
       -> backend receives physical coordinates via FrameContext
```

Resize 后 renderer 已应用的 swapchain size 是 physical viewport 的 source of truth；`window.Region.PhysicalBounds` 提供最新物理窗口尺寸与窗口位置，diagnostic 会同时输出 window physical size、renderer swapchain size、translator viewport size、layout viewport size、scale、logicalViewport、最后一次 applied pending resize、render count 与 layout rebuild count。`viewportMatchesRenderer=True` 表示 translator viewport size 等于 renderer size；`layoutUsesRendererSize=True` 表示 layout viewport size 等于 renderer size。重复相同 size 的 render request 不应增加 `layoutRebuildCount`，不同 size 才应使 retained layout 因 viewport invalidation 重建。

### Root ScrollContainer clip semantics v0

曾观察到 `--enable-scissor` 下 root `ScrollContainer` 的 clip 从 `VerticalPadding` 开始，内容会在距离窗口顶部一个 padding 的位置被裁掉。这个差异属于 layout semantics 问题：D3D12 scissor、D2D text clip 与 `DrawingScissor` 都只是执行 layout 传入的 `ClipBounds`，不是 backend scissor bug。

root clip semantics v0 已阶段完成：`Depth == 0` 的 root `ScrollContainer` 使用 viewport 边界作为 clip rect，children 仍从 `HorizontalPadding` / `VerticalPadding` 后开始布局。`LayoutTreeBuilder` 区分 `containerClipTop` 与 `contentTop`：clip 决定裁剪边界，`contentTop` 决定子元素起始位置。nested `ScrollContainer` 暂时保持原有 padding clip 语义，后续如果要扩展 nested semantics 另开任务。

### 2026-05-10 阶段验收记录

- 手测通过：默认模式、`--enable-scissor`、`--debug-ui --enable-scissor` 下滚动与 button hit smoke 均通过；resize 后继续滚动也通过，未观察到背景/文本裁剪不同步、命中偏移或 device removed。
- `--enable-scissor` 继续保持显式开关，不默认启用；当前阶段继续 soak，不扩展 nested clip stack、GPU partial redraw、text batching 或通用控件抽象。
- 阶段回归基线保持为：`dotnet test`、`--diagnose`、`--diagnose-resize`、`--diagnose-scroll`、`--diagnose-input`。

### `--diagnose` 标准模式

| 指标 | 值 |
|------|-----|
| Format cache | 1 entry, 1 hit, 1 miss, 0 evictions |
| Layout cache | 2 entries, 4 hits, 2 misses, 0 evictions |
| Format hit rate | 50.0% |
| Layout hit rate | 66.7% |
| Device removed | False |
| Device error reason | (none) |
| Swapchain size | 960×540 |
| **Compositor renders** | **3** |
| **Partial apply** | **2** |
| **Full apply** | **1** |
| **Empty frames** | **0** |
| **Partial hit rate** | **66.7%** |
| **Last dirty ranges** | **1 range, [0..3] (4 commands)** |
| **Backend dirty ranges** | **1 range, [0..3] (4 commands)** |
| **Dirty ranges aligned** | **True** |
| **Clipped commands** | **0** (diagnostic mode uses raw commands, not layout pipeline) |
| **Layout commands** | **3** |
| **Layout clipped commands** | **3** |
| **Layout rebuild count** | **1** |
| **Layout rebuild reason** | **TreeStructure** |
| **Layout dirty classifications** | **(none)** |
| **Layout hit targets** | **1** (LayoutBtn, clip = 0,0,960,540) |
| **ScrollContainer[0]** | **visible=540 content=96 scrollY=0 maxScrollY=0 elements=2/2 visible** |

### `--diagnose-resize` 压力模式

| 指标 | 值 |
|------|-----|
| Device removed | False |
| Device error reason | (none) |
| Swapchain size | 929×454 |
| Window physical size | 929×454 |
| Renderer swapchain size | 929×454 |
| Translator viewport size | 929×454 |
| Layout viewport size | 929×454 |
| Last applied pending resize | 929×454 |
| Render count | 80 |
| Layout rebuild count | 80 |
| Layout rebuild reason | ViewportChanged |
| viewportMatchesRenderer | True |
| layoutUsesRendererSize | True |
| scaleMode | PhysicalPixelsV0 |
| screenScale | 1 |
| dpiAwareness | ProcessDefault |
| scale | 1x1 |
| logicalViewport | (matches physical at 100% DPI) |
| coordinateSpace | PhysicalPixels, logicalCoordinates=False |
| 退出码 | 0 |

### `--diagnose-scroll` 滚动 pump smoke

`Irix.Poc` 当前为 `WinExe`，部分终端无法捕获 stdout；`--diagnose-scroll` 同时写入 `TestResults/diagnose-scroll.txt`，用于无 UI 的滚动管线检查。

| 指标 | 预期/基线 |
|------|-----------|
| frames | 2（首帧 drain 输入，第二帧验证 animation dt） |
| waitMs | > 0，来自 synthetic render wait |
| dt | > 0，包含上一帧 render wait |
| drained | 54.0 |
| pending | 0.0 |

### `--diagnose-input` ownership smoke

`--diagnose-input` 同时写入 `TestResults/diagnose-input.txt`，用于无 UI 验证 hover / focus / capture 事件归属。

| 阶段 | 预期/基线 |
|------|-----------|
| afterMove | `hover=Increment focus=- pressed=- capture=- hoverChanges=1 pointerPressed=False` |
| buttonState afterMove | `Increment hovered=True pressed=False focused=False priority=Hovered color=#FF4888FF` |
| afterPress | `hover=Increment focus=Increment pressed=Increment capture=Increment` |
| buttonState afterPress | `Increment hovered=True pressed=True focused=True priority=Pressed color=#FF245CD2` |
| duringCaptureMove | `hover=Decrement focus=Increment pressed=Increment capture=Increment` |
| buttonState duringCaptureMove | `Increment hovered=False pressed=True focused=True priority=Pressed color=#FF245CD2` |
| releaseOutside | `mapped=True message=Increment ... pressed=- capture=-` |
| buttonState releaseOutside | `Increment hovered=False pressed=False focused=True priority=Focused color=#FF54A0FF` |
| keyboardEnter / keyboardSpace | focused target 为 `Increment` 时映射为 `Increment` action |
| pressEmpty | `mapped=False ... focus=- pressed=- capture=- pointerPressed=True` |
| releaseAfterEmptyPress | `mapped=False ... pointerPressed=False` |
| focusLost | `hover=- focus=- pressed=- capture=-` |
| buttonState focusLost | `Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6` |
| events | 输出 `HoverChanged` / `FocusChanged` / `PressedChanged` 诊断事件流 |
| dirtyReason hoverOnly | `reason=StyleOnly classifications=4:StyleOnly` |
| dirtyReason press | `reason=StyleOnly classifications=4:StyleOnly` |
| dirtyReason release | `reason=TextSizeAffecting classifications=1:TextSizeAffecting,4:StyleOnly` |

Layout dirty v1 诊断到此闭环并冻结：reason tracking、`--debug-ui` 可视化、`--diagnose-input` CLI dirty reason、测试与文档均已覆盖；后续不继续扩展诊断面，除非修复现有输出的明确回归。`StyleOnly` patch v0 仅保留设计评估：未来复用 retained layout 时 bounds / clip / element ranges / hit target geometry / scroll diagnostics 必须保持不变；只允许按 dirty element range 重录 draw commands；hit target 可复用 bounds/clip 但必须更新 `ActionId` 等 metadata；新命令必须绑定当前 frame resources，不能引用旧 frame text/resource。plan diagnostics 已完成：`--diagnose` 已输出 hover-only eligible 与 layout-affecting fallback smoke，formatter 不再继续扩展。`StyleOnlyPatchPlanBuilder` 目前仍是 post-layout validation，依赖已经构建出的 next layout elements 与 dirty ranges，只输出 eligible/fallback 诊断，不代表已跳过 layout。未来真正 fast-path 的接入点设计为 dirty classify 后、layout rebuild 前；真实输入必须从 retained layout + next `VirtualNode` 派生 visual command metadata，不能依赖 next layout output。当前不跳过 `StyleOnly` layout，不做局部 layout，不改 `RenderPipeline.Build` 或 `LayoutTreeBuilder`。

---

## 7b. Scroll 手动验证清单（2026-05-10）

以下记录来自 2026-05-10 本机交互回归复测；自动化 smoke 不能替代真实 hover / wheel 手感检查。

| 场景 | 结果 | 记录 |
|------|------|------|
| 前台窗口 hover + 单刻度 wheel | 通过 | Windows raw `-120` 向下滚动时 debug `target` 立即增加约 54px，`applied` 平滑追上；不再出现 1-3 刻度无反应 |
| 后台/非激活窗口 hover + wheel | 通过 | hover wheel 可进入同一 scroll pump 路径；停止输入后不出现长时间追赶 |
| 快速连续 wheel | 通过 | 输入期间 pending 合并，停止后快速收敛；未观察到长 backlog |

以下项目保留为后续设备矩阵复测，不作为本轮继续扩展 scroll 功能的入口。

| 场景 | 需要确认 |
|------|---------|
| 前台窗口 hover + 鼠标滚轮 | 向下滚一刻度时 debug `target` 约增加 54px；向上滚一刻度时 `target` 约减少 54px或在顶部保持 0 |
| 后台/非激活窗口 hover + 鼠标滚轮 | 不产生长时间追赶；焦点/hover 行为符合当前平台输入策略 |
| 240Hz / 180Hz / 120Hz / 60Hz 显示器 | 动画速度一致，不依赖固定 16ms/60fps 假设 |
| Windows 11 DRR 开 / 关 | 停止滚轮后短时间内停止，不继续排队十几秒 |
| 快速连续 100 次 wheel | debug `frameQueued` 期间只累加 pending；停止输入后快速收敛 |

### Scroll scope freeze

本轮到此暂停 scroll 扩展：暂不做惯性、滚动条、系统滚动行数读取、通用 `ScrollController` 抽象或更多平台策略读取；后续只接受明确回归修复或诊断补强。

### 下一步 UI 基础方向

已选择 **focus / hover / input capture 语义 v0** 作为下一步小方向；`clip` GPU scissor v0 暂缓，避免同时扰动渲染后端和输入语义。

### Interaction foundation freeze

当前 scroll pump、input ownership、Button visual state 三条交互基础链路已冻结并标记阶段完成：不继续修改 scroll coalescing / frame pump、ownership 事件规则、button state 派生与颜色优先级，除非有明确回归。短期只允许围绕后续新方向做独立增量，不再小改这些基础链路。

### Counter debug UI gating

Counter PoC 默认 UI 隐藏 `ScrollY:` 与 `Input:` 调试文本，只保留计数、操作提示、控件与可滚动内容；`--debug-ui` 通过 `CounterApplication(showDiagnostics: true)` 恢复完整 UI 内诊断文本。CLI 诊断模式保持独立：`--diagnose-scroll` 与 `--diagnose-input` 继续输出完整报告，不依赖 UI 文本是否显示。debug UI gating 已标记阶段完成，后续不再小改默认/调试 UI 分流，除非出现明确回归。

### Input ownership v0

当前 ownership 事件规则已冻结：不继续添加新的 ownership event 类型，不扩多 pointer、嵌套 focus scope、平台 capture API；后续只做明确回归修复。`CounterModel` 持有 `OwnershipSnapshot InputOwnership`，Button visual state v0 只从 model snapshot 派生布尔属性，不反向扩展 ownership 模型。ownership-only 输入变化（hover / press / focus / capture）通过 `CounterMessage.InputVisualStateChanged(snapshot)` 写入 model；action 输入通过 `CounterMessage.RoutedInput(action, snapshot)` 在同一次 MVU 更新中同时应用 action 与最新 snapshot，确保 release outside 后 pressed/capture 清空能进入同一帧。`Program.DiagInputOwnership` 保留为诊断读取，不再作为 Button visual state 的数据源。

| 状态 | v0 规则 |
|------|---------|
| HoveredTarget | `PointerMoved` 通过 hit-test 更新；进入/离开只更新诊断字段与 `HoverChangeCount` |
| PressedTarget | 左键 `PointerPressed` 命中 target 后记录 pressed target |
| CapturedTarget | 左键 `PointerPressed` 命中 target 后 capture 到同一 target；release 前移到外部也仍归属 captured target |
| FocusedTarget | 左键 `PointerPressed` 命中 target 后设置 focus；点击空白清除 focus；`FocusLost` 清空 hover/focus/pressed/capture |
| Keyboard target | Enter/Space 优先激活 focused target；Up/Down/R 保留 Counter PoC 全局快捷键语义 |
| Wheel | 仍走 scroll coalescing 路径，不进入 target capture 语义 |
| Stateless overload | `CounterInputRouter.TryMapInput(inputEvent, hitTest, out message)` 仅用于旧单事件测试和简单 release hit-test；不能表达 hover/focus/capture，真实 PoC 输入应使用带 `InputOwnershipState` 的 overload |
| Diagnostic events | ownership 变化追加 `HoverChanged`、`FocusChanged`、`PressedChanged` 到诊断事件日志；事件类型冻结，不再为视觉状态新增 ownership event |

已覆盖测试：hover enter/leave、capture 期间移动到另一个 target 不改变 captured target、pressed capture 后 release outside 仍触发原 button、release 后 capture 清空但 focus 保留、press 空白清除 focus 且不触发 action、focused target 的 Enter/Space 激活、focus 后 Up/Down/R 仍可用、FocusLost 清理 ownership、ownership event log、model-owned ownership visual refresh、hover-only/press/release outside/press empty/focus lost 后 model 与 button state 同步、release outside 同帧 focused FillRect 颜色、Button visual state 派生、`--diagnose-input` normal / hovered / pressed / focused button state 与 priority 输出、hover-only / press / release dirty reason 输出。

### Button visual state v0

Button 已基于 `CounterModel.InputOwnership` 派生 `IsHovered` / `IsPressed` / `IsFocused`，由 `CounterApplication.BuildView` 写入 `VirtualNodeAttribute`，`LayoutTreeBuilder` 携带到 `LayoutElement.ButtonState`，`DrawCommandRecorder` 通过现有 `DrawCommand.Color` 选择 button fill color。v0 不直接修改 `DrawCommand`、`IDrawingBackend` 或 D3D12 backend；颜色优先级固定为 `Pressed > Hovered > Focused > Normal`。

### Button visual state scope freeze

本轮 Button visual state 只冻结颜色状态链路与诊断输出；暂不做 focus ring / outline、hover cursor、animation transition、theme system、通用控件抽象，也不扩展新的 ownership event 类型或 D3D12 后端接口。后续只接受明确回归修复或诊断补强。

### Style preset v0

Style 分层已收敛并标记阶段完成：`LayoutStyle` 只描述布局几何参数（padding、spacing、text/button/rectangle height、button width 估算）；`DrawingStyle` 只描述绘制 token（文本色、矩形色、button 各状态 fill color、文本样式）；`ControlVisualStateResolver` 只负责把 `ButtonVisualState` 按 `Pressed > Hovered > Focused > Normal` 映射到 `DrawingStyle` 的颜色 token。`RenderStylePreset.Default` 是 rendering 默认 preset，`CounterStylePreset.Default` 复用默认 layout/button token，仅覆盖 Counter PoC 的正文文本色。当前不引入 theme system、样式继承或通用控件抽象；后续不再小改 style preset 主链，除非有明确回归。

已覆盖测试：默认 preset 的 button height、padding、text height、button width 参数不变；默认 preset 的 normal / focused / hovered / pressed button fill color 不变；Counter PoC preset 保持默认 layout/button token 且只覆盖正文文本色；`--diagnose` style preset 输出包含 preset 名称、layout metrics、button state color priority 与四态颜色。

### Clip/scissor v0

已接受 [ADR-Scissor-Clipping-v0.md](ADR-Scissor-Clipping-v0.md)：clip/scissor v0 已功能闭环完成，但默认启用待定。D3D12 backend 默认仍是 `ClipMode=Diagnostic`，正常 PoC 不启用 GPU per-command scissor；手测可用 `--enable-scissor` 显式打开 FillRect scissor 与 D2D text clip，启动输出和 `--debug-ui` 都显示当前 clip mode。人工验收已通过：默认 UI、`--debug-ui`、滚动、按钮 hover/press/focus、后台 hover wheel 均无交互回归；顶部裁剪场景下 Increment button background/text 已验证同步裁剪，未出现文字越过 clipped 背景。`DrawCommand.ClipBounds` 已经从 layout 传到 backend；`DrawingScissor.ResolveEffectiveScissor` 已抽为纯函数，覆盖 default clip、viewport 内 clip、超出 viewport 交集、空交集；FillRect 与 DrawTextRun 都使用这套 effective clip 语义。`DrawingScissor.ToIntegerScissorRect` 明确 floor origin / ceil extent / clamp viewport 策略，避免 float clip 转整数时缩小有效区域。`ClipMode=Scissor` 目前对 FillRect 使用 D3D12 rasterizer scissor：default clip 使用 full viewport，空交集 skip，按连续相同 scissor run 设置 `RSSetScissorRects`；run-length 计数已覆盖连续相同 scissor、不同 scissor 切换、相同 scissor 非连续，以及 Diagnostic/Scissor mode 差异。D2D text clip v0 已接入：`D3D12DrawingBackend` 将 DrawTextRun effective clip 传入 `D3D12TextRenderer.TextData`，空 text clip skip 并计入 `TextClipSkippedCount`，partial clip 时 `D3D12TextRenderer` 在每条 `DrawTextLayout` 前后 push/pop axis-aligned Direct2D clip，default/full viewport clip 则走原文本路径不额外 push clip，同时保留 `D2D1_DRAW_TEXT_OPTIONS_CLIP` 处理文本自身 layout rect。`--diagnose` 包含五类 scissor/text clip smoke：direct FillRect（`effectiveClip=(32,32,80,40)`、`textClip=False`、`gpuScissor=True`、`clippedCommands=1`、`emptyIntersectionSkipped=0`、`scissorStateChanges=1`、`deviceRemoved=False`）、pipeline FillRect（`source=ScrollContainerRectangle`、`textClip=False`、`clippedCommands=1`、`emptyIntersectionSkipped=0`、`scissorStateChanges=1`、`deviceRemoved=False`、`passed=True`）、pipeline text（`source=ScrollContainerButton`、`textClip=True`、`layoutClip=True`、`effectiveClip=(0,0,960,20)`、`clippedCommands=2`、`textClipSkipped=0`、`deviceRemoved=False`、`passed=True`）、empty FillRect（`kind=FillRect`、`clippedCommands=1`、`emptyIntersectionSkipped=1`、`scissorStateChanges=0`、`deviceRemoved=False`）、text clip（`kind=DrawTextRun`、`textClip=True`、`layoutClip=True`、`effectiveClip=(32,32,80,40)`、`textClipSkipped=1`、`deviceRemoved=False`）。即使 text clip v0 完成且手测通过，`--enable-scissor` 仍暂不默认启用，继续保留显式开关一轮。本轮暂停 clip 扩展：不做 nested clip stack、GPU partial redraw、文本 batching、主题系统或通用控件抽象；后续只接受明确回归修复或诊断补强。

---

## 7c. 已知问题清单（2026-05-08）

以下为本轮诊断/验证中发现的已知问题，记录但不在本轮扩功能修复。

| 编号 | 类别 | 描述 | 严重程度 | 状态 |
|------|------|------|---------|------|
| I-01 | Debug layer | D3D12 debug layer 在 debugger attached 时启用，release 构建无 debug layer 输出 | 信息 | 预期行为 |
| I-02 | Resize 抖动 | 快速拖动时 DXGI `DXGI_SCALING_NONE` 下窗口边缘可能出现短暂黑边（backbuffer 未覆盖区域） | 低 | 可接受，后续可通过 `ResizeBuffers` 立即覆盖边缘消除 |
| I-03 | 启动光标 | 窗口 Show 后到 D3D12 首帧 present 之间仍有短暂 app-starting 光标残留 | 低 | 根因是 D3D12/DirectWrite 初始化延迟，后续可通过预编译 shader 或异步初始化改善 |
| I-04 | 文本 cache | Format hit rate 50%（首次 miss，后续 hit），layout hit rate 66.7% — 诊断模式仅 3 帧，不代表稳态 | 信息 | 正常暖启动行为 |
| I-05 | Device-lost | 设备丢失后仅 fail-fast + reason 字符串，不自动恢复 | 中 | 待实现 device-lost recovery（重建设备、交换链、所有 GPU 资源） |

---

## 8. 当前不建议做的事

> 完整的非目标清单与边界声明见 [设计文档附录 C](/d:/source/Irix/docs/Irix_Framework_Design.md#附录-c非目标清单与边界声明)。以下为核心约束摘要：

**架构边界（绝对不做）：**
- 不要做运行时 `XAML / IXAML` parser
- 不要复制 `DependencyProperty`、`VisualStateManager`、`ResourceDictionary` 那套完整 runtime
- 不要引入第二套 `ViewModel` 权威状态再与 `Model` 同步
- 不要把上层代码直接绑到 `Skia` 对象模型

**范围收敛（v1 不做，后续再议）：**
- 不要先做 `Vulkan`、跨平台、完整多屏热插拔
- 不要先做复杂动画系统、完整 `Remote UI Delivery`
- 不要先做自研 Drawing Engine
- 不要在 `WindowVisualCompositor` 上继续叠更多正式架构职责

---

## 9. 对 AI 工具的特别提示

如果你是 AI 工具或辅助代理，请优先遵循以下约束：

- 先确认当前代码真实状态，再引用设计目标
- 不要把设计文档中的未来能力误认为已落地
- 将 `WindowVisualCompositor` 视为**PoC 过渡实现**，不是最终结构
- 如需新增渲染相关代码，优先放在 `Irix.Rendering`
- 如需新增平台代码，Windows interop 优先使用 `CsWin32`
- `Irix.Platform.Windows` 的 CsWin32 输入由包内 props 自动收集 `NativeMethods.txt/json`；不要再手动添加重复的 `AdditionalFiles Include="NativeMethods.txt"`
- 命名遵循 C# 风格，尽早统一，不保留临时缩写命名
- 不要在未经确认的情况下把 `Skia` API 泄漏到上层 UI / layout / core
- 遇到 `record struct` 风格配置类型时，不要假设 `new Type()` 等价于 `.Default`
- 将 `MVVM bridge` 视为编译期前端，不是第二套 runtime
- 不要默认引入反射式 binding engine、弱类型 `DataContext` 或运行时 `XAML` 加载

---

## 10. 当前工作区注意事项

当前仓库可能处于脏工作区状态。开始修改前请先检查是否存在用户尚未提交的更改，尤其注意以下文件可能已经被编辑：

- `.github/copilot-instructions.md`
- `docs/Irix_Framework_Design.md`
- `src/Irix.Core/VirtualNodeModels.cs`
- `src/Irix.Poc/WindowVisualCompositor.cs`

如果你的任务会改到这些文件，请先阅读现有内容，不要覆盖未知改动。

---

## 11. 建议的下一次提交主题

如果按当前节奏推进，最自然的一次提交应围绕下面这个主题展开：

`Seal V1 partial apply core behind default-off selected render-source gates`

对应改动范围建议：

- `Irix.Rendering`
- `tests/Irix.Core.Tests`
- `docs`
