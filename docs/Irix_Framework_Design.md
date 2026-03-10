# Irix — 高性能原生 .NET UI 框架 架构设计书

> 📌 **文档说明：** 本文档在原版基础上重新整理，增加了版本边界速查、风险热力图，并将散落在各章节的版本约束统一归拢，以便快速定位 v1 范围与后续演进计划。

---

## 目录

1. [背景与动机](#1-背景与动机)
2. [设计原则](#2-设计原则)
3. [版本边界速查](#3-版本边界速查) ⭐ 新增
4. [整体架构概览](#4-整体架构概览)
5. [渲染引擎架构](#5-渲染引擎架构)
6. [MVU 状态核心](#6-mvu-状态核心)
7. [内存管理与数据管线](#7-内存管理与数据管线)
8. [运行模式与网络层](#8-运行模式与网络层)
9. [生态兼容层](#9-生态兼容层)
10. [技术栈清单](#10-技术栈清单)
11. [已知技术风险清单](#11-已知技术风险清单)
12. [分阶段交付计划](#12-分阶段交付计划)
13. [附录：竞品对比](#13-附录竞品对比)

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
3. **Server-Driven UI** 薄客户端模式，支持业务逻辑集中部署、客户端零升级

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
| 单图形后端（Vulkan） | `Silk.NET.Vulkan` + `SkiaSharp` GPU 后端 |
| Win32 主 HWND 窗口 | 单窗口、单主视口优先 |
| MPSC + `IMemoryOwner<T>` 管线 | 线程间所有权转移通道 |
| 最小控件集合 | Text、Rectangle、Button、ScrollContainer |
| 基础输入路由 | 鼠标 / 键盘，焦点切换 |
| MVU Diff / Patch 管线 | VNode Patch 输出与单消费者渲染链路 |
| 基础单元 / 属性测试 | xUnit + FsCheck |

### v1.1 范围（在 v1 主路径稳定后交付）

| 能力 | 说明 |
|------|------|
| 受限多屏异构渲染 | 覆盖单屏 / 双屏固定拓扑两类场景 |
| `ScreenTopologyManager` 受限版本 | 监听 `WM_DISPLAYCHANGE`，管理子视口生命周期 |
| 时间轴插值动画系统 | 基于高精度时间戳的跨屏同步动画 |
| `Ghost Event Window`（评估引入） | 独立输入接收窗口，收益不足则保留为实验特性 |

### v2.0 范围（后续独立规划）

| 能力 | 说明 |
|------|------|
| Server-Driven UI | FlatBuffers Schema + gRPC 双向流 + 乐观更新与回滚 |
| MVVM → MVU Source Generator 桥接 | 零运行时反射，编译期转换 |
| F# 一等互操作层 | Discriminated Unions + 模式匹配 |
| DX12 图形后端 | Windows 特化优化 |
| Native AOT 完整兼容矩阵 | 发布链路验证 |
| 跨平台支持（macOS / Linux） | IPlatformHost 替换实现 |
| 完整多屏热插拔 | 跨 GPU、跨色彩空间组合 |

---

## 4. 整体架构概览

下图为**终局目标蓝图**，v1 只实现 `C# MVU → MVU Core → 本地 MPSC → Vulkan Compositor → Win32 Platform Host` 这一条主路径。

```
┌─────────────────────────────────────────────────────────────────┐
│                        应用层 (App Layer)                        │
│           C# MVU  /  F# MVU  /  C# MVVM (via Bridge)           │
└───────────────────────────┬─────────────────────────────────────┘
                            │ Msg / Model
┌───────────────────────────▼─────────────────────────────────────┐
│                    MVU Core Engine (纯 C#)                       │
│   Model ──Update──► Model'   │   Model ──View──► VNode Tree     │
│        (ref struct Patch)    │        (Diff → VNode Patch List) │
└──────────┬────────────────────────────────────┬─────────────────┘
           │ MPSC Channel                        │ MPSC Channel
           │ (IMemoryOwner<byte> Move)            │ (IMemoryOwner<byte> Move)
┌──────────▼──────────┐              ┌───────────▼─────────────┐
│  Compositor Thread  │              │   Compositor Thread      │
│  Screen A (144Hz)   │              │   Screen B (60Hz)        │
│  DX12 Swapchain     │              │   Vulkan Swapchain       │
│  Skia GrContext     │              │   Skia GrContext         │
└──────────▲──────────┘              └───────────▲─────────────┘
           │                                     │
┌──────────┴─────────────────────────────────────┴─────────────┐
│                平台抽象层 (IPlatformHost)                       │
│   Ghost Event Window │ HWND子视口管理 │ 高精度Timer           │
└───────────────────────────────────────────────────────────────┘
           │ [可选]  Server-Driven 模式
┌──────────▼───────────────────────────────────────────────────┐
│               网络层 (gRPC Bi-directional Stream)             │
│         FlatBuffers VNode Patch  ◄──►  F# Server Core        │
└──────────────────────────────────────────────────────────────┘
```

### 4.1 关键数据流（本地模式，v1）

```
用户输入事件
  → Ghost Window (Win32 WndProc)
  → MPSC 事件队列 (原始事件, 零分配)
  → MVU Core: dispatch Msg
  → Update(Model, Msg) → Model'
  → View(Model') → VNode Patch List
  → 按屏幕归属路由 → 各 Compositor MPSC 队列 (所有权转移)
  → Compositor Thread: 执行 Draw Call
  → GPU Present (与屏幕 VSync 对齐)
```

---

## 5. 渲染引擎架构

### 5.1 平台窗口层

#### 5.1.1 幽灵事件窗口 (Ghost Event Window)

- 使用 `CreateWindowEx` 创建一个**不可见、透明、无边框**的顶层窗口。
- **唯一职责**：接收 OS 输入事件（WM_MOUSEMOVE、WM_KEYDOWN、WM_TOUCH 等），将原始 `HWND/WPARAM/LPARAM` 封装为 `RawInputEvent`（`readonly ref struct`）压入 MPSC 通道。
- 不参与任何渲染，彻底避免"输入处理"与"渲染循环"的线程竞争。

> **v1 收敛策略：** 首个公开版本允许先由主 `HWND` 承担输入接收职责，只保留 `Ghost Event Window` 的抽象接口与实验实现。待本地渲染主路径稳定后再完整迁移，避免 PoC 阶段同时处理窗口拓扑和输入重路由两类复杂性。

#### 5.1.2 独立子视口 (Per-Screen HWND)

- 当检测到应用窗口跨越多个显示器时，在后台静默创建对应数量的**无边框子 HWND**，精准覆盖各屏幕物理区域。
- 每个子 HWND 对应一个 `ICompositor` 实例，持有独立的 Swapchain。
- 子视口的创建和销毁对上层 MVU 逻辑透明，由 `ScreenTopologyManager` 负责监听 `WM_DISPLAYCHANGE` 并自动管理生命周期。

> **v1 收敛策略：** v1 先覆盖`单屏`与`双屏固定拓扑`两类高价值场景；更复杂的热插拔、跨 GPU、跨色彩空间组合留到 v1.1 以后逐步放开。

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

每个屏幕对应一个独立的 `CompositorThread`，拥有完整的渲染资源所有权。

#### 5.2.1 合成引擎生命周期

```
CompositorThread.Start()
  ├─ 初始化图形 API 设备 (v1: Vulkan；vNext: DX12)
  ├─ 创建 Swapchain (匹配当前屏幕刷新率)
  ├─ 初始化 Skia GrContext (绑定到当前 GPU Context)
  └─ 进入渲染循环:
       while (!cancellation.IsCancellationRequested)
         ├─ 等待 VSync 信号 (或高精度 Timer)
         ├─ 消费 MPSC 队列中的 VNode Patch
         ├─ 执行布局 (Measure/Arrange) — ref struct 栈上计算
         ├─ 执行绘制 (Skia DrawCall)
         ├─ Submit CommandBuffer / Present Swapchain
         └─ 归还 IMemoryOwner 到 MemoryPool
```

#### 5.2.2 图形 API 选型

| API | 交付阶段 | 说明 |
|-----|---------|------|
| **Vulkan** | **v1 唯一图形后端** | 优先打通 `SkiaSharp` GPU 集成与单后端渲染链路，减少调试矩阵 |
| **Direct3D 12** | vNext Windows 优化路径 | 在 v1 主链稳定后再引入，用于 Windows 特化优化与工具链增强 |

> ⚠️ v1 只维护一套底层图形路径，直接使用 `Silk.NET.Vulkan` 绑定，**不使用** `Silk.NET.Windowing` 高级封装。

#### 5.2.3 Skia 与 Vulkan 集成约束（关键风险点）

Skia GPU 后端集成是本架构**工程复杂度最高的单点**，需严格遵循：

1. 每个 `CompositorThread` 持有**独立的** `GRContext`，禁止跨线程共享。
2. Skia Surface 从 Swapchain BackBuffer 创建，生命周期由 Compositor 完整管理。
3. 渲染完成后，必须在 `GRContext.Flush()` 后再执行 `Present`，确保 GPU 指令顺序正确。
4. PoC 阶段须优先验证：多 `GRContext` 同时运行时的显存占用是否在可接受范围。

```csharp
// 伪代码示意
internal sealed class VulkanCompositor : ICompositor
{
    private readonly GRContext _grContext;
    private readonly VkSwapchain _swapchain;

    public void RenderFrame(ReadOnlySpan<VNodePatch> patches)
    {
        var backBuffer = _swapchain.AcquireNextImage();
        using var surface = SKSurface.Create(_grContext, backBuffer.ToGRBackendRenderTarget());
        var canvas = surface.Canvas;

        foreach (ref readonly var patch in patches)
            DrawPatch(canvas, in patch);   // ref struct, 栈上计算

        _grContext.Flush();
        _swapchain.Present(backBuffer);
    }
}
```

### 5.3 时间轴动画系统

- 动画系统**不依赖帧序号**，仅依赖全局高精度物理时间戳（`Stopwatch.GetTimestamp()`，精度 < 1µs）。
- 每帧插值公式：`value = lerp(from, to, easing((now - startTime) / duration))`
- 跨屏幕的同一动画元素，由各自 Compositor 独立插值。由于时间基准相同，视觉上完全同步，彻底消除因刷新率不同导致的"状态撕裂"。

---

## 6. MVU 状态核心

### 6.1 核心接口定义

MVU Core 是一个**纯粹的 C# 库**，零依赖渲染、网络、平台 API，可完整运行在单元测试环境中。

```csharp
// 核心三元组：Model / Msg / VNode
public interface IMvuApp<TModel, TMsg>
    where TModel : notnull
    where TMsg : notnull
{
    TModel Init();
    (TModel NextModel, Cmd<TMsg>? Command) Update(TModel model, TMsg msg);
    VNodeTree View(TModel model);
}

// 副作用指令（Command），保持 Update 纯函数
public abstract class Cmd<TMsg>
{
    public sealed class None : Cmd<TMsg> { }
    public sealed class OfAsync(Func<CancellationToken, ValueTask<TMsg>> task) : Cmd<TMsg> { }
    public sealed class Batch(IReadOnlyList<Cmd<TMsg>> cmds) : Cmd<TMsg> { }
}
```

### 6.2 VNode 树与 Diff

```csharp
// v1 不强制整棵 VNode 树都使用 ref struct，优先保证可实现性与可测试性
public readonly struct VNode
{
    public readonly NodeType Type;
    public readonly ReadOnlyMemory<VNodeAttribute> Attributes;
    public readonly ReadOnlyMemory<VNode> Children;
    public readonly ulong Key;  // 用于 Diff keyed reconciliation
}

public readonly struct VNodeAttribute
{
    public readonly AttributeKind Kind;
    public readonly AttributeValue Value;  // union-like blittable struct
}
```

v1 的零分配目标聚焦在 **Diff 输出、Patch 管线、布局热路径、渲染热路径**，而非强制要求整个声明式树结构绝对栈上化。`VNode` 可采用轻量不可变结构配合池化 Builder / Arena 分配策略，在复杂度、可调试性和性能之间取得更稳妥的平衡。

**Diff 算法策略：**

- 同层比较，Key 驱动的列表 Reconciliation（类 React keys）。
- Diff 输出为 `VNodePatch[]`，每个 Patch 描述"对哪个节点，做什么操作（Add/Remove/Update/Move）"。
- Diff 计算优先在同步上下文中完成，输出的 `VNodePatch` 数组从 `MemoryPool<VNodePatch>` 租用，随后以 `IMemoryOwner` 形式转移给 Compositor；v1 不把"整棵 VNode 树 0 分配"作为发布门槛。

### 6.3 调度器 (IMsgDispatcher)

```csharp
public interface IMsgDispatcher<TMsg>
{
    // 线程安全：任意线程均可调用
    void Dispatch(TMsg msg);

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
  ├─ 从 MemoryPool<VNodePatch> 租用内存块
  ├─ 写入 VNodePatch 数据
  ├─ 封装为 PatchMessage { IMemoryOwner<VNodePatch> Owner, ScreenId Target }
  └─ Enqueue 到对应 Compositor 的 MPSC Channel
                              ↓ (所有权转移，Core 侧不再持有引用)
Compositor Thread
  ├─ Dequeue PatchMessage
  ├─ 独占读取 Owner.Memory.Span
  ├─ 执行渲染
  └─ Owner.Dispose() → 内存归还 MemoryPool
```

> **规则**：`IMemoryOwner<T>` 在同一时刻只有一个合法持有者。Core 侧 Enqueue 之后视为已放弃所有权，不得再访问该内存区域。违反此规则会导致数据竞争，Analyzer Rule 在编译期强制检查。

### 7.3 FlatBuffers 跨网络序列化（v2.0）

> **版本边界说明：** 本节属于 `Server-Driven` 演进方向，**不纳入 v1 GA 发布门槛**。

FlatBuffers 用于 Server-Driven 模式下的网络传输层，而非本地进程内通信。

- FlatBuffers 的访问模式为偏移量寻址（offset-based），并非直接 blittable struct 映射。
- "零拷贝"的含义：服务端构建 FlatBuffer → 网络传输字节流 → 客户端直接从字节流中按偏移读取字段，无需反序列化到独立的堆对象。
- 对于需要直接 Blittable 映射的性能热点结构（如每帧顶点数据），使用 `MemoryMarshal.Cast<byte, TVertex>` 手动管理布局，并通过 `[StructLayout(LayoutKind.Sequential)]` 强制保证对齐，不经过 FlatBuffers。

---

## 8. 运行模式与网络层

> **v1 范围说明：** v1 只交付 `8.1 本地模式`。`8.2 Server-Driven 模式` 保留为架构前瞻设计，不作为首版里程碑阻塞项。

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

### 8.2 Server-Driven 模式 (Thin Client，v2.0)

```
┌──────────────┐     gRPC Bi-directional Stream      ┌──────────────────┐
│   客户端进程  │ ◄──────────────────────────────────► │   服务端进程      │
│              │  InputEvent (FlatBuffers, Seq ID ↑)  │                  │
│  Local State │ ─────────────────────────────────►  │  F# / C# MVU Core│
│  (视觉状态)   │ ◄─────────────────────────────────  │  Business Logic  │
│  Compositor  │  VNodePatch (FlatBuffers, Seq ID ↓) │  (权威状态)       │
└──────────────┘                                     └──────────────────┘
```

#### 8.2.1 双态状态路由

| 状态类型 | 归属 | 示例 |
|---------|------|------|
| **纯视觉状态** | 客户端本地计算 | 悬停高亮、滚动偏移、输入框光标位置、动画进度 |
| **业务混合状态** | 服务端权威 | 表单校验结果、数据列表、权限控制、业务计算 |

#### 8.2.2 乐观更新与冲突解决

**流程：**

1. 用户输入 → 客户端立即基于**乐观状态**更新本地视觉（如输入框字符回显），同时生成 `InputEvent { SeqId: N, Payload: ... }` 发送服务端。
2. 服务端处理后返回 `VNodePatch { AckSeqId: N, ... }`。
3. 客户端收到 Ack 后：
   - **SeqId 连续匹配**：确认乐观更新，无需操作。
   - **服务端下发修正 Patch**（如校验不通过）：客户端执行状态回滚（Rollback），将乐观状态替换为服务端权威状态，触发视觉更新。
4. **超时/网络断开**：本地纯视觉状态继续工作，业务状态冻结并显示"离线"指示器，重连后发送挂起事件队列。

```csharp
// 客户端乐观状态管理器（伪代码）
internal class OptimisticStateManager
{
    private readonly Queue<(uint SeqId, TModel OptimisticSnapshot)> _pendingAcks = new();

    public TModel ApplyOptimistic(TModel current, TMsg msg, uint seqId)
    {
        var next = _mvuCore.Update(current, msg).NextModel;
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

---

## 9. 生态兼容层

> **版本边界说明：** 本章能力**不进入 v1 GA 范围**。首版优先保证原生 C# MVU 体验与本地渲染内核稳定，兼容层在核心模型稳定后再补齐。

### 9.1 C# MVVM 桥接 (Source Generator)

允许传统 .NET 开发者以熟悉的 MVVM 模式接入框架，Source Generator 在编译期完成转换，零运行时反射。

```csharp
// 开发者写法（传统 MVVM）
[MvuViewModel]
public partial class CounterViewModel
{
    [Observable] private int _count;

    [Command]
    public void Increment() => Count++;
}

// Source Generator 自动生成（概念示意）
// → 将 PropertyChanged 映射为 Msg { PropertyName, NewValue }
// → 将 ICommand.Execute 映射为 Msg { CommandName, Parameter }
// → 接入底层 MVU Dispatch 管线
```

### 9.2 F# MVU 互操作层

提供极薄的 F# 接口，允许开发者用 F# 的 Discriminated Unions 和模式匹配编写业务逻辑，编译产物为标准 .NET Assembly，与 C# 层无缝对接。

```fsharp
// F# 侧定义
type Msg =
    | Increment
    | Decrement
    | Reset of int

type Model = { Count: int }

let update (model: Model) (msg: Msg) =
    match msg with
    | Increment -> { model with Count = model.Count + 1 }, Cmd.None
    | Decrement -> { model with Count = model.Count - 1 }, Cmd.None
    | Reset n   -> { model with Count = n }, Cmd.None
```

---

## 10. 技术栈清单

| 层级 | 模块 | 核心技术选型 | 交付阶段 | 说明 |
|------|------|------------|---------|------|
| **图形 API** | 底层硬件接口 | `Silk.NET.Vulkan` | **v1** | v1 只维护单一后端，降低驱动与调试矩阵复杂度 |
| **矢量绘图** | 2D 渲染引擎 | `SkiaSharp` (GPU 后端) | **v1** | 路径、文本、抗锯齿；v1 先验证单 `GRContext` / 单主视口主链 |
| **平台窗口** | 系统 API | Win32 P/Invoke (`user32.dll`) | **v1** | 先完成主 `HWND` 路径，`Ghost Window` 与多子视口分阶段增强 |
| **核心逻辑** | 状态引擎 | C# 14 | **v1** | `MemoryPool`、同步热路径优化、Native AOT 友好设计 |
| **本地 IPC** | 线程间通信 | `System.Threading.Channels` (MPSC) | **v1** | `IMemoryOwner` 所有权转移通道 |
| **测试** | 单元/集成测试 | xUnit + FsCheck (Property-based) | **v1** | MVU Core 纯函数，天然适合属性测试 |
| **编译器扩展** | 代码生成 | Roslyn Source Generators | v2.0 | 用于 MVVM 桥接、AOT 元数据生成 |
| **跨网络序列化** | 网络协议 | FlatBuffers (`.fbs` Schema) | v2.0 | `Server-Driven` 模式增量 `Patch` 协议 |
| **网络传输** | 长连接 | `Grpc.Net.Client` (Bi-directional) | v2.0 | `Server-Driven UI` 底层通信 |

---

## 11. 已知技术风险清单

> ⭐ 按严重程度排序，🔴 高风险项均为 v1 PoC 阶段优先验证目标。

| 编号 | 风险描述 | 严重程度 | 缓解策略 |
|------|---------|---------|---------|
| R-01 | `Skia` GPU 后端与 `Vulkan` 集成稳定性低于预期 | 🔴 高 | **Phase 1 PoC 最优先验证**；先锁定单窗口、单主视口、单后端，再决定是否扩展到多屏 |
| R-02 | 多线程/多屏 `GRContext` 显存开销超预期 | 🔴 高 | 不作为 v1 GA 阻塞项；v1 先完成单主视口，v1.1 再验证多屏独立合成 |
| R-03 | `VNode` 全量 `ref struct` 化导致实现与调试复杂度过高 | 🟠 中高 | v1 改为"热路径零分配优先"，不把整棵声明树栈上化作为硬要求 |
| R-04 | Win32 强绑定导致跨平台迁移成本高 | 🟠 中高 | `IPlatformHost` 接口 Day 1 设计；Win32 实现完整封装在 `Platform.Windows` 项目 |
| R-05 | v1 范围膨胀导致 PoC 成功但产品不可交付 | 🟠 中高 | 将 `Server-Driven`、`MVVM Bridge`、`F#` 下放到 v2.0，v1 只保留本地主路径 |
| R-06 | `IMemoryOwner<T>` 所有权转移在异常/取消路径下被破坏 | 🟡 中 | 在 MPSC 管线引入专项单元测试、压力测试与 Analyzer 辅助检查 |
| R-07 | `Server-Driven` 网络栈与 `Native AOT` 组合成熟度不足 | 🟡 中 | 移出 v1 主线；待本地模式稳定后单独建立兼容性矩阵 |
| R-08 | 多显示器动态插拔（热插拔）的 `HWND` 生命周期管理 | 🟡 中 | 作为 v1.1 专项能力建设，先覆盖单屏与双屏固定拓扑 |

---

## 12. 分阶段交付计划

v1.0 以**本地模式可用、单图形后端稳定、最小 MVU/Compositor 主路径可交付**为完成标准。多屏增强、Server-Driven 与生态兼容层不作为首版阻塞项。

### Phase 1：单后端单窗口渲染 PoC（目标：6 周）

**目标：** 验证 `Vulkan + SkiaSharp` 主链可行性，尽快暴露 R-01 风险。

- [ ] 搭建 `Silk.NET.Vulkan` 基础渲染循环（三角形上屏）
- [ ] 集成 `SkiaSharp` Vulkan 后端，渲染基础矩形、路径、文本
- [ ] 实现主 `HWND` + 单 `CompositorThread` 基础渲染闭环
- [ ] 实现最小化 MVU Core（`Model/Update/View` 三元组）
- [ ] 实现 `VNodePatch` 输出与单消费者渲染链路
- [ ] **验收指标：** 1080p 下文本 + 矩形界面稳定运行 30min；帧循环稳态无持续性 GC 分配；无设备丢失或未释放资源

### Phase 2：本地模式 MVP（目标：8 周）

**目标：** 把 PoC 收敛成可用的本地桌面最小产品，而不是直接跳到网络层。

- [ ] 实现 `IMsgDispatcher<TMsg>` 串行调度与基础命令模型
- [ ] 实现最小控件集合：`Text`、`Rectangle`、`Button`、`ScrollContainer`
- [ ] 实现输入路由、焦点切换、基础鼠标/键盘事件模型
- [ ] 完整实现 `MPSC + IMemoryOwner<T>` 本地所有权转移管线
- [ ] 为 MVU Core、Diff、Patch Routing 建立单元测试与属性测试
- [ ] **验收指标：** 本地模式示例应用可连续运行 8h；输入、布局、绘制无死锁；热路径 GC 分配可测且稳定

### Phase 3：v1.0 稳定化与受控多屏支持（目标：6 周）

**目标：** 在本地主路径稳定后，再引入受控范围内的多屏与动画能力。

- [ ] 实现 `ScreenTopologyManager` 的受限版本（单屏 / 双屏固定拓扑）
- [ ] 实现时间轴插值动画系统
- [ ] 评估是否引入 `Ghost Event Window`，若收益不足则保留为实验特性
- [ ] 补齐资源释放、异常恢复、取消路径与压力测试
- [ ] 建立显存占用、帧时间、GC、线程竞争的基线监测
- [ ] **验收指标：** 双屏固定拓扑下动画无明显撕裂；无跨线程资源竞争告警；稳态帧循环 GC 分配为 0 或保持严格上界

### Phase 4：v1.1 / v2.0 演进项（后续独立规划）

**目标：** 在不阻塞首版交付的前提下，逐步扩展架构愿景。

- [ ] 更完整的多屏热插拔、跨刷新率、跨色彩空间支持
- [ ] `Server-Driven UI`：FlatBuffers Schema、gRPC 双向流、乐观更新与回滚
- [ ] `MVVM → MVU` Source Generator 桥接
- [ ] F# 一等互操作层
- [ ] Native AOT 完整兼容矩阵与发布链路

---

## 13. 附录：竞品对比

| 特性 | Irix | Avalonia| Avalonia | MAUI | Blazor Desktop |
|------|-------|---------|------|---------------|
| 渲染后端 | Vulkan/DX12 + Skia | Skia (单线程) | 平台原生 | WebView2 |
| 多屏异构渲染 | ✅ 原生支持 | ⚠️ 有限 | ⚠️ 依赖平台 | ❌ 不支持 |
| 每帧 GC 分配 | ✅ 零分配热路径 | ⚠️ 有分配 | ⚠️ 有分配 | ❌ 较多 |
| Server-Driven UI | ✅ 一等公民 | ❌ | ❌ | ✅（Blazor Server） |
| Native AOT | ✅ 设计目标 | ⚠️ 实验性 | ⚠️ 部分支持 | ❌ |
| F# 支持 | ✅ 原生互操作 | ⚠️ 可用但非一等 | ⚠️ 可用 | ⚠️ 可用 |
| 跨平台 | ⚠️ v2.0 目标 | ✅ | ✅ | ✅ |
| 成熟度 | 🚧 早期设计 | ✅ 生产可用 | ✅ 生产可用 | ✅ 生产可用 |

---

*本文档为活文档，随设计迭代持续更新。欢迎在 GitHub Issues 提出技术疑问或设计建议。*
