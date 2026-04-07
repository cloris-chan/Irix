# Irix 项目进度与待办

> 面向开发者、Copilot/Codex 等 AI 工具的当前状态说明。目标是帮助接手者快速判断“哪些已经落地、哪些只是设计、下一步应该从哪里开始”。

---

## 1. 项目定位

Irix 当前是一个**早期原型期**的原生 .NET UI 框架项目。

当前已经明确的方向：

- v1 / Windows-only PoC 以 `D3D12` 为唯一图形后端
- 上层 UI 不直接绑定第三方绘图库
- Drawing 层采用 `DrawCommand + IDrawingBackend` 的内部抽象方向
- `Skia` 在当前规划中是 backend adapter，而不是长期架构中心
- UI 交付层分为两条产品线：
  - `Local UI Remoting`：loopback-only，本机多进程插件/扩展 UI 接入
  - `Remote UI Delivery`：跨机器、商业版、server-driven UI
- `MVVM bridge` 应保持为轻量编译期 authoring layer，而不是复制 `WPF / WinUI / MAUI` runtime
- `XAML / IXAML` 仅作为 DSL，由 Source Generator 在编译期降解为 C#，不做运行时解析
- `TwoWay Binding` 的本质是生成 MVU 的 `update/state` glue code，不引入第二套权威状态

设计主文档见：[Irix_Framework_Design.md](/d:/source/Irix/docs/Irix_Framework_Design.md)

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
- `Irix.Rendering`
  - 已有 `ICompositor`
  - 已有 `CompositorLoop`，负责异步消费 `PatchBatch`
  - 已有 `ConsoleCompositor` 与 `CompositeCompositor`
  - 已有 `LayoutTreeBuilder`、`LayoutElement`、`DrawCommandRecorder` 过渡骨架
- `Irix.Drawing`
  - 已拆出独立项目骨架
  - 已有 `DrawCommand`、`FrameContext`、`DrawCommandBatch`、`IDrawingBackend` 最小类型
- `Irix.Poc`
  - 已有 Counter 示例应用
  - 已有 `WindowVisualCompositor`，能把 `VirtualNode` 渲染成当前 PoC Window 内容
  - 已有 `WindowBackend`，可将 `DrawCommand` 翻译成 PoC Window 内容元素与命中目标
  - 已将 Counter 示例中的输入映射抽到独立 `CounterInputRouter`
  - 已打通：窗口创建 -> 输入 -> runtime dispatch -> patch 发布 -> PoC 可视化
- `Irix.Core.Tests`
  - 已有最基础 runtime 测试
  - 已有 layout / draw pipeline / `WindowBackend` 基础测试与最近的回归测试

### 尚未落地的关键内容

- 还没有 `D3D12` 渲染主链
- 还没有 `Skia + D3D12` backend adapter
- 还没有真正的 retained tree / layout tree / draw command pipeline
- `VirtualNodeDiffer` 仍然是最小实现，不是完整 diff
- 测试覆盖仍然很薄，目前主要是单个 runtime 测试

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

- `VirtualNodeDiffer` 当前基本等同于“整棵树替换根节点”
- 还没有真正的局部 diff / keyed reconciliation
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

1. 消费 `DrawCommandBatch`
2. 生成 PoC Window 内容元素
3. 维护命中目标
4. 空帧到来时主动清空窗口元素与命中目标，避免上一帧命中信息残留

布局与命令录制已经开始沉到 `Irix.Rendering`，但离正式 retained tree / GPU backend 还有距离。

最近已确认并修复的 PoC 问题：

- `WindowDrawCommandTranslator` 若误用 `new LayoutStyle()`，会因为 `record struct` 默认值全为 `0`，导致文本/按钮高度都退化为 `0`
- `WindowBackend` / `WindowsNativeWindow` 现已补通颜色传递，PoC Window 不再忽略 `DrawCommand.Color`

关键文件：

- [DrawingPrimitives.cs](/d:/source/Irix/src/Irix.Drawing/DrawingPrimitives.cs)
- [IDrawingBackend.cs](/d:/source/Irix/src/Irix.Drawing/IDrawingBackend.cs)
- [LayoutTreeBuilder.cs](/d:/source/Irix/src/Irix.Rendering/LayoutTreeBuilder.cs)
- [DrawCommandRecorder.cs](/d:/source/Irix/src/Irix.Rendering/DrawCommandRecorder.cs)
- [CompositorLoop.cs](/d:/source/Irix/src/Irix.Rendering/CompositorLoop.cs)
- [WindowVisualCompositor.cs](/d:/source/Irix/src/Irix.Poc/WindowVisualCompositor.cs)

### 3.4 Tests

当前测试状态：

- `Irix.Core.Tests` 已有 runtime 测试和 layout/draw pipeline 基础测试
- 已补 `WindowDrawCommandTranslator` 默认布局回归测试
- 已补 `WindowBackend` 颜色映射断言
- 已补 `WindowVisualCompositor` 命中边界与空帧清理测试
- 已补 Counter PoC 输入路由映射测试
- 已补 `PatchBatch` / `DrawCommandBatch` / `CompositorLoop` 基础所有权与释放路径测试
- 还没有 diff、异常/取消路径测试

当前已知行为记录：

- `PatchBatch.Dispose()` 后再次访问 `Memory`，当前实现会因切片边界失效而抛 `ArgumentOutOfRangeException`
- `DrawCommandBatch.Dispose()` 后再次访问 `Memory`，当前实现返回空内存
- `CompositorLoop` 在正常渲染路径中会负责释放传入的 `PatchBatch` 与翻译产出的 `DrawCommandBatch`

关键文件：

- [RuntimeTests.cs](/d:/source/Irix/tests/Irix.Core.Tests/RuntimeTests.cs)

---

## 4. 当前架构决策

下面这些应视为**当前生效中的架构决策**。

### 已确认

- Windows-only PoC 阶段只做 `D3D12`
- `Vulkan` 后移到后续阶段
- `Skia` 不直接暴露给上层 UI
- 上层依赖目标是 `DrawCommand + IDrawingBackend`
- 免费/开源许可方向优先做 `Local UI Remoting`
- 商业版方向做 `Remote UI Delivery`
- `MVVM bridge` 只做轻量编译期桥接，不复制 `DependencyProperty` / `VisualState` 运行时
- `XAML / IXAML` 仅作为 DSL，不做运行时解析
- `TwoWay Binding` 编译为 MVU 的 `Message + Update` glue code

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

- 为 `VirtualNodeDiffer` 制定从 `ReplaceRoot` 走向真实 diff 的演进计划
- 增加 `PatchBatch` / `IMemoryOwner<T>` 异常、取消、释放路径测试
- 增加输入路由和命中测试的最小测试覆盖

### P2

- 开始搭 `D3D12` 最小三角形上屏
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
- [x] 实现 `WindowBackend`，使其消费 `DrawCommandBatch`
- [x] 将 `WindowVisualCompositor` 从 patch 直接消费改成 draw command 消费
- [x] 建立 `VirtualNodePatch -> LayoutTreeBuilder -> DrawCommandRecorder -> WindowBackend` 过渡链
- [x] 打通 PoC Window 对 `DrawCommand.Color` 的映射
- [ ] 搭 `D3D12` 基础渲染循环
- [ ] 评估 `Skia + D3D12` 集成可行性

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

为了避免范围膨胀，接手者不要优先做下面这些事情：

- 不要先做 `Vulkan`
- 不要先做跨平台
- 不要先做完整多屏热插拔
- 不要先做复杂动画系统
- 不要先做完整 `Remote UI Delivery`
- 不要先做自研 Drawing Engine
- 不要把上层代码直接绑到 `Skia` 对象模型
- 不要在 `WindowVisualCompositor` 上继续叠更多正式架构职责
- 不要做运行时 `XAML / IXAML` parser
- 不要复制 `DependencyProperty`、`VisualStateManager`、`ResourceDictionary` 那套完整 runtime
- 不要引入第二套 `ViewModel` 权威状态再与 `Model` 同步

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
