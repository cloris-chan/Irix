# Irix 项目进度与待办

> 面向开发者、Copilot/Codex 等 AI 工具的当前状态说明。目标是帮助接手者快速判断“哪些已经落地、哪些只是设计、下一步应该从哪里开始”。
> 📅 **最后验证日期：** 2026-05-08。本文档描述的代码状态以此日期为准。
>
> 📐 **架构设计详见：** [Irix_Framework_Design.md](/d:/source/Irix/docs/Irix_Framework_Design.md)。本文档不重复设计细节，仅记录实现状态与待办。
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
  - `Irix.Platform.Windows` 已启用 `CsWin32RunAsBuildTask=true`，CsWin32 绑定以实体 `obj/.../Generated/CsWin32/Windows.Win32.NativeMethods.g.cs` 参与编译，避免依赖易失效的 Roslyn `roslyn-source-generated://` 虚拟文档
  - 已有 `D3D12Renderer`，使用 CsWin32 生成的裸指针 COM 包装（`allowMarshaling: false`），支持设备创建、交换链、清屏、矩形 + 文本帧合成、呈现、resize
  - 已有 `D3D12Renderer2D`，运行时 HLSL 编译 + 顶点缓冲区渲染彩色矩形
  - 已有 `D3D12TextRenderer`，通过 D3D11On12 + Direct2D + DirectWrite 在 D3D12 back buffer 上叠加文本
  - 已有 `D3D12DrawingBackend`（Irix.Poc），Phase 3：FillRect → D3D12 矩形渲染，DrawTextRun → DirectWrite 文本渲染
- `Irix.Rendering`
  - 已有 `ICompositor`
  - 已有 `CompositorLoop`，负责异步消费 `PatchBatch`；已新增合并式 `RequestRenderAsync`，用于 resize 等不改变 VirtualNode 树的显式重绘请求，避免连续 `WM_SIZE` 产生无界空 patch 队列
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
- `VirtualNodeDiffer` 已实现局部 diff：递归深比较 + keyed reconciliation + Update/Add/Remove patches；`default` 树边界处理已完善；193 个测试用例覆盖各场景
- `DrawCommand` 已移除内联 `string? Text`，改为 `TextSlice` + `IFrameResourceResolver` 传递文本内容；`ResourceHandle` 已回归资源职责并用于 `TextStyle`
- DirectWrite backend 已缓存 bounded `IDWriteTextFormat` 与 bounded `IDWriteTextLayout`；显式 glyph atlas/cache 尚未实现，当前仍委托 DirectWrite 内部 glyph rasterization/cache
- 渲染热路径仍有托管分配：`DrawCommandRecorder` 每帧从 `FrameDrawingResources` 静态池 Rent，`RenderFrameBatch.Dispose()` 归还；`D3D12DrawingBackend` 使用 `FrameRenderList<T>`（ArrayPool 背板），每帧 Reset 而非 new；`DrawCommand` 录制走小批量 `stackalloc` + 大批量 pooled owner。`FrameTextArena.Seal()` 从 `ArrayPool` 租用 `char[]` 而非生成 `string`。热路径每帧仅剩 `ArrayPool` rent/return（非 GC 分配）
- `PatchBatch` 已携带 `Root` 属性，消费者不再需要从 `Memory` 中反推根节点
- 测试覆盖已扩展至 158 个测试（含 diff、DrawCommand 文本传递、FrameTextArena、FrameDrawingResources、arena reuse、pool Rent/Return、TextSlice 生命周期、patch 应用、文本渲染正确性、CompositorLoop 合并重绘请求、render request 与 empty diff 区分、RetainedTree patch apply（去重升序 dirty set）、LayoutTree 中间结构（DFS index → element range 映射、VirtualNodeKind 语义）、增量布局 dirty range 计算与合并（父子重叠/相邻区间合并）、DrawCommand range 映射（element→command range）、dirty command range 计算与传递、RangeUtils 工具类、RetainedCommandBuffer 局部替换、RetainedRenderFrame 纯 TryApplyPartial 失败路径、Dispose 安全释放、资源一致性保护与零分配读取、资源 generation 跟踪与显式所有权、FrameDrawingResources Retain/Release/Return 幂等性、DrawingBackendCompositor retained frame 与 partial apply pilot、cross-frame partial guard、compositor 诊断计数、layout dirty v0、retained layout、DrawingBackendCompositor、所有权转移等）
- `CompositorLoop` 已实现合并式显式重绘请求：连续 resize 只保留必要的 repaint，渲染中再次请求会在当前帧后补一帧，避免丢失最新 viewport
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
| 帧消费 (CompositorLoop) | ✅ 已验证（含合并式显式重绘请求） |
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
- D3D12 互操作使用 CsWin32 `allowMarshaling: false` 生成的裸指针 COM 包装；Windows 平台项目使用 `CsWin32RunAsBuildTask=true` 将绑定生成为实体 `obj` 编译输入，减少 Roslyn source-generated virtual document 噪音（ADR-013）
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

- ✅ `VirtualNodeDiffer` 已从 `ReplaceRoot` 提升到局部 diff（Update/Add/Remove + keyed reconciliation）；143 个测试用例
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
- [x] 将 Windows 平台 CsWin32 生成模式切到 build task，生成实体 `Windows.Win32.NativeMethods.g.cs` 并避免编辑器反复请求过期 `roslyn-source-generated://` 文档
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
- [x] `CompositorLoop` 合并式 render request 行为测试：连续请求只排队一次、渲染中 dirty 后补一帧、普通 empty diff 不等同 render request（193 个测试，全部通过）
- [x] 引入最小 `RetainedTree`：单次 DFS 遍历应用 ReplaceRoot/Update/Add/Remove patch，返回去重升序 dirty 节点索引集合；13 个测试覆盖 replace root、update、add、remove、keyed reconciliation、多 patch 组合、empty batch、diff→apply 等价性、dirty 排序去重、layout dirty v0、RenderPipeline dirty-driven rebuild、Translator RetainedTree 集成
- [x] `RenderPipeline` 接入 `RetainedTree`：Translator 持有 RetainedTree，diff batch 调用 Apply 并传递 dirty set，render request 只复用 retained tree；LayoutTreeBuilder 接受 dirty nodes 参数（v0 全量重建）
- [x] Layout dirty v0：`LayoutTreeBuilder.Build(root, viewport, dirtyNodes)` 接口已落地，当前为全量重建，dirty set 透传用于后续增量布局
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
- [x] Nested clip intersection 精确断言：断言交集结果等于预期矩形 `[16, 16, 268, 168]`，而非仅 `<= viewport`
- [x] Hit-test + scroll 联动测试：`ScrollY=30` 使第一个 button 部分滚出 clip；验证 clip 外不命中、clip 内命中；第二个 button 滚入可见区后可命中
- [x] ScrollY v0 清理：删除 `savedCursorY`；`ScrollY` 负值 clamp 到 0；`Math.Clamp(scrollY, 0, maxScrollY)` 防止超大滚动
- [x] 容器高度 v0：`ScrollContainer` 支持 `Height` 属性；无属性时 root 用 `viewportHeight - containerTop`，嵌套容器用内容高度；容器布局后 cursorY 恢复到 `containerTop + containerVisibleHeight + spacing`
- [x] Scroll bounds clamp：根据 content height 和 visible height 计算 `MaxScrollY`，布局时 clamp `ScrollY` 到 `[0, MaxScrollY]`；`OffsetElementY` 在 clamp 后偏移子元素 y 坐标
- [x] Scroll 诊断：`LayoutTreeResult.ScrollDiagnostics` 暴露每个 ScrollContainer 的 `VisibleHeight`、`ContentHeight`、`ScrollY`、`MaxScrollY`；`RenderPipeline.LastLayoutResult` 可访问；`--diagnose` 输出 scroll container 状态
- [x] 抽取 `LayoutContext` 结构体：替代 `availableWidth`/`viewportHeight`/`clipBounds`/`depth`/`style`/`scrollDiags` 参数列表；`Depth` 字段区分 root（0）和 nested（1+）；`DefaultContainerHeight` 根据 depth 决定容器高度语义
- [x] Nested container 高度语义：root 默认剩余 viewport，nested 无 `Height` 属性时用剩余 viewport 或 `Style.TextHeight * 3` 兜底；显式 `Height` 始终优先
- [x] Scroll clamp 测试：覆盖 `ScrollY < 0`（clamp 到 0）、`ScrollY > MaxScrollY`（clamp 到 max）、显式 `Height` 小于内容高度；4 个新测试验证 diagnostics 中 scrollY 被正确 clamp
- [x] 可见元素诊断：`ScrollContainerDiag` 新增 `VisibleElementCount`/`ClippedElementCount`；XML doc 明确语义：VisibleElementCount = 与可见区域相交的元素数，ClippedElementCount = 完全在可见区域外的元素数；`--diagnose` 输出 `elements=2/2 visible`
- [x] 鼠标滚轮事件接入 v0：`CounterInputRouter` 将 `PointerWheel` delta 映射为 `Scroll` 消息（WHEEL_DELTA=120 → 40px/step）；滚轮通过现有 MVU 路径：`Dispatch` → `Update` → `BuildView` → diff → retained tree → layout → render
- [x] Scroll action 建模：`CounterMessage.Scroll(DeltaY)` 走 MVU update，`CounterModel.ScrollY` 作为状态；`BuildView` 将 `ScrollY` 作为 `ScrollContainer` 属性传入布局
- [x] Scroll wheel 单测：wheel delta 120/-120/240 映射正确、Scroll 消息更新 model ScrollY、负 ScrollY clamp 到 0、ScrollY 出现在 view 属性中、ScrollY 产生正确 scroll diagnostics
- [x] ScrollController 纯函数实现：`ApplyWheel` 累计 raw delta（subpixel accumulator），换算 whole pixel 到 `TargetPosition`；`Tick(dt)` 指数 ease `Position` → `TargetPosition`；`SnapThreshold` 自动停止动画；`GetScrollY` 返回整数布局偏移
- [x] Raw wheel delta 保真：`CounterInputRouter` 发送 `Wheel(rawDelta)` 不做整数截断；高精度触摸板小 delta（如 30）通过 accumulator 累计：30×4 = 120 = 40px
- [x] Smooth scroll 动画：`ScrollState` 持有 `Accumulator`/`TargetPosition`/`Position`/`IsAnimating`；`Tick(now/dt)` 消息驱动每帧逼近 target；`IsAnimating=true` 时 `StartTickLoop` 持续 dispatch Tick + request render；动画结束自动停止
- [x] ScrollDelta 结构化：`CounterInputRouter` 发送 `ScrollDelta(ScrollDeltaUnit.WheelRaw, rawDelta)`，不做整数截断；`ScrollDeltaUnit` 枚举支持 `Line`/`Pixel`/`Page`/`WheelRaw`
- [x] ScrollMetrics：`LineExtent`/`PageExtent`/`ViewportExtent`/`ContentExtent`；controller 不再硬编码 40px，通过 `ConvertToPixels(delta, metrics, settings)` 换算
- [x] SystemScrollSettings：PoC 默认 `LinesPerWheelNotch=3`、`WheelUnitsPerNotch=120`；Windows 平台后续可读取 `SPI_GETWHEELSCROLLLINES`
- [x] ScrollState 改为 double：`TargetPosition`/`Position`/`Accumulator` 全部 double 精度；小 delta 不因 int target 丢失
- [x] ApplyScrollDelta：根据 `ScrollDeltaUnit` + `ScrollMetrics` + `SystemScrollSettings` 换算到 pixel target；WheelRaw: `120/120 × 3 lines × 18px = 54px`；30×4 = 120 = 54px
- [x] Scroll 精度测试：120 units × 3 lines × 18px = 54px、30×4 等价一刻度、小 delta 累计、Line/Pixel/Page 换算、backward-compatible ApplyWheel
- [x] Tick loop 单实例 guard：`EnsureScrollTickLoop` 用 `Interlocked.Exchange` 保证同时只有一个 tick loop 在跑；`RunScrollTickLoopAsync` 开头等待 `IsAnimating=true`（带 500ms 超时），结束时连续 3 帧 idle 自然退出
- [x] Scroll dispatch 立即渲染：`HandleInput` 中 scroll 消息后先 `RequestRenderAsync()` 再 `EnsureScrollTickLoop`；`EnsureScrollTickLoop` 无条件调用，不检查 `IsAnimating`（避免读旧 model 竞态）
- [x] Scroll 集成测试：单刻度 target=54px、双刻度 target=108px、正负抵消 target=0、单刻度动画收敛 scrollY=54、双刻度动画收敛 scrollY=108、debug 显示包含 target/pos/acc/applied
- [x] Debug 显示：PoC 文本临时显示 `ScrollY: applied=54 target=54.0 pos=53.87 max=0 acc=0.000 anim=True`，可直接看出 input 是否更新 target、动画是否推进 position
- [x] Wheel coalescing：`HandleInput` 不直接 `Dispatch(Scroll)`；raw delta 累加到 `PendingScrollDelta`（Interlocked CAS），只启动/唤醒 animation loop。快速滚轮 100 个事件不产生 100 次 MVU update
- [x] Per-tick drain：animation loop 每幀讀取並清空 pending delta，合成單個 `ScrollFrame(delta, dt)` 消息。每幀最多一次 scroll update，Tick 不會排在大量 wheel 消息後面
- [x] Clamp target 到 layout max：`ScrollState.MaxScrollY` 由 layout pipeline 通过 `RenderPipeline.LastMaxScrollY` → `WindowDrawCommandTranslator.LastMaxScrollY` → `postFrameCallback` → `UpdateMaxScrollY` 反馈回 model。`ApplyScrollDelta` 將 `TargetPosition` clamp 到 `[0, MaxScrollY]`；`WithMaxScrollY` 在更新时重新 clamp
- [x] MaxScrollY 集成测试：scroll way past content 后 target clamp 到 MaxScrollY；MaxScrollY 更新后 target 被重新 clamp
- [x] ScrollFrame 统一消息：`ScrollFrame(delta, dt)` 合并 scroll delta + animation tick，取代独立的 `Scroll` 和 `Tick` 消息
- [x] `RetainedCommandBuffer`：全量 batch + dirty replacement ranges，内存层验证局部替换（v0，不接 D3D12）
- [x] 明确 retained command 资源生命周期：`RetainedCommandBuffer` 为帧作用域，`TextSlice` 仅在 `FrameDrawingResources` 存活期间有效；partial apply 仅限同帧资源作用域内
- [x] `RetainedRenderFrame`：组合 retained command buffer、resource resolver、dirty command ranges、hit targets；提供 `ApplyFull`、`ApplyPartial`、`Invalidate`、`ToBatch`
- [x] `RenderPipeline` 内部接入 `RetainedRenderFrame` v0：pipeline 输出完整 `RenderFrameBatch`，内部维护 retained frame，用 dirty ranges 更新
- [x] `RetainedTree.Apply` 语义固化：Update 保留 children、Add/Remove DFS index 语义、dirty index 含义已补注释和文档

---

## 7a. 诊断基线（2026-05-09）

以下为 `Irix.Poc --diagnose` 与 `Irix.Poc --diagnose-resize` 的输出基线，供后续对比。

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
| **Layout hit targets** | **1** (LayoutBtn, clip = 16,16,928,524) |
| **ScrollContainer[0]** | **visible=524 content=96 scrollY=0 maxScrollY=0 elements=2/2 visible** |

### `--diagnose-resize` 压力模式

| 指标 | 值 |
|------|-----|
| Device removed | False |
| Device error reason | (none) |
| Swapchain size | 929×454 |
| 退出码 | 0 |

---

## 7b. 已知问题清单（2026-05-08）

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

`Add DirectWrite text rendering to the D3D12 backend`

对应改动范围建议：

- `Irix.Platform.Windows`
- `Irix.Poc`
- `docs`
