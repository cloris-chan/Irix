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
- `Irix.Poc`
  - 已有 Counter 示例应用
  - 已有 `WindowVisualCompositor`，能把 `VirtualNode` 渲染成当前 PoC Window 内容
  - 已打通：窗口创建 -> 输入 -> runtime dispatch -> patch 发布 -> PoC 可视化
- `Irix.Core.Tests`
  - 已有最基础 runtime 测试

### 尚未落地的关键内容

- 还没有 `D3D12` 渲染主链
- 还没有 `Skia + D3D12` backend adapter
- 还没有正式的 `DrawCommand` / `IDrawingBackend` 代码骨架
- 还没有正式的 `LayoutTreeBuilder`
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

当前渲染层实际上还是“PoC 可视化层”，不是正式 GPU 渲染层。

`WindowVisualCompositor` 现在同时做了三件事：

1. 从 `VirtualNode` 解析控件语义
2. 执行简单布局
3. 直接生成 PoC Window 内容元素和命中目标

这适合演示，但后续必须拆开。

关键文件：

- [CompositorLoop.cs](/d:/source/Irix/src/Irix.Rendering/CompositorLoop.cs)
- [WindowVisualCompositor.cs](/d:/source/Irix/src/Irix.Poc/WindowVisualCompositor.cs)

### 3.4 Tests

当前测试状态：

- `Irix.Core.Tests` 只有最基础的 runtime patch 发布测试
- 还没有 diff、layout、输入路由、所有权转移、异常/取消路径测试

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

### 未确认或尚未落地

- `D3D12` 具体接入方式
- `Skia + D3D12` 的最终 backend adapter 方案
- 本机 IPC 选型：
  - 命名管道
  - loopback gRPC
  - 其他本机 RPC 封装
- `DrawCommand` 的最终字段设计
- retained tree / layout tree 的具体对象模型

---

## 5. 推荐阅读顺序

新开发者或 AI 工具建议按以下顺序建立上下文：

1. [Irix_Framework_Design.md](/d:/source/Irix/docs/Irix_Framework_Design.md)
2. [Runtime.cs](/d:/source/Irix/src/Irix.Core/Runtime.cs)
3. [VirtualNodeModels.cs](/d:/source/Irix/src/Irix.Core/VirtualNodeModels.cs)
4. [WindowVisualCompositor.cs](/d:/source/Irix/src/Irix.Poc/WindowVisualCompositor.cs)
5. [WindowsPlatformHost.cs](/d:/source/Irix/src/Irix.Platform.Windows/WindowsPlatformHost.cs)
6. [Program.cs](/d:/source/Irix/src/Irix.Poc/Program.cs)
7. [RuntimeTests.cs](/d:/source/Irix/tests/Irix.Core.Tests/RuntimeTests.cs)

---

## 6. 最近最值得做的事

优先级按当前建议顺序排列。

### P0

- 在 `Irix.Rendering` 中建立正式的 `DrawCommand` / `IDrawingBackend` / `DrawCommandBatch` 骨架
- 把 `WindowVisualCompositor` 从“直接消费 `VirtualNodePatch`”改成“消费 `DrawCommandBatch`”
- 抽出 `LayoutTreeBuilder` 或同类职责对象，别让 PoC compositor 继续同时承担布局和绘制语义解释

### P1

- 为 `VirtualNodeDiffer` 制定从 `ReplaceRoot` 走向真实 diff 的演进计划
- 增加 `PatchBatch` / `IMemoryOwner<T>` 异常、取消、释放路径测试
- 增加输入路由和命中测试的最小测试覆盖

### P2

- 开始搭 `D3D12` 最小三角形上屏
- 明确 `SkiaBackend` 只位于 backend adapter 层
- 为 `Local UI Remoting` 起草最小协议：`InputEvent`、`VirtualNodePatch`、`Ack / SeqId`

---

## 7. 当前待办清单

### Rendering / Drawing

- [ ] 新建 `DrawCommand` 数据模型
- [ ] 新建 `IDrawingBackend`
- [ ] 新建 `FrameContext`
- [ ] 新建 `DrawCommandBatch`
- [ ] 实现 `WindowBackend`，使其消费 `DrawCommandBatch`
- [ ] 将 `WindowVisualCompositor` 重构为：
- [ ] `VirtualNodePatch -> LayoutTreeBuilder -> DrawCommandRecorder -> WindowBackend`
- [ ] 搭 `D3D12` 基础渲染循环
- [ ] 评估 `Skia + D3D12` 集成可行性

### Core

- [ ] 为 `VirtualNode` 增加更清晰的属性建模策略
- [ ] 设计 retained element tree
- [ ] 设计 layout tree
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

### Tests

- [ ] 为 diff 增加测试
- [ ] 为 `PatchBatch.Dispose()` 路径增加测试
- [ ] 为 runtime command 执行增加测试
- [ ] 为输入路由增加测试
- [ ] 为 layout / hit testing 增加测试

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

