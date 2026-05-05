# Irix — 高性能原生 .NET UI 框架 架构设计书

> 📌 **文档说明：** 本文档在原版基础上重新整理，增加了版本边界速查、风险热力图，并将散落在各章节的版本约束统一归拢，以便快速定位 v1 范围与后续演进计划。当前版本进一步收敛了 v1 的执行边界，并补充了 Drawing 层的独立演进策略。

---

## 目录

1. [背景与动机](#1-背景与动机)
2. [设计原则](#2-设计原则)
3. [版本边界速查](#3-版本边界速查) ⭐ 新增
4. [整体架构概览](#4-整体架构概览)
   - 4.3 [线程模型](#43-线程模型)
5. [渲染与 Drawing 架构](#5-渲染与-drawing-架构)
   - 5.4 [输入路由与焦点模型](#54-输入路由与焦点模型)
   - 5.5 [布局层设计](#55-布局层设计)
   - 5.6 [可观测性与诊断](#56-可观测性与诊断)
6. [MVU 状态核心](#6-mvu-状态核心)
7. [内存管理与数据管线](#7-内存管理与数据管线)
   - 7.4 [错误处理与恢复策略](#74-错误处理与恢复策略)
8. [运行模式与网络层](#8-运行模式与网络层)
9. [生态兼容层](#9-生态兼容层)
10. [技术栈清单](#10-技术栈清单)
11. [已知技术风险清单](#11-已知技术风险清单)
12. [分阶段交付计划](#12-分阶段交付计划)
13. [附录：竞品对比](#13-附录竞品对比)
- [附录 A：术语表](#附录-a术语表)
- [附录 B：架构决策记录索引](#附录-b架构决策记录索引-adr)
- [附录 C：非目标清单与边界声明](#附录-c非目标清单与边界声明)

---

## 1. 背景与动机

### 1.1 现有框架的痛点

| 框架 | 主要痛点 |
|------|---------|
| **Electron** | 内存开销巨大（每个窗口独立 Chromium 进程），启动慢，不适合原生桌面体验 |
| **MAUI / WinUI 3** | 多显示器 DPI 异构处理不完善；MVVM 样板代码重；AOT 支持不稳定 |
| **Blazor Desktop** | WebView 渲染层带来不可控的性能上限；离线场景下体验退化明显 |
| **Avalonia** | Skia 渲染质量较好，但缺乏 Server-Driven 能力；多线程渲染模型不完整 |

### 1.2 差异化定位

Irix 并非在所有方向上超越竞品，而是专注解决以下三个核心场景：

1. **多显示器异构环境**（DPI、刷新率、色彩空间各异）下的无撕裂渲染
2. **极致低内存开销**的长期运行桌面应用（工业控制、监控大屏、金融终端）
3. **原生 UI 交付层**：同时覆盖 `Local UI Remoting`（插件/扩展进程通过 loopback RPC 接入宿主原生 UI）与 `Remote UI Delivery`（远程 server-driven UI 薄客户端）

**平台定位：** v1.0 目标平台为 **Windows 10/11 (x64)**。跨平台支持（macOS / Linux）作为 v2.0 目标，但窗口抽象接口从 Day 1 设计为可替换，避免后期重构债务。

---

## 2. 设计原则

以下原则按优先级排序，冲突时高优先级优先。

| 优先级 | 原则 | 含义 |
|--------|------|------|
| P0 | **正确性优先** | 内存安全和线程安全不可妥协，即使以性能为代价 |
| P1 | **可测试性** | MVU Core 必须可在无渲染环境下完整单元测试 |
| P1.5 | **范围收敛优先** | v1 只做一条可验证主路径：本地模式 + 单图形后端 + 最小控件集合 |
| P2 | **零拷贝热路径** | 每帧渲染路径上不允许托管堆分配（0 GC pressure） |
| P3 | **接口隔离** | 渲染后端、网络后端、平台后端均面向接口编程，可独立替换 |
| P4 | **渐进式采用** | 开发者可以先用 MVVM 习惯接入，逐步迁移到 MVU 原生模式 |

---

## 3. 版本边界速查

> ⭐ 本章为新增内容，将原文档中散落于各章节的版本说明统一汇总，方便快速定位各能力的交付节奏。

### v1.0 GA 范围（首版必须交付）

| 能力 | 说明 |
|------|------|
| C# MVU Core（本地模式） | Model / Update / View 三元组，纯函数，可单元测试 |
| 单图形后端（Windows PoC） | `D3D12` 作为 v1 / Windows-only PoC 唯一图形后端，`Vulkan` 后移到后续阶段 |
| Win32 主 HWND 窗口 | 单窗口、单主视口优先 |
| 内部 Drawing 抽象层 | UI 树不直接耦合第三方绘图库；v1 用内部 `DrawCommand` 层 + `Skia` 适配器落地 |
| MPSC + `IMemoryOwner<T>` 管线 | 线程间所有权转移通道 |
| 最小控件集合 | Text、Rectangle、Button、ScrollContainer |
| 基础输入路由 | 鼠标 / 键盘，焦点切换 |
| MVU Diff / Patch 管线 | VirtualNode Patch 输出与单消费者渲染链路 |
| 基础单元 / 属性测试 | xUnit + FsCheck |

### v1.1 范围（在 v1 主路径稳定后交付）

| 能力 | 说明 |
|------|------|
| 受限多屏异构渲染 | 覆盖单屏 / 双屏固定拓扑两类场景 |
| `ScreenTopologyManager` 受限版本 | 监听 `WM_DISPLAYCHANGE`，管理子视口生命周期 |
| 时间轴插值动画系统 | 基于高精度时间戳的跨屏同步动画 |
| `Ghost Event Window`（评估引入） | 独立输入接收窗口，收益不足则保留为实验特性 |

### v1.x 范围（在 v1 主路径稳定后交付的产品化增强）

| 能力 | 说明 |
|------|------|
| Local UI Remoting | 基于 loopback RPC 的本机多进程 UI 扩展模型，适合插件/扩展宿主 |
| 本机 UI 传输层 | 命名管道 / loopback gRPC / 其他本机 IPC 封装 |
| 插件宿主模型 | 插件握手、生命周期、挂载区域、资源配额与宿主控制权 |

### v2.0 范围（后续独立规划）

| 能力 | 说明 |
|------|------|
| Remote UI Delivery | FlatBuffers Schema + gRPC 双向流 + 乐观更新与回滚，用于跨机器 UI 下发 |
| MVVM → MVU Source Generator 桥接 | 轻量编译期桥接；`XAML/IXAML` 仅作为 DSL；双向绑定编译为 MVU 消息与状态更新；无运行时解析与反射式 Binding Engine |
| F# 一等互操作层 | Discriminated Unions + 模式匹配 |
| 第二图形后端 / 后端扩展 | 在 `D3D12` 主链稳定后，再补齐 `Vulkan` 或其他后端 |
| 自研 Drawing Engine（评估项） | 仅在 `DrawCommand` 层稳定且 `Skia` 明确成为瓶颈后启动 |
| Native AOT 完整兼容矩阵 | 发布链路验证 |
| 跨平台支持（macOS / Linux） | IPlatformHost 替换实现 |
| 完整多屏热插拔 | 跨 GPU、跨色彩空间组合 |

---

## 4. 整体架构概览

下图为**终局目标蓝图**。但 v1 实际交付只实现 `C# MVU → MVU Core → 本地 MPSC → 单 Compositor → DrawCommand → 单图形后端 → Win32 Platform Host` 这一条主路径，避免提前为多屏、多后端、网络模式支付复杂度。

```
┌─────────────────────────────────────────────────────────────────┐
│                        应用层 (App Layer)                        │
│           C# MVU  /  F# MVU  /  C# MVVM (via Bridge)           │
└───────────────────────────┬─────────────────────────────────────┘
                            │ Message / Model
┌───────────────────────────▼─────────────────────────────────────┐
│                    MVU Core Engine (纯 C#)                       │
│   Model ──Update──► Model'   │ Model ──View──► VirtualNode Tree │
│        (ref struct Patch)    │ (Diff → VirtualNode Patch List)  │
└──────────┬────────────────────────────────────┬─────────────────┘
           │ MPSC Channel                        │ MPSC Channel
           │ (IMemoryOwner<byte> Move)            │ (IMemoryOwner<byte> Move)
┌──────────────────────────▼─────────────────────────────────────┐
│                    Compositor / Drawing Layer                   │
│   Retained UI Tree → Layout → DrawCommand List → GPU Backend   │
└──────────────────────────▲─────────────────────────────────────┘
                           │
┌──────────────────────────┴─────────────────────────────────────┐
│                平台抽象层 (IPlatformHost)                       │
│   主 HWND │ 子视口管理（v1.1）│ 高精度Timer │ Ghost Window(实验)│
└───────────────────────────────────────────────────────────────┘
           │ [可选]  UI 交付层
┌──────────▼───────────────────────────────────────────────────┐
│      Local UI Remoting / Remote UI Delivery Transport         │
│      FlatBuffers VirtualNode Patch ◄──► App / Plugin Core     │
└──────────────────────────────────────────────────────────────┘
```

### 4.1 关键数据流（本地模式，v1）

```
用户输入事件
  → 主 HWND (Win32 WndProc；Ghost Window 保留为实验能力)
  → MPSC 事件队列 (原始事件, 零分配)
  → MVU Core: dispatch Message
  → Update(Model, Message) → Model'
  → BuildView(Model') → VirtualNode Patch List
  → Retained UI Tree / Layout Tree 更新
  → DrawCommand List 生成
  → Compositor MPSC 队列 (所有权转移)
  → Compositor Thread: 执行 DrawCommand
  → GPU Present (与屏幕 VSync 对齐)
```

**数据流各阶段验证状态（截至当前 PoC）：**

| 阶段 | 组件 | 验证状态 | 说明 |
|------|------|---------|------|
| 输入采集 | `WindowsPlatformHost` / `WindowsNativeWindow` | ✅ 已验证 | 基础鼠标/键盘事件流已打通 |
| 事件传递 | MPSC 事件队列 | ⚠️ 部分 | Channel 基础可用，异常/取消路径未测 |
| 消息派发 | `IMessageDispatcher` / `Runtime` | ✅ 已验证 | 基础 dispatch + Update 循环已测试 |
| 视图构建 | `BuildView` → `VirtualNode` | ⚠️ 部分 | 基础树构建可用，属性模型待完善 |
| Diff / Patch | `VirtualNodeDiffer` | ⚠️ 深比较已实现 | 递归节点等价判断，无变化跳过 ReplaceRoot；尚无局部 diff |
| 布局 | `LayoutTreeBuilder` | ⚠️ 部分 | Retained layout 已引入，未脱离 PoC 硬编码常量 |
| 命令录制 | `DrawCommandRecorder` | ⚠️ 部分 | 基础录制可用，TextRunEntry 已分离，未接入真实 GPU backend |
| 帧消费 | `CompositorLoop` | ✅ 已验证 | 消费 + 所有权转移 + 释放 + 无变化跳过 |
| GPU 渲染 | D3D12 / SkiaBackend | ✅ Phase 1 已验证 | D3D12 清屏渲染已接入 PoC；CsWin32 裸指针 COM 包装已替代手写 vtable |
| PoC 可视化 | `WindowVisualCompositor` | ✅ 已验证 | PoC Window 内容元素 + 命中目标已通 |

### 4.2 最小化 PoC 项目结构（当前仓库）

```text
src/
├─ Irix.Core/              # MVU Core、VirtualNode 模型、Diff/Patch、调度运行时
├─ Irix.Drawing/           # Drawing 抽象：DrawCommand、FrameContext、IDrawingBackend
├─ Irix.Rendering/         # Compositor 抽象、Patch 消费循环、backend 编排与诊断输出
├─ Irix.Platform/          # 平台无关抽象：IPlatformHost、屏幕与输入模型
├─ Irix.Platform.Windows/  # Windows 平台宿主 PoC 实现
└─ Irix.Poc/               # 最小示例应用（Counter）

tests/
└─ Irix.Core.Tests/        # MVU Runtime 与 Patch 发布基础测试
```

> **命名统一说明：** PoC 已统一采用 C# 风格命名，例如 `VirtualNode`、`VirtualNodePatch`、`Message`、`Command`、`IMessageDispatcher`，避免继续沿用 `VNode`、`Msg` 这类缩写式标识。完整术语对照见 [附录 A：术语表](#附录-a术语表)。

### 4.3 线程模型

> v1 采用**严格单线程更新 + 单线程渲染**模型，避免过早引入多线程复杂度。

#### 4.3.1 线程角色定义

| 线程 | 职责 | 生命周期 |
|------|------|---------|
| **Platform Thread** | Win32 消息循环 (`GetMessage` / `DispatchMessage`)；原始输入事件采集；窗口创建与销毁 | 应用进程生命周期 |
| **MVU Core Thread** | 串行执行 `Update`、`BuildView`、`Diff`；维护权威 `Model` 状态 | 应用进程生命周期 |
| **Compositor Thread** | 消费 `PatchBatch`；执行布局与绘制；驱动 GPU Present | 应用进程生命周期 |

> **v1 约束：** MVU Core Thread 和 Compositor Thread 各为独立线程，通过 MPSC Channel 通信。Platform Thread 可以是 MVU Core Thread 本身（PoC 阶段），也可以是独立线程（正式实现）。

#### 4.3.2 线程间通信路径

```text
Platform Thread
  │ RawInputEvent (via IObservable / Channel)
  ▼
MVU Core Thread
  │ Dispatch(message) → Update → BuildView → Diff
  │ PatchBatch (IMemoryOwner 转移，via MPSC Channel)
  ▼
Compositor Thread
  │ Consume PatchBatch → Layout → DrawCommand → GPU Present
  │ Dispose IMemoryOwner → 归还 MemoryPool
```

#### 4.3.3 同步约束

- **Update 串行性**：`IMessageDispatcher.Dispatch()` 可从任意线程调用（线程安全），但 `Update` 执行保证在 MVU Core Thread 上串行化。
- **所有权单次转移**：`IMemoryOwner<T>` 从 Core Thread 转移到 Compositor Thread 后，Core 侧不再持有引用（参见 §7.2）。
- **无跨线程共享可变状态**：两个线程之间唯一的数据通道是 MPSC Channel 的 `IMemoryOwner<T>` 转移，不存在共享可变对象。
- **v1 不使用锁**：线程间同步完全依赖 Channel 的生产者-消费者语义，不引入 `lock`、`Mutex` 或其他同步原语。

#### 4.3.4 未来演进

- v1.1：每个屏幕独立 Compositor Thread（多屏异构渲染）。
- v1.x：Platform Thread 独立化，Ghost Event Window 在独立线程上运行消息循环。

---

## 5. 渲染与 Drawing 架构

### 5.1 幽灵事件窗口 (Ghost Event Window)

- 使用 `CreateWindowEx` 创建一个**不可见、透明、无边框**的顶层窗口。
- **唯一职责**：接收 OS 输入事件（WM_MOUSEMOVE、WM_KEYDOWN、WM_TOUCH 等），将原始 `HWND/WPARAM/LPARAM` 封装为 `RawInputEvent`（`readonly ref struct`）压入 MPSC 通道。
- 不参与任何渲染，彻底避免"输入处理"与"渲染循环"的线程竞争。

> **v1 收敛策略：** 首个公开版本允许先由主 `HWND` 承担输入接收职责，只保留 `Ghost Event Window` 的抽象接口与实验实现。待本地渲染主路径稳定后再完整迁移，避免 PoC 阶段同时处理窗口拓扑和输入重路由两类复杂性。

#### 5.1.1 幽灵事件窗口 (Ghost Event Window)

- 使用 `CreateWindowEx` 创建一个**不可见、透明、无边框**的顶层窗口。
- **唯一职责**：接收 OS 输入事件（WM_MOUSEMOVE、WM_KEYDOWN、WM_TOUCH 等），将原始 `HWND/WPARAM/LPARAM` 封装为 `RawInputEvent`（`readonly ref struct`）压入 MPSC 通道。
- 不参与任何渲染，彻底避免"输入处理"与"渲染循环"的线程竞争。

> **v1 收敛策略：** 首个公开版本允许先由主 `HWND` 承担输入接收职责，只保留 `Ghost Event Window` 的抽象接口与实验实现。待本地渲染主路径稳定后再完整迁移，避免 PoC 阶段同时处理窗口拓扑和输入重路由两类复杂性。

#### 5.1.2 独立子视口 (Per-Screen HWND)

- 当检测到应用窗口跨越多个显示器时，在后台静默创建对应数量的**无边框子 HWND**，精准覆盖各屏幕物理区域。
- 每个子 HWND 对应一个 `ICompositor` 实例，持有独立的 Swapchain。
- 子视口的创建和销毁对上层 MVU 逻辑透明，由 `ScreenTopologyManager` 负责监听 `WM_DISPLAYCHANGE` 并自动管理生命周期。

> **v1 收敛策略：** v1 只要求主 `HWND` + 单主视口闭环可用。独立子视口、多屏固定拓扑与更复杂的热插拔组合统一下放到 v1.1。

#### 5.1.3 平台抽象接口

```csharp
// 平台窗口抽象 - 为跨平台预留
public interface IPlatformHost : IDisposable
{
    IObservable<RawInputEvent> RawInputEvents { get; }
    IReadOnlyList<IScreenInfo> Screens { get; }
    event Action<ScreenTopologyChangedArgs> TopologyChanged;
    INativeWindow CreateSubViewport(in ScreenRegion region);
}

public interface IScreenInfo
{
    int Id { get; }
    float DpiScale { get; }
    int RefreshRateHz { get; }
    ColorSpace ColorSpace { get; }
    PixelRect PhysicalBounds { get; }
}
```

### 5.2 合成引擎线程 (Compositor)

v1 只实现**单 `CompositorThread` + 单主视口**。每个屏幕一个独立 `CompositorThread` 仍然是长期目标，但不作为 PoC 和 v1 GA 的预设复杂度。

#### 5.2.1 合成引擎生命周期

```
CompositorThread.Start()
  ├─ 初始化图形 API 设备 (v1 / Windows PoC: D3D12)
  ├─ 创建 Swapchain (匹配当前屏幕刷新率)
  ├─ 初始化 Drawing Backend Context（v1 默认由 Skia 适配器承接）
  └─ 进入渲染循环:
       while (!cancellation.IsCancellationRequested)
         ├─ 等待 VSync 信号 (或高精度 Timer)
                 ├─ 消费 MPSC 队列中的 Patch / RenderFrameBatch
         ├─ 执行布局 (Measure/Arrange)
         ├─ 执行绘制 (DrawCommand → Backend Adapter)
         ├─ Submit CommandBuffer / Present Swapchain
         └─ 归还 IMemoryOwner 到 MemoryPool
```

#### 5.2.2 图形 API 选型

| API | 交付阶段 | 说明 |
|-----|---------|------|
| **Direct3D 12** | **v1 / Windows PoC** | Windows-only 阶段的唯一图形后端；更贴近 Windows / DXGI / PIX 工具链 |
| **Vulkan** | vNext | 在 `D3D12` 主链稳定后补齐，用于后续跨平台或后端对照验证 |
| **第二后端** | vNext | 首条主链稳定后，再补另一条图形后端，避免双线调试 |

> ⚠️ Windows-only PoC 阶段不再并行评估 `Vulkan`。v1 只围绕 `D3D12` 打通主链，避免双线验证稀释执行力。

#### 5.2.3 后端选型决策标准

`D3D12` 已作为 Windows-only PoC 的固定选择。对 v1 来说，重点不再是后端比选，而是尽快证明 `D3D12 + DrawCommand + Backend Adapter` 这条链路在 Windows 上稳定可交付。验收标准按优先级排序如下：

1. 文本、矩形、裁剪、透明度等基础图元能否稳定渲染。
2. 资源创建、销毁、设备丢失恢复路径是否容易做对。
3. 调试与诊断链路是否顺手（崩溃定位、显存观察、帧分析）。
4. 与 `Skia` 适配器或未来自研 Drawing Backend 的边界是否清晰。
5. 是否为后续跨平台保留合理空间。

#### 5.2.4 Drawing 层策略

Irix 不应将 UI 语义层直接绑定到 `Skia` API。`Skia` 更适合作为**v1 的底层绘制实现**，而不是长期的架构中心。

v1 的推荐分层如下：

```
VirtualNode Patch
  → Retained UI Tree
  → Layout Result
  → DrawCommand List
  → IDrawingBackend
  → Skia / Future Backend
```

其中 `DrawCommand` 是 Irix 自己的稳定中间层，至少覆盖：

- `FillRect`
- `StrokeRect`
- `DrawTextRun`
- `PushClipRect` / `PopClip`
- `PushTransform` / `PopTransform`
- `DrawPath`
- `DrawImage`（可延后）

对应抽象建议：

```csharp
public interface IDrawingBackend : IDisposable
{
    void BeginFrame(in FrameContext frameContext);
    void Execute(ReadOnlySpan<DrawCommand> commands);
    void EndFrame();
}
```

> **设计原则：** 上层布局、命中测试、动画、Patch Routing 只依赖 `DrawCommand` 语义，不依赖 `Skia` 的 `Canvas`、`Paint`、`Path` 等具体对象模型。
>
> **当前过渡实现补充：** 在文本资源系统落地前，`DrawTextRun` 可以临时携带内联文本；但点击/命中等交互语义必须通过与绘制命令并行的 `HitTestTarget[]` / `RenderFrameBatch` 传递，不能回流到 `DrawCommand` 字段。

#### 5.2.5 是否需要自研 Drawing Engine

短答案：**现在不应该直接自研，但应该从 Day 1 把路铺好。**

原因如下：

1. `Skia` 在文本、路径、栅格化成熟度上足够高，适合验证 UI 主链。
2. 当前项目的核心风险首先在窗口、调度、布局、Patch、输入与渲染闭环，而不是 2D 栅格化算法本身。
3. 如果现在直接做类似 `Flutter Impeller` 的自研 Drawing Engine，会把图形 API、路径 tessellation、文本 shaping、缓存策略、AA、资源生命周期等风险一次性叠满。

但从设计理念上，你的直觉是对的：`Skia` 并不天然契合 Irix 的全部目标，特别是下面几类诉求：

- 严格可控的命令录制与资源生命周期
- 更贴近 UI 场景的 retained drawing / partial invalidation
- 更稳定的多线程与多视口资源模型
- 对热路径分配、缓存、诊断的细粒度掌控

因此更合理的路线不是“继续深绑 Skia”，而是：

- v1：`DrawCommand` + `SkiaBackend`
- v1.x：补齐文本缓存、画刷缓存、命令录制和局部重绘基线
- v2：如果 `Skia` 在性能模型、资源模型、可诊断性上持续成为瓶颈，再评估自研 `Irix.Drawing` 引擎

只有在以下条件同时满足时，才建议启动自研 Drawing Engine：

1. `DrawCommand` 语义已经稳定，且至少支撑过一个完整 MVP。
2. 已经通过基线监测确认瓶颈确实在 `Skia`/第三方绘制实现，而不是布局、Patch、文本或窗口层。
3. 团队愿意长期承担文本 shaping、路径栅格化、GPU 资源系统、调试工具的维护成本。

#### 5.2.6 Drawing 层最小对象模型（建议落地稿）

为了让 `DrawCommand` 真正成为稳定边界，v1 建议把 Drawing 层拆成三类对象：

1. **Layout 输出对象**：描述元素最终几何、裁剪和命中区域。
2. **绘制命令对象**：描述最终如何画，而不是“它原本是什么控件”。
3. **资源引用对象**：描述文本样式、画刷、图片、路径等可缓存资源。

建议的最小结构如下：

```csharp
public readonly record struct LayoutBox(
    int NodeId,
    PixelRectangle Bounds,
    PixelRectangle ClipBounds,
    int ZIndex = 0);

public enum DrawCommandKind : byte
{
    FillRect,
    StrokeRect,
    DrawTextRun,
    PushClipRect,
    PopClip,
    PushTransform,
    PopTransform,
    DrawPath,
    DrawImage
}

public readonly record struct DrawCommand(
    DrawCommandKind Kind,
    DrawRect Rect = default,
    DrawColor Color = default,
    ResourceHandle Resource = default,
    float StrokeWidth = 1,
    Matrix3x2 Transform = default,
    int ZIndex = 0);

public readonly record struct DrawRect(float X, float Y, float Width, float Height);

public readonly record struct DrawColor(byte A, byte R, byte G, byte B);

public readonly record struct ResourceHandle(int Id, DrawingResourceKind Kind);

public enum DrawingResourceKind : byte
{
    None,
    TextStyle,
    Brush,
    Image,
    Path
}
```

> **关键约束：** `DrawCommand` 必须是“可序列化、可记录、可回放、可做 diff 诊断”的稳定数据结构，不能把 `SKPaint`、`SKPath`、`SKImage` 这类后端对象直接塞进去，也不能把 `ActionId`、命中目标、按钮语义等交互元数据塞进去。

#### 5.2.7 布局树与 DrawCommand 的边界

`VirtualNode` 不应该直接映射成 backend API 调用，而应该先映射到稳定的布局结果与绘制语义：

```text
VirtualNode
  → Element Tree
  → Layout Tree
  → LayoutBox[]
  → DrawCommand[]
  → IDrawingBackend.Execute(...)
```

各层职责建议如下：

- `VirtualNode`：声明式输入，保留控件语义。
- `Element Tree`：补全控件状态、继承属性、焦点状态、命中测试元数据。
- `Layout Tree`：负责 Measure / Arrange，产出最终几何。
- `DrawCommand`：只表达绘制，不再携带按钮、滚动容器等高层控件语义。

这层拆分的价值在于：

- hit testing 可以依赖 `LayoutBox`，而不是倒推绘制结果；
- backend 替换时无需改 MVU / layout；
- 以后做局部重绘、录制回放、远程诊断会更自然。

#### 5.2.8 与当前 PoC 的衔接方式

当前仓库中的 `WindowVisualCompositor` 已经收敛为 PoC backend 层，当前主要承担两件事：

1. 消费 `RenderFrameBatch`（当前过渡实现为 `DrawCommandBatch + HitTestTarget[]`）；
2. 把绘制命令和并行命中目标翻译成 PoC 窗口内容元素与点击路由数据。

这对于演示是合适的，但不适合作为正式 rendering 架构的终点。推荐演进顺序如下：

1. 把 `WindowVisualCompositor` 中的布局逻辑抽成 `LayoutTreeBuilder`。
2. 把 `WindowContentElement` 视为临时的 `DrawCommand` 替身，先建立 `DrawCommandRecorder`。
3. 保持 `WindowVisualCompositor` 只消费 `RenderFrameBatch`，不要再把 `ActionId` / hit testing 元数据塞回 `DrawCommand`。
4. 等 `SkiaBackend` 接入后，PoC Window backend 与 `SkiaBackend` 共享同一套 `DrawCommand` 输入。

也就是说，当前 PoC 最务实的下一步不是"立刻写 Skia"，而是先把中间层站稳：

`VirtualNodePatch -> LayoutTreeBuilder -> DrawCommandRecorder -> RenderPipeline -> RenderFrameBatch -> WindowBackend`

**已落地：** 这条链路已经稳定运行。`DrawCommand` 已移除内联文本（ADR-011）；`RenderPipeline` 已引入 retained layout；`VirtualNodeDiffer` 已实现递归深比较；`CompositorLoop` 已实现无变化帧跳过。`IDrawingBackend` 已两条路径落地：`PoCDrawingBackend`（GDI Window）+ `D3D12DrawingBackend`（D3D12 清屏）。D3D12 互操作已从手写 vtable 迁移到 CsWin32 生成的裸指针 COM 包装（ADR-013）。

**下一步：** D3D12 Phase 2（矩形绘制 + 文本渲染），让 `LayoutTreeBuilder` 脱离 PoC 硬编码常量，引入增量布局。

#### 5.2.9 文本、路径与图片的资源策略

自研 Drawing 层最容易失控的地方通常不是矩形，而是文本和路径。因此 v1 建议明确分层：

- 文本 shaping 与 glyph rasterization：暂时委托给 `Skia`。
- 复杂路径栅格化：暂时委托给 `Skia`。
- 图片解码与上传：使用独立资源接口封装，避免与 backend 紧耦合。

对应地，`Irix.Drawing` 在 v1 不应该尝试自研：

- text shaping engine
- GPU path tessellator
- image codec pipeline

v1 应该先把这些能力包装成“可替换资源服务”：

```csharp
public interface ITextRasterizer
{
    TextLayoutResult Layout(TextLayoutRequest request);
}

public interface IImageResourceManager
{
    ResourceHandle GetOrCreate(ImageSource source);
}
```

这样后续即使自研 drawing engine，也可以分阶段替换：

- 先替换命令执行器；
- 再替换缓存系统；
- 最后再考虑是否替换文本和路径底层实现。

#### 5.2.10 Drawing 层非目标（v1）

为了避免再次范围膨胀，v1 明确不做以下事情：

- 不做完整 retained scene graph 动画系统
- 不做通用矢量编辑器级路径能力
- 不做复杂文本排版引擎
- 不做 GPU 驱动无关的自研 shader/material 系统
- 不做类似 Impeller 的全链路自研 2D GPU renderer

v1 Drawing 层的成功标准只有两个：

1. 对上能稳定承接 Irix 自己的 UI 布局与绘制语义。
2. 对下能让 `WindowBackend` 与 `SkiaBackend` 共用同一份 `DrawCommand` 输入。

### 5.3 时间轴动画系统

- 动画系统**不依赖帧序号**，仅依赖全局高精度物理时间戳（`Stopwatch.GetTimestamp()`，精度 < 1µs）。
- 每帧插值公式：$value = lerp(from, to, easing((now - startTime) / duration))$
- 跨屏幕的同一动画元素，由各自 Compositor 独立插值。由于时间基准相同，视觉上完全同步，彻底消除因刷新率不同导致的"状态撕裂"。

### 5.4 输入路由与焦点模型

> 本节定义 v1 的输入事件流转、命中测试与焦点管理的最小设计。完整键盘导航、无障碍输入等能力作为后续演进。

#### 5.4.1 输入事件数据模型

```csharp
// 原始平台输入事件 — 从 Platform Thread 采集
public readonly ref struct RawInputEvent
{
    public InputEventType Type { get; }
    public long TimestampTicks { get; }  // Stopwatch.GetTimestamp()
    public InputEventData Data { get; }  // union-like blittable
}

public enum InputEventType : byte
{
    MouseMove,
    MouseButtonDown,
    MouseButtonUp,
    MouseWheel,
    KeyDown,
    KeyUp,
    FocusIn,
    FocusOut
}

// 鼠标事件数据
public readonly record struct MouseEventData(
    float ClientX, float ClientY,
    MouseButton Button,
    ModifierKeys Modifiers);

// 键盘事件数据
public readonly record struct KeyboardEventData(
    int VirtualKeyCode,
    int ScanCode,
    ModifierKeys Modifiers,
    bool IsRepeat);
```

#### 5.4.2 命中测试模型

命中测试依赖 `LayoutBox`（参见 §5.2.6）的几何信息，**不依赖** `DrawCommand` 的绘制结果。

```csharp
// 命中目标 — 与 DrawCommand 并行传递，不嵌入 DrawCommand
public readonly record struct HitTestTarget(
    int NodeId,           // 对应 VirtualNode / Element 的标识
    PixelRectangle Bounds,// 命中区域（与 LayoutBox.Bounds 一致）
    HitTestBehavior Behavior);

public enum HitTestBehavior : byte
{
    Opaque,      // 接收输入事件，阻止向下传递
    Transparent, // 接收输入事件，允许穿透
    Ignore       // 不参与命中测试（如装饰性元素）
}
```

**命中测试流程：**

1. 鼠标事件到达时，从 `HitTestTarget[]` 的**末尾**（最高 ZIndex）向前遍历。
2. 找到第一个包含鼠标坐标且 `Behavior != Ignore` 的目标。
3. 如果目标为 `Opaque`，事件到此为止；如果为 `Transparent`，继续向前传递。
4. 命中结果封装为 `InputRouteResult`，随 `Message` 一起交给 MVU Core。

#### 5.4.3 焦点模型（v1 最小集）

```csharp
public interface IFocusManager
{
    int? FocusedNodeId { get; }
    void RequestFocus(int nodeId);
    void MoveFocus(FocusDirection direction);
}

public enum FocusDirection : byte
{
    Next,       // Tab
    Previous,   // Shift+Tab
    Up, Down, Left, Right  // 方向键
}
```

**v1 焦点约束：**

- 焦点管理由 MVU Core 侧的 `Model` 字段驱动（如 `FocusedNodeId`），不引入独立的运行时焦点树。
- `FocusIn` / `FocusOut` 事件通过 `InputEventType` 传递，由 `Update` 函数处理。
- Tab 导航顺序由控件在 `VirtualNode` 树中的声明顺序决定。
- v1 不实现焦点环（focus ring）的视觉渲染，仅保证逻辑焦点正确。

#### 5.4.4 输入路由约束

- 输入事件从 Platform Thread 到 MVU Core Thread 的传递**零分配**：原始事件数据为栈上 `ref struct`。
- 命中测试在 MVU Core Thread 上执行（基于最新的 `LayoutBox[]`），不在 Platform Thread 上执行。
- v1 不实现事件冒泡（bubbling）或捕获（capture）阶段；输入事件直接路由到命中目标对应的 `Message`。
- 滚动容器的输入处理：`ScrollContainer` 控件在 `Update` 中自行处理 `MouseWheel` 事件，更新滚动偏移状态。

### 5.5 布局层设计

> 本节补充 §5.2.7 中提到的 `Element Tree → Layout Tree → LayoutBox[]` 链路的具体设计。

#### 5.5.1 布局算法选型

v1 采用**简化版 Flexbox 子集**作为默认布局算法：

- 支持 `Row`（水平排列）和 `Column`（垂直排列）两种主轴方向。
- 支持 `Start` / `Center` / `End` / `Stretch` 四种交叉轴对齐。
- 支持固定尺寸（`px`）和自动撑满（`Fill`）两种尺寸模式。
- v1 **不做**：百分比尺寸、`Wrap`（换行）、`SpaceBetween` / `SpaceAround` 分布、嵌套滚动约束。

#### 5.5.2 布局流程

```text
VirtualNode Patch
  → Element Tree 更新（补全控件状态、继承属性）
  → Layout Tree 构建 / 增量更新
  → Measure Pass（自底向上计算固有尺寸）
  → Arrange Pass（自顶向下分配最终位置与尺寸）
  → LayoutBox[] 输出
```

#### 5.5.3 核心接口

```csharp
public interface ILayoutEngine
{
    // 全量布局（首次或根节点变更时）
    LayoutResult PerformLayout(LayoutElement root, PixelRectangle availableBounds);

    // 增量布局（局部 Patch 时，仅重新测量受影响的子树）
    LayoutResult PerformIncrementalLayout(
        LayoutElement root,
        ReadOnlySpan<int> dirtyNodeIds,
        PixelRectangle availableBounds);
}

public readonly record struct LayoutResult(
    FrozenDictionary<int, LayoutBox> Boxes,
    PixelRectangle TotalBounds);
```

#### 5.5.4 布局失效策略

- 当 `VirtualNodePatch` 类型为 `Update` 时，标记对应节点及其祖先为 `dirty`。
- `Measure Pass` 仅对 `dirty` 子树重新执行，其余子树复用上一帧的测量结果。
- 当 `Patch` 类型为 `Add` / `Remove` / `Move` 时，触发父节点的完整重新布局。
- v1 不做跨子树的布局缓存共享（即不支持"布局结果的序列化与反序列化"）。

#### 5.5.5 与 PoC 的衔接

当前 `LayoutTreeBuilder` 是 PoC 过渡实现，使用硬编码常量（如按钮高度、文本行高）。向正式布局层演进的路径：

1. 将硬编码常量替换为 `LayoutStyle` 属性（从 `VirtualNode` 属性中读取）。
2. 引入 `MeasurePass` / `ArrangePass` 分离，替代当前的单遍布局。
3. 引入 `dirty` 标记，支持增量布局。
4. 最终使 `LayoutTreeBuilder` 成为 `ILayoutEngine` 的默认实现。

### 5.6 可观测性与诊断

> v1 的可观测性以内置、零配置、低开销为原则，不做独立的监控服务。

#### 5.6.1 核心诊断指标

| 指标类别 | 具体指标 | 采集方式 | v1 目标 |
|---------|---------|---------|---------|
| **帧性能** | 帧时间 (ms)、FPS | `Stopwatch` 在 Compositor Thread 每帧采集 | 稳态 60 FPS（VSync 对齐） |
| **帧性能** | GC 分配 / 帧 | `GC.GetAllocatedBytesForCurrentThread()` 差值 | 热路径 0 分配 |
| **内存** | `MemoryPool` 使用量、峰值、归还率 | `MemoryPool` 封装计数器 | 无泄漏 |
| **内存** | GPU 显存占用 | D3D12 查询接口 | 待 D3D12 接入后定义 |
| **Diff** | Patch 数量 / 帧、ReplaceRoot 次数 | `VirtualNodeDiffer` 内部计数 | 观察 diff 质量 |
| **布局** | Measure / Arrange 耗时 | `Stopwatch` 在 Layout Engine 内采集 | < 1ms / 帧 |
| **输入** | 事件队列深度、丢弃计数 | MPSC Channel 封装计数器 | 无丢弃 |
| **错误** | 异常计数、设备丢失次数、帧跳过次数 | 各层 catch 块内计数 | 0 异常 |

#### 5.6.2 诊断暴露机制

- **Debug overlay**：v1 提供可选的帧内诊断叠加层（在渲染输出上叠加 FPS、GC、Patch 数等指标），通过编译开关 `IRIX_DIAGNOSTICS` 启用。
- **Structured logging**：使用 `Microsoft.Extensions.Logging` 的 `ILogger` 接口，关键路径使用 `LogTrace` / `LogDebug` 级别，生产构建默认关闭。
- **ETW / EventSource**（v1.1+）：为性能分析工具（如 PerfView、Windows Performance Recorder）提供结构化事件流。

#### 5.6.3 v1 不做的诊断能力

- 不做独立的诊断面板 UI（如 Flutter DevTools 级别的独立工具）。
- 不做 UI trace 录制与回放（v2.0 商业版能力）。
- 不做远程诊断采集。
- 不做自动性能回归检测（留给 CI/CD 管线）。

---

## 6. MVU 状态核心

### 6.1 核心接口定义

MVU Core 是一个**纯粹的 C# 库**，零依赖渲染、网络、平台 API，可完整运行在单元测试环境中。

```csharp
// 核心三元组：Model / Message / VirtualNode
public interface IApplication<TModel, TMessage>
    where TModel : notnull
    where TMessage : notnull
{
    TModel Initialize();
    (TModel NextModel, Command<TMessage>? Command) Update(TModel model, TMessage message);
    VirtualNodeTree BuildView(TModel model);
}

// 副作用指令（Command），保持 Update 纯函数
public abstract class Command<TMessage>
{
    public sealed class None : Command<TMessage> { }
    public sealed class Async(Func<CancellationToken, ValueTask<TMessage>> task) : Command<TMessage> { }
    public sealed class Batch(IReadOnlyList<Command<TMessage>> commands) : Command<TMessage> { }
}
```

### 6.2 VirtualNode 树与 Diff

```csharp
// v1 不强制整棵 VirtualNode 树都使用 ref struct，优先保证可实现性与可测试性
public readonly struct VirtualNode
{
    public readonly NodeType Type;
    public readonly ReadOnlyMemory<VirtualNodeAttribute> Attributes;
    public readonly ReadOnlyMemory<VirtualNode> Children;
    public readonly ulong Key;  // 用于 Diff keyed reconciliation
}

public readonly struct VirtualNodeAttribute
{
    public readonly AttributeKind Kind;
    public readonly AttributeValue Value;  // union-like blittable struct
}
```

v1 的零分配目标聚焦在 **Diff 输出、Patch 管线、布局热路径、渲染热路径**，而非强制要求整个声明式树结构绝对栈上化。`VirtualNode` 可采用轻量不可变结构配合池化 Builder / Arena 分配策略，在复杂度、可调试性和性能之间取得更稳妥的平衡。

**Diff 算法策略：**

- 递归深比较：逐节点比较 `Kind`、`Key`、`Content`、`Attributes`、`Children`，短路于首个差异。
- 当前实现：若两棵树等价，输出空 `PatchBatch`（`Count = 0`），下游可跳过渲染；若不等价，输出 `ReplaceRoot`。
- **已落地：** 递归节点等价判断（`NodesEqual`），正确处理 `default(VirtualNode)` 的空数组边界。
- **待落地：** 同层比较、Key 驱动的列表 Reconciliation（类 React keys）；局部 `Update` / `Add` / `Remove` / `Move` Patch 输出。
- Diff 计算优先在同步上下文中完成，输出的 `VirtualNodePatch` 数组从 `MemoryPool<VirtualNodePatch>` 租用，随后以 `IMemoryOwner` 形式转移给 Compositor；v1 不把"整棵 VirtualNode 树 0 分配"作为发布门槛。
- `PatchBatch` 已携带 `Root` 属性，消费者可直接使用 `patchBatch.Root` 获取新根节点，无需从 `Memory` 中反推。

### 6.3 调度器 (IMessageDispatcher)

```csharp
public interface IMessageDispatcher<TMessage>
{
    // 线程安全：任意线程均可调用
    void Dispatch(TMessage message);

    // 调度器内部维护消息队列，保证 Update 在单一线程上串行执行
}
```

---

## 7. 内存管理与数据管线

### 7.1 两种上下文，两套规则

| 上下文 | 发生场景 | 内存策略 |
|--------|---------|---------|
| **同步/栈上下文** | Layout Measure/Arrange、局部 Diff、绘制参数计算 | 优先使用 `ref struct` + `ReadOnlySpan<T>`；热路径禁止持续性托管堆分配 |
| **异步/跨界上下文** | 跨线程传递 Patch、跨网络传递 Patch | `MemoryPool<T>` 租用 + `IMemoryOwner<T>` 所有权转移 |

### 7.2 所有权转移模型

```
MVU Core Thread
  ├─ 从 MemoryPool<VirtualNodePatch> 租用内存块
  ├─ 写入 VirtualNodePatch 数据
  ├─ 封装为 VirtualNodePatchBatch { IMemoryOwner<VirtualNodePatch> Owner, ScreenId Target }
  └─ Enqueue 到对应 Compositor 的 MPSC Channel
                              ↓ (所有权转移，Core 侧不再持有引用)
Compositor Thread
  ├─ Dequeue VirtualNodePatchBatch
  ├─ 独占读取 Owner.Memory.Span
  ├─ 执行渲染
  └─ Owner.Dispose() → 内存归还 MemoryPool
```

> **规则**：`IMemoryOwner<T>` 在同一时刻只有一个合法持有者。Core 侧 Enqueue 之后视为已放弃所有权，不得再访问该内存区域。违反此规则会导致数据竞争，Analyzer Rule 在编译期强制检查。

### 7.3 FlatBuffers 跨网络序列化（v2.0）

> **版本边界说明：** 本节属于 `Remote UI Delivery` 演进方向，**不纳入 v1 GA 发布门槛**。

FlatBuffers 用于 `Remote UI Delivery` 模式下的网络传输层，而非本地进程内通信。

- FlatBuffers 的访问模式为偏移量寻址（offset-based），并非直接 blittable struct 映射。
- "零拷贝"的含义：服务端构建 FlatBuffer → 网络传输字节流 → 客户端直接从字节流中按偏移读取字段，无需反序列化到独立的堆对象。
- 对于需要直接 Blittable 映射的性能热点结构（如每帧顶点数据），使用 `MemoryMarshal.Cast<byte, TVertex>` 手动管理布局，并通过 `[StructLayout(LayoutKind.Sequential)]` 强制保证对齐，不经过 FlatBuffers。

### 7.4 错误处理与恢复策略

> v1 的错误处理以**"快速失败 + 可预测恢复"**为原则，不做复杂的容错重试链路。

#### 7.4.1 分层错误处理

| 层级 | 错误类型 | 处理策略 |
|------|---------|---------|
| **MVU Core** | `Update` 抛出异常 | 捕获异常，保持上一个有效 `Model` 不变；通过诊断通道报告错误；不崩溃进程 |
| **MVU Core** | `BuildView` 抛出异常 | 同上，保持上一个有效 VirtualNode 树 |
| **Diff / Patch** | Diff 计算失败 | 退化为 ReplaceRoot（整棵子树替换），确保渲染不中断 |
| **MPSC Channel** | `IMemoryOwner` 分配失败（OOM） | 抛出 `OutOfMemoryException`，上层捕获后暂停帧生产，等待内存释放 |
| **Compositor** | GPU 设备丢失 (`DXGI_ERROR_DEVICE_REMOVED`) | 尝试重建设备与 Swapchain；连续 3 次失败则上报致命错误并安全退出 |
| **Compositor** | 单帧渲染超时 | 跳过当前帧，记录诊断指标，继续下一帧 |
| **Platform** | 窗口创建失败 | 向应用层报告错误，由应用决定重试或退出 |
| **Platform** | 输入事件积压（队列满） | 丢弃最旧的输入事件，记录丢弃计数 |

#### 7.4.2 取消路径

- `Command<TMessage>.Async` 接受 `CancellationToken`，当应用关闭时通过 `CancellationTokenSource.Cancel()` 通知所有挂起的异步命令。
- `CompositorThread` 通过 `CancellationToken` 控制渲染循环退出，退出时保证所有已分配的 `IMemoryOwner` 被正确释放。
- `IMemoryOwner<T>` 的释放必须在 `finally` 块中执行，确保异常路径下不泄漏池化内存。

#### 7.4.3 诊断断言

v1 在 Debug 构建中启用以下诊断断言（`Debug.Assert`）：

- `IMemoryOwner<T>` 的持有者必须是当前线程（防止跨线程误用）。
- `DrawCommand` 序列中 `PushClipRect` / `PopClip` 和 `PushTransform` / `PopTransform` 必须配对。
- `LayoutBox` 的 `ClipBounds` 必须完全包含在父节点的 `ClipBounds` 内。
- `VirtualNodePatch` 的目标 `NodeId` 必须在当前树中存在（`Remove` / `Move` 操作）。

---

## 8. 运行模式与 UI 交付层

> **版本边界说明：** v1 只交付 `8.1 本地模式`。`8.2 Local UI Remoting` 建议作为 v1.x 的高优先级演进项；`8.3 Remote UI Delivery` 作为商业化与跨机器交付能力，不作为首版里程碑阻塞项。

### 8.1 本地模式 (Thick Client)

```
┌─────────────────────────────────────────────────┐
│                  单一本地进程                     │
│                                                  │
│  MVU Core Thread ──MPSC──► Compositor Thread(s) │
│                  纳秒级 IMemoryOwner 传递         │
└─────────────────────────────────────────────────┘
```

适用场景：普通桌面应用、离线应用、低延迟要求的工具软件。

### 8.2 Local UI Remoting（Loopback-Only，本机多进程扩展）

`Local UI Remoting` 不是传统意义上的“远程 UI”，而是**本机 loopback-only UI 协议**。它的目标是让插件、扩展模块、脚本宿主或隔离子进程，在**不进入主进程**的前提下，把 UI 以原生渲染方式投递到宿主窗口中。

```text
┌──────────────┐      loopback RPC / local stream      ┌──────────────────┐
│  Host App    │ ◄────────────────────────────────────► │ Plugin Process    │
│              │                                        │                  │
│ MVU Runtime  │  InputEvent / Command / Ack            │ Plugin Logic      │
│ Layout+Draw  │ ◄────────────────────────────────────  │ VirtualNode View  │
│ Compositor   │  VirtualNodePatch / DrawCommand        │ or MVU Adapter    │
└──────────────┘                                        └──────────────────┘
```

#### 8.2.1 目标场景

- 支持插件/扩展市场的桌面应用
- 需要多进程隔离的 IDE、工具链、工业控制台
- 宿主希望统一窗口、输入、主题与渲染，但不信任第三方插件代码
- 希望在不开 WebView 的前提下，为进程外模块提供原生 UI 接入能力

#### 8.2.2 核心约束

- **仅允许 loopback / 本机 IPC**，不提供跨机器连接能力。
- 插件进程**不拥有窗口与渲染设备**，只拥有 UI 描述权与业务逻辑。
- 宿主进程保留最终输入路由、布局、绘制与资源配额控制权。
- 协议层尽量与 `Remote UI Delivery` 共用消息模型，但认证、网络容错与租户能力不在此层实现。

#### 8.2.3 为什么它有独立产品价值

`Local UI Remoting` 本身就可以作为一个免费/开源许可能力成立，因为它解决的是很多桌面宿主长期存在的痛点：

- 插件导致主进程崩溃或内存泄漏
- WebView 插件体验割裂
- 进程内 SDK 难做权限隔离
- 扩展系统难统一主题、布局、输入和无障碍能力

它提供的是一种新的宿主模型：

- 插件在独立进程中运行
- 宿主统一接管窗口、输入、绘制
- 插件只通过 loopback 协议提交 UI 和处理消息

#### 8.2.4 开源版边界

建议将 `Local UI Remoting` 作为**免费 / 开源许可版**能力，边界如下：

- 允许 loopback-only 连接
- 允许本机插件 / 扩展 / 脚本宿主使用
- 不提供跨机器连接
- 不提供企业级身份认证、租户隔离、审计、远程发布与会话管理

这样既能形成开发者生态入口，也不会直接吞掉商业版价值。

### 8.3 Remote UI Delivery（跨机器 / 商业版）

```
┌──────────────┐     gRPC Bi-directional Stream      ┌──────────────────┐
│   客户端进程  │ ◄──────────────────────────────────► │   服务端进程      │
│              │  InputEvent (FlatBuffers, Seq ID ↑)  │                  │
│  Local State │ ─────────────────────────────────►  │  F# / C# MVU Core│
│  (视觉状态)   │ ◄─────────────────────────────────  │  Business Logic  │
│  Compositor  │ VirtualNodePatch (FlatBuffers, Seq ID ↓) │ (权威状态) │
└──────────────┘                                     └──────────────────┘
```

`Remote UI Delivery` 才是完整意义上的 server-driven / thin-client 交付能力。它面向的是跨机器部署、集中式业务逻辑、终端零升级、企业可控交付。

#### 8.3.1 双态状态路由

| 状态类型 | 归属 | 示例 |
|---------|------|------|
| **纯视觉状态** | 客户端本地计算 | 悬停高亮、滚动偏移、输入框光标位置、动画进度 |
| **业务混合状态** | 服务端权威 | 表单校验结果、数据列表、权限控制、业务计算 |

#### 8.3.2 乐观更新与冲突解决

**流程：**

1. 用户输入 → 客户端立即基于**乐观状态**更新本地视觉（如输入框字符回显），同时生成 `InputEvent { SeqId: N, Payload: ... }` 发送服务端。
2. 服务端处理后返回 `VirtualNodePatch { AckSeqId: N, ... }`。
3. 客户端收到 Ack 后：
   - **SeqId 连续匹配**：确认乐观更新，无需操作。
   - **服务端下发修正 Patch**（如校验不通过）：客户端执行状态回滚（Rollback），将乐观状态替换为服务端权威状态，触发视觉更新。
4. **超时/网络断开**：本地纯视觉状态继续工作，业务状态冻结并显示"离线"指示器，重连后发送挂起事件队列。

```csharp
// 客户端乐观状态管理器（伪代码）
internal class OptimisticStateManager
{
    private readonly Queue<(uint SeqId, TModel OptimisticSnapshot)> _pendingAcks = new();

    public TModel ApplyOptimistic(TModel current, TMessage message, uint seqId)
    {
        var next = _mvuCore.Update(current, message).NextModel;
        _pendingAcks.Enqueue((seqId, next));
        return next;
    }

    public TModel Reconcile(TModel authoritative, uint ackedSeqId)
    {
        // 清除已确认的乐观快照
        while (_pendingAcks.TryPeek(out var head) && head.SeqId <= ackedSeqId)
            _pendingAcks.Dequeue();

        // 若服务端下发了修正，以服务端状态为准，丢弃所有待确认乐观状态
        if (_pendingAcks.Count > 0 && /* 服务端有实质修正 */ true)
        {
            _pendingAcks.Clear();
            return authoritative;
        }
        return _pendingAcks.Count > 0 ? _pendingAcks.Peek().OptimisticSnapshot : authoritative;
    }
}
```

#### 8.3.3 商业版边界

建议将 `Remote UI Delivery` 作为**商业版 / 企业版**能力，其商业价值不应只建立在“解除 loopback-only 限制”这一项上，而应打包为完整可运营能力：

- 跨机器连接与远程状态权威
- 身份认证、授权与证书管理
- 审计日志与会话追踪
- 灰度发布、远程回滚与版本控制
- 资源配额、租户隔离与连接治理
- 诊断面板、UI trace、崩溃回放

#### 8.3.4 协议兼容策略

`Local UI Remoting` 与 `Remote UI Delivery` 应尽量共享以下协议语义：

- `InputEvent`
- `VirtualNodePatch`
- `DrawCommandBatch`（如后续网络侧也需要）
- `Ack / SeqId`
- 乐观更新与回滚模型

二者的主要差异应体现在**传输边界与运营能力**，而不是 UI 协议本身完全分叉。这样才能避免免费版与商业版各自演化成两套不兼容系统。

---

## 9. 生态兼容层

> **版本边界说明：** 本章能力**不进入 v1 GA 范围**。首版优先保证原生 C# MVU 体验与本地渲染内核稳定，兼容层在核心模型稳定后再补齐。

### 9.1 C# MVVM 桥接 (Source Generator)

Irix 的 `MVVM Bridge` 不应复制 `WPF / WinUI / MAUI` 上那套完整而沉重的运行时对象模型。它的职责应收敛为一种**面向 MVU 的轻量 authoring layer**：让习惯 MVVM 的开发者能以熟悉写法接入，但最终仍落到 Irix 唯一的 `Model / Message / Update / View` 主链。

#### 9.1.1 定位

- `MVVM Bridge` 只是迁移入口与语法糖，不是第二套 UI runtime。
- 运行时只保留一份权威状态：MVU `Model`。不再维护独立的、可长期漂移的 `ViewModel State`。
- `ViewModel`、Binding、`IXAML` 语法的价值在于提升 authoring 体验，而不是引入新的对象层级。

#### 9.1.2 Binding 收敛策略

Binding 只负责“读当前状态”和“把用户输入转换为 Message”。bridge 的最小能力建议收敛为：

- `OneWay`
- `TwoWay`
- `Command`
- 少量可在编译期解析的 typed converter（如后续确有必要）

其中 `TwoWay Binding` 的本质不是运行时同步两套对象图，而是由生成器自动把写入动作转换为 `dispatch`：

```ixaml
<TextBox Text="{Bind Name, Mode=TwoWay}" />
<Button Command="{Bind Save}" />
```

对应生成代码应接近：

```csharp
builder.TextBox(
    text: model.Name,
    onTextChanged: value => dispatch(new SetNameMessage(value)));

builder.Button(
    onClick: () => dispatch(new SaveMessage()));
```

也就是说，所谓“双向绑定”只是**内部自动管理 MVU 的 update/state glue code**，而不是重新引入一个通用运行时 Binding Engine。

#### 9.1.3 XAML / IXAML 的角色

`XAML / IXAML` 在 Irix 中应仅作为 DSL 存在：

- 仅在编译期由 Source Generator 解析
- 生成强类型 C# 视图构建代码
- 生成结果直接接入 `BuildView` / `Dispatch` / `VirtualNode` 主链
- 编译期暴露 binding path、类型不匹配、命令签名不匹配等错误

明确不做：

- 运行时 `XAML / IXAML` 解析
- 运行时对象图装配
- 反射式 Markup Extension 系统
- 依赖运行时 `DataContext` 猜测的弱类型 binding

#### 9.1.4 VisualTree / VisualState 收敛策略

Irix 不应复制 WPF 式完整 `VisualTree`、`DependencyObject`、`VisualStateManager` 体系。bridge 生成的视图最终仍应下沉到 Irix 自己的稳定主链：

```text
IXAML / ViewModel
  → Source Generator
  → Typed C# View Builder
  → VirtualNode / Element / Layout / DrawCommand
```

因此：

- `VisualTree` 只保留 Irix 自己的 `VirtualNode` / Element / Layout 层次，不额外引入第二棵 UI 树
- `VisualState` 应优先编译为条件属性、条件样式或条件分支，而不是独立运行时状态机
- 模板、资源、样式若存在，也应优先做编译期展开或受限模型，而不是完整复制 WPF 的动态系统

#### 9.1.5 明确非目标

为避免 bridge 失控，下面这些能力不应作为首批设计目标：

- 不做 `DependencyProperty` / `DependencyObject` 兼容层
- 不做通用运行时 Binding Path 解释器
- 不做完整 Routed Event 系统
- 不做通用 Attached Property 运行时
- 不做完整 Trigger / Resource Dictionary / Template 运行时
- 不做“为了兼容旧 XAML 生态”而复制 `WPF / WinUI / MAUI` 的历史包袱

#### 9.1.6 开发者写法与生成目标（概念示意）

```csharp
[ViewModelBridge]
public partial class CounterViewModel
{
    [Observable] private int _count;

    [Command]
    public void Increment() => Count++;
}

// Source Generator 的生成目标（概念示意）
// → 将属性读取映射为对 Model 的只读投影
// → 将属性写入映射为类型安全的 Message
// → 将 Command 调用映射为 Dispatch
// → 将 IXAML DSL 映射为普通 C# 视图构建代码
```

这样既能保留传统 .NET 开发者熟悉的 authoring 体验，也能保持 Irix 在 AOT、可测试性、零运行时反射和架构边界上的优势。

### 9.2 F# MVU 互操作层

提供极薄的 F# 接口，允许开发者用 F# 的 Discriminated Unions 和模式匹配编写业务逻辑，编译产物为标准 .NET Assembly，与 C# 层无缝对接。

```fsharp
// F# 侧定义
type Message =
    | Increment
    | Decrement
    | Reset of int

type Model = { Count: int }

let update (model: Model) (message: Message) =
    match message with
    | Increment -> { model with Count = model.Count + 1 }, Command.None
    | Decrement -> { model with Count = model.Count - 1 }, Command.None
    | Reset n   -> { model with Count = n }, Command.None
```

---

## 10. 技术栈清单

| 层级 | 模块 | 核心技术选型 | 交付阶段 | 说明 |
|------|------|------------|---------|------|
| **图形 API** | 底层硬件接口 | `D3D12` | **v1** | Windows-only PoC 的唯一图形后端，先把主链打通再谈第二后端 |
| **Drawing 抽象** | 中间绘制层 | `DrawCommand` + `IDrawingBackend` | **v1** | 上层 UI 不直接耦合第三方绘图库，为后续替换后端或自研引擎留边界 |
| **矢量绘图实现** | 2D 绘图后端 | `SkiaSharp`（作为适配器） | **v1** | 先复用成熟文本/路径能力，但限制在 backend adapter 边界内 |
| **平台窗口** | 系统 API | Win32 P/Invoke (`user32.dll`) | **v1** | 先完成主 `HWND` 路径，`Ghost Window` 与多子视口分阶段增强 |
| **核心逻辑** | 状态引擎 | C# 14 | **v1** | `MemoryPool`、同步热路径优化、Native AOT 友好设计 |
| **本地 IPC** | 线程间通信 | `System.Threading.Channels` (MPSC) | **v1** | `IMemoryOwner` 所有权转移通道 |
| **测试** | 单元/集成测试 | xUnit + FsCheck (Property-based) | **v1** | MVU Core 纯函数，天然适合属性测试 |
| **编译器扩展** | 代码生成 | Roslyn Source Generators | v2.0 | 用于轻量 MVVM authoring bridge、`IXAML` DSL 编译期降解与 AOT 元数据生成 |
| **本机 UI 传输** | Loopback RPC / IPC | 命名管道 / Unix Domain Socket / loopback gRPC（待定） | v1.x | 用于 `Local UI Remoting` 插件/扩展宿主 |
| **跨网络序列化** | 网络协议 | FlatBuffers (`.fbs` Schema) | v2.0 | `Remote UI Delivery` 增量 `Patch` 协议 |
| **网络传输** | 长连接 | `Grpc.Net.Client` (Bi-directional) | v2.0 | `Remote UI Delivery` 底层通信 |

---

## 11. 已知技术风险清单

> ⭐ 按严重程度排序，🔴 高风险项均为 v1 PoC 阶段优先验证目标。

| 编号 | 风险描述 | 严重程度 | 缓解策略 |
|------|---------|---------|---------|
| R-01 | `D3D12` 主链在资源生命周期、设备恢复或文本渲染上稳定性低于预期 | 🔴 高 | 先锁定单窗口、单主视口、单后端；以文本、矩形、裁剪、资源释放为首批验收项 |
| R-02 | `Skia` 与 `D3D12` 集成稳定性低于预期 | 🔴 高 | 严格限制上层只依赖 `DrawCommand` 与 backend adapter，必要时保留替换绘制实现的空间 |
| R-03 | 过早深绑 `Skia` API，导致后续替换 backend 或自研 drawing engine 成本过高 | 🔴 高 | 在 v1 先建立 `DrawCommand` + `IDrawingBackend` 边界，禁止上层直接依赖 `Skia` 对象模型 |
| R-04 | `VirtualNode` 全量 `ref struct` 化导致实现与调试复杂度过高 | 🟠 中高 | v1 改为"热路径零分配优先"，不把整棵声明树栈上化作为硬要求 |
| R-05 | Win32 强绑定导致跨平台迁移成本高 | 🟠 中高 | `IPlatformHost` 接口 Day 1 设计；Win32 实现完整封装在 `Platform.Windows` 项目 |
| R-06 | v1 范围膨胀导致 PoC 成功但产品不可交付 | 🟠 中高 | 将 `Local UI Remoting`、`Remote UI Delivery`、`MVVM Bridge`、`F#`、自研 Drawing Engine 下放到后续阶段，v1 只保留本地主路径 |
| R-07 | `IMemoryOwner<T>` 所有权转移在异常/取消路径下被破坏 | 🟡 中 | 在 MPSC 管线引入专项单元测试、压力测试与 Analyzer 辅助检查 |
| R-08 | 多显示器动态插拔（热插拔）的 `HWND` 生命周期管理 | 🟡 中 | 作为 v1.1 专项能力建设，先覆盖单窗口单主视口 |
| R-09 | 过早启动自研 Drawing Engine，吞噬核心 UI 框架建设节奏 | 🟡 中 | 先用 `SkiaBackend` 跑通 MVP，再以基线数据决定是否自研 |
| R-10 | 开源版与商业版的 UI 协议分叉，导致生态与商业化相互掣肘 | 🟡 中 | `Local UI Remoting` 与 `Remote UI Delivery` 共享核心 UI 协议，仅在传输边界和运营能力上区分 |
| R-11 | 为兼容 MVVM / XAML 过度引入 `WPF / WinUI / MAUI` 级运行时对象模型，导致 AOT、热路径和架构边界失守 | 🟡 中 | 明确 bridge 仅为编译期 DSL + Binding 代码生成，不引入运行时 parser、反射式 Binding Engine、`DependencyProperty` 体系 |

---

## 12. 分阶段交付计划

v1.0 以**本地模式可用、单图形后端稳定、最小 MVU/Compositor 主路径可交付**为完成标准。多屏增强、UI 交付层与生态兼容层不作为首版阻塞项。

> **Phase 与版本号映射关系：** 本文档中 Phase 编号与 §3 版本边界速查的版本号对应关系如下：

| Phase | 对应版本 | 核心交付物 | 预估周期 |
|-------|---------|-----------|---------|
| Phase 1 | v1.0（基础） | D3D12 渲染 PoC + Drawing 抽象 + 最小 MVU | 6 周 |
| Phase 2 | v1.0（MVP） | 本地模式可用：控件、输入、MPSC 管线 | 8 周 |
| Phase 3 | v1.0 GA（稳定化） | 多屏受限支持 + 动画 + 基线监测 | 6 周 |
| Phase 4 | v1.1 ~ v1.x | Local UI Remoting（loopback-only 插件宿主） | 待定 |
| Phase 5 | v2.0 | Remote UI Delivery + MVVM Bridge + F# + 跨平台 | 待定 |

### Phase 1：单后端单窗口渲染 PoC（目标：6 周）

**目标：** 在 Windows 上打通 `D3D12` 渲染主链，并建立独立于第三方库的 Drawing 抽象边界。

- [ ] 搭建 `D3D12` 基础渲染循环（三角形上屏）
- [ ] 定义 `DrawCommand` / `IDrawingBackend` 最小抽象
- [ ] 集成 `SkiaSharp` 作为 `D3D12` 路径下的首个 backend adapter，渲染基础矩形、路径、文本
- [ ] 实现主 `HWND` + 单 `CompositorThread` 基础渲染闭环
- [ ] 实现最小化 MVU Core（`Model/Update/View` 三元组）
- [ ] 实现 `VirtualNodePatch -> Retained UI Tree -> DrawCommandBatch + HitTestTarget` 单消费者渲染链路
- [ ] **验收指标：** 1080p 下文本 + 矩形界面稳定运行 30min；帧循环稳态无持续性 GC 分配；无设备丢失或未释放资源；上层无 `Skia` 直接依赖

### Phase 2：本地模式 MVP（目标：8 周）

**目标：** 把 PoC 收敛成可用的本地桌面最小产品，而不是直接跳到网络层。

- [ ] 实现 `IMessageDispatcher<TMessage>` 串行调度与基础命令模型
- [ ] 实现最小控件集合：`Text`、`Rectangle`、`Button`、`ScrollContainer`
- [ ] 实现输入路由、焦点切换、基础鼠标/键盘事件模型
- [ ] 完整实现 `MPSC + IMemoryOwner<T>` 本地所有权转移管线
- [ ] 建立文本缓存、画刷缓存、局部重绘和命令录制的基础监测
- [ ] 为 MVU Core、Diff、Patch Routing 建立单元测试与属性测试
- [ ] **验收指标：** 本地模式示例应用可连续运行 8h；输入、布局、绘制无死锁；热路径 GC 分配可测且稳定

### Phase 3：v1.0 稳定化与受控多屏支持（目标：6 周）

**目标：** 在本地主路径稳定后，再引入受控范围内的多屏与动画能力。

- [ ] 实现 `ScreenTopologyManager` 的受限版本（单屏 / 双屏固定拓扑）
- [ ] 实现时间轴插值动画系统
- [ ] 评估是否引入 `Ghost Event Window`，若收益不足则保留为实验特性
- [ ] 补齐资源释放、异常恢复、取消路径与压力测试
- [ ] 建立显存占用、帧时间、GC、线程竞争的基线监测
- [ ] 评估 `SkiaBackend` 是否已经成为性能或资源模型瓶颈，决定是否立项 `Irix.Drawing`
- [ ] **验收指标：** 双屏固定拓扑下动画无明显撕裂；无跨线程资源竞争告警；稳态帧循环 GC 分配为 0 或保持严格上界

### Phase 4：v1.x Local UI Remoting（开源/免费许可方向）

**目标：** 让本机插件/扩展进程能够通过 loopback 协议接入宿主原生 UI，同时保持主进程对窗口、输入和绘制的控制权。

- [ ] 定义 `Local UI Remoting` 协议：`InputEvent`、`VirtualNodePatch`、`Ack / SeqId`
- [ ] 选定本机传输方案（命名管道 / loopback gRPC）
- [ ] 实现插件进程生命周期、握手与能力协商
- [ ] 实现宿主侧插件区域挂载、输入转发与资源配额控制
- [ ] 建立 loopback-only 许可边界与宿主安全模型
- [ ] **验收指标：** 插件进程崩溃不影响宿主主窗口；插件 UI 可被宿主统一布局与绘制；协议与本地渲染主链共用核心 UI 模型

### Phase 5：v2.0 Remote UI Delivery 与商业化能力

**目标：** 在不阻塞首版交付的前提下，逐步扩展架构愿景。

- [ ] 更完整的多屏热插拔、跨刷新率、跨色彩空间支持
- [ ] `Remote UI Delivery`：FlatBuffers Schema、gRPC 双向流、乐观更新与回滚
- [ ] 轻量 `MVVM → MVU` Source Generator 桥接（`IXAML` 仅作为 DSL，无运行时解析）
- [ ] F# 一等互操作层
- [ ] 第二图形后端
- [ ] `Irix.Drawing` 自研 Drawing Engine（仅在基线数据证明必要时）
- [ ] 商业版能力：认证、审计、租户隔离、远程发布、诊断与回放
- [ ] Native AOT 完整兼容矩阵与发布链路

---

## 13. 附录：竞品对比

| 特性 | Irix | Avalonia | WPF | MAUI | Blazor Desktop |
|------|-------|---------|------|------|---------------|
| 渲染后端 | D3D12 (v1) / Vulkan (vNext) + Skia Adapter | Skia (单线程) | 平台原生 (DirectX) | 平台原生 | WebView2 |
| 多屏异构渲染 | ✅ 原生支持 | ⚠️ 有限 | ⚠️ 有限 | ⚠️ 依赖平台 | ❌ 不支持 |
| 每帧 GC 分配 | ✅ 零分配热路径 | ⚠️ 有分配 | ⚠️ 有分配 | ⚠️ 有分配 | ❌ 较多 |
| UI 交付层（Local/Remote） | ✅ 一等公民 | ❌ | ❌ | ❌ | ✅（偏 Web 模式） |
| Native AOT | ✅ 设计目标 | ⚠️ 实验性 | ❌ 不支持 | ⚠️ 部分支持 | ❌ |
| F# 支持 | ✅ 原生互操作 | ⚠️ 可用但非一等 | ⚠️ 可用 | ⚠️ 可用 | ⚠️ 可用 |
| 跨平台 | ⚠️ v2.0 目标 | ✅ | ❌ Windows-only | ✅ | ✅ |
| 成熟度 | 🚧 早期设计 | ✅ 生产可用 | ✅ 生产可用 | ✅ 生产可用 | ✅ 生产可用 |

---

*本文档为活文档，随设计迭代持续更新。欢迎在 GitHub Issues 提出技术疑问或设计建议。*

---

## 附录 A：术语表

| 术语 | 全称 / 说明 | 首次出现 |
|------|------------|---------|
| **MVU** | Model-View-Update，Elm 架构模式的 C# 移植 | §6 |
| **VirtualNode** | 声明式 UI 树的轻量不可变节点，类比 React 的 VDOM | §6.2 |
| **VirtualNodePatch** | Diff 输出的操作描述（Add / Remove / Update / Move） | §6.2 |
| **PatchBatch** | 一组 `VirtualNodePatch` 的所有权包装，通过 MPSC 传递 | §7.2 |
| **DrawCommand** | Irix 自有的稳定绘制命令，隔离上层 UI 与底层 backend | §5.2.4 |
| **DrawCommandBatch** | 一组 `DrawCommand` 的内存包装 | — |
| **RenderFrameBatch** | 单帧完整渲染数据：`DrawCommandBatch` + `HitTestTarget[]` | §5.2.8 |
| **HitTestTarget** | 命中测试目标，与 `DrawCommand` 并行传递 | §5.4.2 |
| **LayoutBox** | 布局输出：节点的最终几何、裁剪区域、ZIndex | §5.2.6 |
| **IDrawingBackend** | 绘制后端抽象接口 | §5.2.4 |
| **IPlatformHost** | 平台窗口抽象接口 | §5.1.3 |
| **ICompositor** | 合成引擎抽象接口 | §5.2 |
| **CompositorThread** | 执行渲染循环的专用线程 | §4.3 |
| **MPSC** | Multi-Producer Single-Consumer，多生产者单消费者队列 | §7.2 |
| **IMemoryOwner\<T\>** | `System.Buffers` 的内存所有权句柄，保证单持有者 | §7.2 |
| **Ghost Event Window** | 不可见的输入接收窗口，隔离输入与渲染线程 | §5.1.1 |
| **ScreenTopologyManager** | 管理多屏子视口生命周期的组件 | §5.1.2 |
| **Local UI Remoting** | 本机 loopback-only 的插件 UI 接入协议 | §8.2 |
| **Remote UI Delivery** | 跨机器 server-driven UI 交付能力（商业版） | §8.3 |
| **MVVM Bridge** | 轻量编译期 MVVM authoring layer，非运行时 | §9.1 |
| **IXAML** | Irix 的声明式 UI DSL，仅编译期解析 | §9.1.3 |
| **D3D12** | Direct3D 12，v1 唯一图形后端 | §5.2.2 |
| **CsWin32** | Win32 API 的 C# 源码生成器 | 项目约定 |

---

## 附录 B：架构决策记录索引 (ADR)

> 以下为当前已确认的关键架构决策。每条决策记录了决策内容、上下文与权衡。详细论述见正文对应章节。

| ADR 编号 | 决策 | 章节 | 状态 | 简要理由 |
|---------|------|------|------|---------|
| ADR-001 | D3D12 作为 v1 唯一图形后端 | §5.2.2 | ✅ 已确认 | Windows-only 阶段贴近 DXGI/PIX 工具链，避免双线调试 |
| ADR-002 | DrawCommand 作为 Skia 隔离层 | §5.2.4 | ✅ 已确认 | 防止上层深绑 Skia 对象模型，保留 backend 替换空间 |
| ADR-003 | IMemoryOwner 所有权转移模型 | §7.2 | ✅ 已确认 | 保证跨线程零拷贝传递的内存安全 |
| ADR-004 | HitTestTarget 与 DrawCommand 并行传递 | §5.4.2 | ✅ 已确认 | 交互语义不污染绘制边界 |
| ADR-005 | 单线程 Update 串行执行 | §4.3 / §6.3 | ✅ 已确认 | 避免 MVU 状态的并发复杂度 |
| ADR-006 | Skia 仅作为 backend adapter，非架构中心 | §5.2.5 | ✅ 已确认 | 为未来自研 Drawing Engine 保留空间 |
| ADR-007 | MVVM Bridge 为编译期前端，非第二套 runtime | §9.1 | ✅ 已确认 | AOT 友好，避免反射式 Binding Engine |
| ADR-008 | Local UI Remoting 为免费/开源方向 | §8.2.4 | ✅ 已确认 | 形成开发者生态入口，不吞掉商业版价值 |
| ADR-009 | v1 不做运行时 XAML/IXAML 解析 | §9.1.3 | ✅ 已确认 | Source Generator 编译期降解，零运行时开销 |
| ADR-010 | VirtualNode 采用轻量不可变结构，非全量 ref struct | §6.2 | ✅ 已确认 | 平衡可实现性、可调试性与热路径性能 |
| ADR-011 | DrawCommand 不内联文本，通过 ResourceHandle + TextRunEntry 并行传递 | §5.2.6 | ✅ 已确认 | DrawCommand 保持纯值类型，可序列化/可记录/可回放 |
| ADR-012 | PatchBatch 携带 Root 属性，消费者直接使用 | §7.2 | ✅ 已确认 | 消除从 Memory 反推根节点的 hack，为未来增量 patch 铺路 |
| ADR-013 | D3D12 互操作使用 CsWin32 生成的裸指针 COM 包装 | §5.2.2 | ✅ 已确认 | 消除手写 vtable 偏移和 GUID 错误；`allowMarshaling: false` 保持 AOT 兼容 |

---

## 附录 C：非目标清单与边界声明

> 本附录汇总 v1 明确不做但容易被误认为"应该有"的能力，避免范围膨胀。

### C.1 无障碍 (Accessibility)

| 能力 | v1 状态 | 说明 |
|------|---------|------|
| UIA（UI Automation）Provider | ❌ 不做 | v1 不暴露 UIA 接口，屏幕阅读器无法识别 Irix 控件 |
| 键盘导航（完整 Tab 链） | ⚠️ 最小集 | 仅支持基础 Tab / Shift+Tab，不支持方向键导航网格 |
| 高对比度模式 | ❌ 不做 | v1 使用固定色彩方案 |
| 屏幕阅读器语义标注 | ❌ 不做 | 需要 UIA Provider 支持后才有意义 |

> **v1.1+ 演进方向：** 在 `Element Tree` 中预留 `AutomationProperties` 属性槽位，为后续 UIA 集成做准备。

### C.2 主题与样式系统

| 能力 | v1 状态 | 说明 |
|------|---------|------|
| 深色/浅色主题切换 | ❌ 不做 | v1 使用单一色彩方案，硬编码在应用层 |
| 样式继承 / 资源字典 | ❌ 不做 | 不引入 `ResourceDictionary` / `Style` 运行时 |
| 色彩空间管理 | ⚠️ 最小集 | `DrawColor` 使用 sRGB ARGB 字节，不做 linear / HDR 转换 |
| DPI 缩放策略 | ⚠️ 最小集 | `IScreenInfo.DpiScale` 提供 DPI 信息，布局层按比例缩放 |

> **v1 约束：** 控件外观通过 `DrawCommand` 的 `Color` / `Rect` 等字段直接指定，不经过主题引擎。应用层可自行实现简单的"当前主题"状态字段，通过 MVU `Model` 驱动。

### C.3 动画系统边界

| 能力 | v1 状态 | 说明 |
|------|---------|------|
| 时间轴插值动画 | ✅ Phase 3 | 基于高精度时间戳的 `lerp` + `easing` |
| 状态机动画（VisualStateManager） | ❌ 不做 | 不引入独立动画状态机 |
| 物理弹簧 / 惯性动画 | ❌ 不做 | v1 仅支持时间驱动的缓动函数 |
| 手势识别（滑动、捏合） | ❌ 不做 | v1 仅处理原始鼠标/键盘事件 |

### C.4 渲染能力边界

| 能力 | v1 状态 | 说明 |
|------|---------|------|
| 圆角矩形 | ❌ 不做 | v1 仅支持直角矩形 |
| 阴影 / 模糊效果 | ❌ 不做 | 需要后端 shader 支持 |
| 透明度混合 | ⚠️ 最小集 | `DrawColor.A` 字段支持，但不做层级 alpha 合成优化 |
| 文本选择 / 编辑 | ❌ 不做 | v1 的 `Text` 控件为只读显示 |
| 图片渲染 | ⚠️ 延后 | `DrawCommand.DrawImage` 已定义但 v1 不要求实现 |
