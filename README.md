# Irix

Irix 是一个面向高性能桌面场景的原生 .NET UI 框架原型项目。

当前阶段的重点不是“做一个功能齐全的新 UI 框架”，而是先验证一条清晰主链：

- Windows-only PoC
- `D3D12` 作为唯一图形后端
- `DrawCommand + IDrawingBackend` 作为内部 Drawing 抽象
- `Skia` 仅作为 backend adapter 候选，而不是上层架构中心
- 后续扩展到 `Local UI Remoting` 和 `Remote UI Delivery`

## 当前状态

仓库目前已经有：

- 最小 MVU runtime
- Windows 平台宿主 PoC
- Counter 示例应用
- `VirtualNodePatch` 到 PoC Window 可视化的闭环
- 基础 runtime 测试

仓库目前还没有：

- `D3D12` 渲染主链
- `Skia + D3D12` backend adapter
- 正式的 `DrawCommand` / `IDrawingBackend` 代码骨架
- 真正的 retained tree / layout tree / draw pipeline
- 完整 diff、输入路由、布局和资源生命周期测试

如果你刚接手这个仓库，请把它视为**早期架构原型**，不是功能完成度很高的框架。

## 文档入口

- 设计主文档：[docs/Irix_Framework_Design.md](/d:/source/Irix/docs/Irix_Framework_Design.md)
- 项目进度与待办：[docs/Project_Status_and_Todo.md](/d:/source/Irix/docs/Project_Status_and_Todo.md)

建议阅读顺序：

1. 先看设计主文档，理解当前架构方向和版本边界
2. 再看项目进度文档，确认哪些已经落地、哪些还只是设计
3. 最后再读代码

## 解决方案结构

```text
src/
├─ Irix.Core/              # MVU Core、VirtualNode 模型、Diff/Patch、运行时
├─ Irix.Drawing/           # Drawing 抽象：DrawCommand、FrameContext、IDrawingBackend
├─ Irix.Rendering/         # Compositor 抽象、Patch 消费循环、backend 编排层
├─ Irix.Platform/          # 平台无关抽象
├─ Irix.Platform.Windows/  # Windows 宿主 PoC
└─ Irix.Poc/               # 最小示例应用（Counter）

tests/
└─ Irix.Core.Tests/        # 当前仅有基础 runtime 测试
```

## 当前架构方向

### 渲染 / Drawing

当前已确认：

- v1 / Windows-only PoC 只做 `D3D12`
- `Vulkan` 后移到后续阶段
- 上层不直接依赖 `Skia`
- Drawing 层目标是：

```text
VirtualNodePatch
  -> Retained UI Tree
  -> Layout Tree
  -> DrawCommandBatch
  -> IDrawingBackend
```

当前 PoC 中的 [WindowVisualCompositor.cs](/d:/source/Irix/src/Irix.Poc/WindowVisualCompositor.cs) 是过渡实现，它同时承担了控件语义解释、简单布局和 PoC 可视化，不应视为最终架构。

### UI 交付层

Irix 当前把这部分拆成两条产品线：

- `Local UI Remoting`
  - loopback-only
  - 面向插件/扩展宿主
  - 适合免费 / 开源许可方向
- `Remote UI Delivery`
  - 跨机器
  - 面向商业版 / 企业版
  - 用于远程 server-driven UI 交付

## 如何运行

当前最直接的方式是运行 PoC：

```powershell
dotnet run --project .\src\Irix.Poc\Irix.Poc.csproj
```

PoC 会启动一个简单的 Counter 窗口。当前支持的交互方式见 [Program.cs](/d:/source/Irix/src/Irix.Poc/Program.cs)：

- 点击按钮
- `Up` / `Down`
- 鼠标滚轮
- `R` 重置

运行测试：

```powershell
dotnet test .\Irix.slnx
```

## 近期最重要的工作

当前最值得优先推进的是：

1. 在 `Irix.Drawing` 中稳定 `DrawCommand` / `IDrawingBackend` / `DrawCommandBatch`
2. 把 `WindowVisualCompositor` 改成消费 `DrawCommandBatch`
3. 抽出 `LayoutTreeBuilder`
4. 开始搭 `D3D12` 最小渲染循环

如果要开始写代码，推荐从 [docs/Project_Status_and_Todo.md](/d:/source/Irix/docs/Project_Status_and_Todo.md) 的待办清单继续往下推进。

## 注意事项

- Win32 interop 优先使用 `CsWin32`
- 命名尽量统一成 C# 风格，不保留临时缩写
- 不要把 `Skia` API 直接泄漏到 Core / Layout / 上层 UI
- 不要优先做 `Vulkan`、跨平台、完整多屏热插拔或自研 Drawing Engine
