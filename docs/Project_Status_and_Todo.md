# Irix 项目进度与待办

> 面向开发者、Copilot/Codex 等 AI 工具的当前状态说明。目标是帮助接手者快速判断“哪些已经落地、哪些只是设计、下一步应该从哪里开始”。>
> 📅 **最后验证日期：** 请在每次提交前更新此日期。本文档描述的代码状态以此日期为准。
>
> 📐 **架构设计详见：** [Irix_Framework_Design.md](/d:/source/Irix/docs/Irix_Framework_Design.md)。本文档不重复设计细节，仅记录实现状态与待办。
---

## 1. 项目定位

Irix 当前是一个**早期原型期**的原生 .NET UI 框架项目。

**核心方向概要**（详细论述见 [设计文档 §1~§3](/d:/source/Irix/docs/Irix_Framework_Design.md)）：

- v1 / Windows-only PoC 以 `D3D12` 为唯一图形后端（[ADR-001](/d:/source/Irix/docs/Irix_Framework_Design.md#附录-b架构决策记录索引-adr)）
- Drawing 层采用 `DrawCommand + IDrawingBackend` 隔离 Skia（[ADR-002](/d:/source/Irix/docs/Irix_Framework_Design.md#附录-b架构决策记录索引-adr)）
- UI 交付层分两条线：`Local UI Remoting`（免费/开源）与 `Remote UI Delivery`（商业版）
- `MVVM Bridge` 仅为编译期 authoring layer（[ADR-007](/d:/source/Irix/docs/Irix_Framework_Design.md#附录-b架构决策记录索引-adr)）

**Phase / 版本映射**（详见 [设计文档 §12](/d:/source/Irix/docs/Irix_Framework_Design.md#12-分阶段交付计划)）：

| Phase | 版本 | 当前状态 |
|-------|------|---------|
| Phase 1 | v1.0 基础 | 🚧 进行中 |
| Phase 2 | v1.0 MVP | ❌ 未开始 |
| Phase 3 | v1.0 GA | ❌ 未开始 |
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
  - Win32 互操作优先走 `CsWin32`
  - 已有 `D3D12Renderer`，使用 CsWin32 生成的裸指针 COM 包装（`allowMarshaling: false`），支持设备创建、交换链、清屏、呈现
  - 已有 `D3D12Renderer2D`，运行时 HLSL 编译 + 顶点缓冲区渲染彩色矩形
  - 已有 `D3D12DrawingBackend`（Irix.Poc），Phase 2：FillRect → D3D12 矩形渲染
- `Irix.Rendering`
  - 已有 `ICompositor`
  - 已有 `CompositorLoop`，负责异步消费 `PatchBatch`；已实现无变化帧跳过（`Count == 0` 时跳过翻译与渲染）
  - 已有 `ConsoleCompositor` 与 `CompositeCompositor`
  - 已有 `LayoutTreeBuilder`、`LayoutElement`、`DrawCommandRecorder` 过渡骨架
  - `RenderPipeline` 已引入 retained layout：缓存 `LayoutElement[]`，树/视口不变时复用
  - 已有 `RenderFrameBatch` / `HitTestTarget` / `TextRunEntry`，并行承载命中数据与文本内容
  - 已有 `DrawingBackendCompositor`，桥接 `ICompositor` → `IDrawingBackend`，缓存命中目标
- `Irix.Drawing`
  - 已拆出独立项目骨架
  - 已有 `DrawCommand`、`FrameContext`、`DrawCommandBatch`、`IDrawingBackend` 最小类型
  - `DrawCommand` 已移除内联 `string? Text`，改为 `ResourceHandle` + `TextRunEntry[]` 并行传递
- `Irix.Poc`
  - 已有 Counter 示例应用
  - 已有 `WindowVisualCompositor`，能消费当前 `RenderFrameBatch` 并更新 PoC Window 内容与命中目标
  - 已有 `WindowBackend`，可将 `DrawCommand + HitTestTarget[]` 翻译成 PoC Window 内容元素与命中目标
  - 已有 `PoCDrawingBackend`，首次实现 `IDrawingBackend` 接口，验证 DrawCommand → WindowContentElement 链路
  - 已有 `D3D12DrawingBackend`，D3D12 渲染路径已接入 PoC（Phase 1: 清屏渲染）
  - 已将 Counter 示例中的输入映射抽到独立 `CounterInputRouter`
  - 已打通：窗口创建 -> 输入 -> runtime dispatch -> patch 发布 -> PoC 可视化
- `Irix.Core.Tests`
  - 已有最基础 runtime 测试
  - 已有 layout / draw pipeline / `WindowBackend` 基础测试与最近的回归测试

### 尚未落地的关键内容

- D3D12 渲染已接入 PoC：`D3D12Renderer` 使用 CsWin32 生成的裸指针 COM 包装（不再手写 vtable），`D3D12DrawingBackend` 实现 Phase 1 清屏渲染
- 还没有 Skia + D3D12 集成
- 还没有真正的 retained tree / layout tree / draw command pipeline
- `VirtualNodeDiffer` 已实现深比较（递归节点等价判断），能正确检测无变化并跳过 ReplaceRoot；尚未实现局部 diff / keyed reconciliation
- `DrawCommand` 已移除内联 `string? Text`，改为 `ResourceHandle` + 并行 `TextRunEntry[]` 传递文本
- `PatchBatch` 已携带 `Root` 属性，消费者不再需要从 `Memory` 中反推根节点
- 测试覆盖已扩展至 47 个测试（含 diff、DrawCommand 文本传递、CompositorLoop 跳过、retained layout、DrawingBackendCompositor、所有权转移等）
- `CompositorLoop` 已实现 `PatchBatch.Count == 0` 时跳过翻译与渲染，避免无变化帧清空窗口
- `RenderPipeline` 已引入 retained layout：缓存上一帧的 `LayoutElement[]`，仅在树或视口变化时重新布局，否则复用缓存并重新录制 DrawCommand
- `IDrawingBackend` 已首次落地实现：`PoCDrawingBackend`（Irix.Poc）+ `DrawingBackendCompositor`（Irix.Rendering），验证了从 `RenderFrameBatch` → `IDrawingBackend` → `INativeWindow` 的完整链路

**数据流各阶段验证速查**（详见 [设计文档 §4.1](/d:/source/Irix/docs/Irix_Framework_Design.md#41-关键数据流本地模式v1)）：

| 阶段 | 状态 |
|------|------|
| 输入采集 → MPSC | ✅ 已验证 |
| 消息派发 → Update | ✅ 已验证 |
| View 构建 | ⚠️ 部分 |
| Diff / Patch | ⚠️ 深比较已实现，无变化跳过；尚无局部 diff |
| 布局 | ⚠️ Retained layout 已引入，未脱离硬编码常量 |
| 命令录制 | ⚠️ 基础可用，TextRunEntry 已分离 |
| 帧消费 (CompositorLoop) | ✅ 已验证（含无变化跳过） |
| GPU 渲染 | ✅ D3D12 清屏渲染已接入 PoC |
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

- `VirtualNodeDiffer` 已实现递归深比较，能正确检测无变化（输出空 PatchBatch）；输出仍为 ReplaceRoot，尚未实现局部 diff / keyed reconciliation
- 还没有 drawing 层抽象

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

当前渲染层实际上还是“PoC 可视化层 + 初步成形的 layout/draw pipeline 骨架”，不是正式 GPU 渲染层。

`WindowVisualCompositor` 当前主要负责 PoC backend 可视化：

1. 消费 `RenderFrameBatch`
2. 生成 PoC Window 内容元素
3. 维护命中目标，并明确与 `DrawCommand` 分离传递
4. 空帧到来时主动清空窗口元素与命中目标，避免上一帧命中信息残留

布局与命令录制已经开始沉到 `Irix.Rendering`，但离正式 retained tree / GPU backend 还有距离。

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
- 已补 `VirtualNodeDiffer` 深比较与空 PatchBatch 检测测试（15 个测试用例）
- 已补 `PatchBatch.Root` 属性验证
- 已补 `WindowDrawCommandTranslator` 默认布局回归测试
- 已补 `WindowBackend` 颜色映射断言
- 已补 `WindowVisualCompositor` 命中边界与空帧清理测试
- 已补 Counter PoC 输入路由映射测试
- 已补 `PatchBatch` / `DrawCommandBatch` / `RenderFrameBatch` / `CompositorLoop` 基础所有权与释放路径测试
- 已补 `CompositorLoop` 无变化帧跳过测试
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

### 已确认（详见 ADR-001 ~ ADR-012）

- D3D12 作为 v1 唯一图形后端 / Skia 仅作为 backend adapter（ADR-001, ADR-006）
- DrawCommand + IDrawingBackend 隔离层（ADR-002）
- IMemoryOwner 所有权转移模型（ADR-003）
- HitTestTarget 与 DrawCommand 并行传递（ADR-004）
- 单线程 Update 串行执行（ADR-005）
- MVVM Bridge 为编译期前端（ADR-007）
- Local UI Remoting 为免费/开源方向（ADR-008）
- 不做运行时 XAML/IXAML 解析（ADR-009）
- VirtualNode 采用轻量不可变结构（ADR-010）
- DrawCommand 不内联文本，通过 ResourceHandle + TextRunEntry[] 并行传递（ADR-011）
- PatchBatch 携带 Root 属性，消费者直接使用而非从 Memory 反推（ADR-012）

### 未确认或尚未落地

- `D3D12` 具体接入方式
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

- 在 `Irix.Drawing` 中继续稳定 `DrawCommand` / `IDrawingBackend` / `DrawCommandBatch`
- 继续把 `LayoutTreeBuilder` / `DrawCommandRecorder` 从 PoC 规则里抽成更通用的 pipeline
- 让 `WindowVisualCompositor` 保持为纯 PoC/backend 层，不再回流正式职责
- 梳理 `record struct` 风格配置对象的默认值策略，避免再次出现 `new XxxStyle()` 触发零值布局

### P1

- ✅ `VirtualNodeDiffer` 已从 `ReplaceRoot` 提升到深比较（递归节点等价判断）；下一步：局部 diff / keyed reconciliation
- 增加 `PatchBatch` / `IMemoryOwner<T>` 异常、取消、释放路径测试
- 增加输入路由和命中测试的最小测试覆盖

### P2

- ✅ D3D12 基础渲染循环已搭建（CsWin32 裸指针 COM 包装，不再手写 vtable）
- ✅ `D3D12DrawingBackend` 已实现 Phase 1 清屏渲染
- ✅ Phase 2: D3D12 矩形绘制已实现（`D3D12Renderer2D`：运行时 HLSL 编译 + 顶点缓冲区）
- Phase 3: D3D12 文本渲染（暂用 GDI 软件光栅化上传纹理）
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
- [x] 实现 `D3D12DrawingBackend`（`IDrawingBackend` 的 D3D12 实现，Phase 1 清屏渲染）
- [x] 从手写 vtable 迁移到 CsWin32 生成的裸指针 COM 包装
- [x] Phase 2: D3D12 矩形绘制（`D3D12Renderer2D`：运行时 HLSL 编译 + 顶点缓冲区）
- [ ] Phase 3: D3D12 文本渲染（暂用 GDI 软件光栅化上传纹理）

### Core

- [ ] 为 `VirtualNode` 增加更清晰的属性建模策略
- [ ] 设计 retained element tree
- [ ] 设计 layout tree
- [ ] 让 `LayoutTreeBuilder` 脱离 PoC 特有布局常量和控件假设
- [ ] 将 diff 从 `ReplaceRoot` 提升到最小可用局部 diff
- [ ] 增加 keyed reconciliation 设计草案

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

- [ ] 为 diff 增加测试
- [x] 为 `PatchBatch.Dispose()` 路径增加测试
- [ ] 为 runtime command 执行增加测试
- [x] 为 Counter PoC 输入路由增加最小测试
- [ ] 为 hit testing 增加测试
- [x] 为 layout / draw pipeline 增加基础测试
- [x] 为 PoC 渲染回归增加最小测试
- [x] 为 `WindowVisualCompositor` 命中测试增加最小覆盖
- [x] 为 `CompositorLoop` 所有权转移增加最小测试

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

`Introduce DrawCommand pipeline skeleton and decouple PoC window compositor from VirtualNodePatch`

对应改动范围建议：

- `Irix.Rendering`
- `Irix.Poc`
- `Irix.Core`（仅补最小桥接类型）
- `Irix.Core.Tests`（补基础测试）
