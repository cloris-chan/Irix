using Irix;
using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Contains("--diagnose"))
        {
            RunDiagnosticMode();
            return;
        }

        if (args.Contains("--diagnose-resize"))
        {
            RunResizeDiagnosticMode();
            return;
        }

        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));
        window.ExternalRenderingEnabled = true;
        window.Show();

        // D3D12 rendering path
        var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);

        var drawCommandTranslator = new WindowDrawCommandTranslator(
            window,
            () => _ = d3d12Renderer.ApplyPendingResize(),
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
            });
        using var d3d12Compositor = new DrawingBackendCompositor(d3d12Backend);
        var compositor = args.Contains("--console")
            ? new CompositeCompositor(new ConsoleCompositor(Console.Out), d3d12Compositor)
            : (ICompositor)d3d12Compositor;
        await using var compositorLoop = new CompositorLoop(drawCommandTranslator, compositor);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);

        window.SizeChanged += (w, h) =>
        {
            d3d12Renderer.Resize(w, h);
            _ = compositorLoop.RequestRenderAsync();
        };
        using var inputSubscription = platformHost.RawInputEvents.Subscribe(new PlatformInputObserver(HandleInput));

        platformHost.TopologyChanged += OnTopologyChanged;

        Console.WriteLine($"Detected screens: {platformHost.Screens.Count}");
        Console.WriteLine("Rendering: D3D12 (clear color from FillRect)");
        Console.WriteLine("Controls: Click buttons, Up/Down = +/-1, R = reset, Mouse wheel = +/-1.");

        await runtime.StartAsync();

        window.RunMessageLoop();

        Console.WriteLine($"Final count: {runtime.CurrentModel.Count}");

        void HandleInput(RawInputEvent inputEvent)
        {
            if (CounterInputRouter.TryMapInput(inputEvent, TryGetActionIdAt, out var message))
            {
                runtime.Dispatch(message);
                // After wheel input, if animating, start the tick loop
                if (message is CounterMessage.Wheel && runtime.CurrentModel.Scroll.IsAnimating)
                {
                    _ = StartTickLoop(runtime, compositorLoop);
                }
            }
        }

        string? TryGetActionIdAt(int x, int y)
        {
            return d3d12Compositor.TryGetActionIdAt(x, y, out var actionId) ? actionId : null;
        }

        void OnTopologyChanged(object? sender, ScreenTopologyChangedEventArgs args)
        {
            Console.WriteLine($"Topology changed. Screen count: {args.Screens.Count}");
        }

        platformHost.TopologyChanged -= OnTopologyChanged;
    }
    private static ScreenRegion CreatePrimaryWindowRegion(IScreenInfo screen)
    {
        const int windowWidth = 960;
        const int windowHeight = 540;
        var bounds = screen.PhysicalBounds;
        var x = bounds.X + Math.Max((bounds.Width - windowWidth) / 2, 0);
        var y = bounds.Y + Math.Max((bounds.Height - windowHeight) / 2, 0);
        return new ScreenRegion(screen.Id, new PixelRectangle(x, y, windowWidth, windowHeight));
    }

    private sealed class PlatformInputObserver(Action<RawInputEvent> onNext) : IObserver<RawInputEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(RawInputEvent value)
        {
            onNext(value);
        }
    }

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(16); // ~60fps

    /// <summary>
    /// Dispatches Tick messages at ~60fps while the scroll animation is active.
    /// Stops automatically when IsAnimating becomes false.
    /// </summary>
    private static async Task StartTickLoop(
        Runtime<CounterModel, CounterMessage> runtime,
        CompositorLoop compositorLoop)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lastTick = stopwatch.Elapsed;

        while (!runtime.CurrentModel.Scroll.IsAnimating)
        {
            await Task.Delay(TickInterval);
        }

        while (runtime.CurrentModel.Scroll.IsAnimating)
        {
            await Task.Delay(TickInterval);
            var now = stopwatch.Elapsed;
            var dt = (float)(now - lastTick).TotalSeconds;
            lastTick = now;

            runtime.Dispatch(new CounterMessage.Tick(dt));
            await compositorLoop.RequestRenderAsync();
        }
    }

    /// <summary>
    /// Diagnostic smoke mode: renders one frame with test rectangles and text,
    /// dumps cache stats, and exits. Use: --diagnose
    /// </summary>
    private static void RunDiagnosticMode()
    {
        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);

        // Build a test frame: one rectangle + one text + one button
        var resources = FrameDrawingResources.Rent();
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        var bgText = resources.AddText("Diagnostic Mode: Hello World 🎯");
        var btnText = resources.AddText("Click Me");
        resources.Seal();

        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect,
                Rect: new DrawRect(0, 0, 960, 540),
                Color: DrawColor.Opaque(32, 32, 32)),
            new(DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 120, 200, 40),
                Color: DrawColor.Opaque(52, 120, 246)),
            new(DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32),
                Resource: textStyle,
                Text: bgText,
                Color: DrawColor.Opaque(255, 255, 255)),
            new(DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 120, 200, 40),
                Resource: textStyle,
                Text: btnText,
                Color: DrawColor.Opaque(255, 255, 255))
        };

        // Render 3 frames via compositor to warm caches and collect stats
        for (var i = 0; i < 3; i++)
        {
            var batch = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(commands), commands.Length),
                [],
                resources,
                i == 0 ? [] : [(0, commands.Length)]); // frame 0: full; frames 1-2: dirty ranges

            compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
            batch.Dispose();
        }

        // Dump diagnostics
        var diag = d3d12Renderer.GetTextDiagnostics();
        if (diag.HasValue)
        {
            var d = diag.Value;
            Console.WriteLine("=== D3D12 Text Renderer Diagnostics ===");
            Console.WriteLine($"Format cache: {d.CachedFormats} entries, {d.FormatHits} hits, {d.FormatMisses} misses, {d.FormatEvictions} evictions");
            Console.WriteLine($"Layout cache: {d.CachedLayouts} entries, {d.LayoutHits} hits, {d.LayoutMisses} misses, {d.LayoutEvictions} evictions");
            Console.WriteLine($"Format hit rate: {(d.FormatHits + d.FormatMisses > 0 ? 100.0 * d.FormatHits / (d.FormatHits + d.FormatMisses) : 0):F1}%");
            Console.WriteLine($"Layout hit rate: {(d.LayoutHits + d.LayoutMisses > 0 ? 100.0 * d.LayoutHits / (d.LayoutHits + d.LayoutMisses) : 0):F1}%");
        }

        Console.WriteLine($"Device removed: {d3d12Renderer.IsDeviceRemoved}");
        Console.WriteLine($"Device error reason: {d3d12Renderer.DeviceErrorReason ?? "(none)"}");
        Console.WriteLine($"Swapchain size: {d3d12Renderer.Width}x{d3d12Renderer.Height}");
        Console.WriteLine($"=== Compositor Diagnostics ===");
        Console.WriteLine($"Render count: {compositor.RenderCount}");
        Console.WriteLine($"Partial apply: {compositor.PartialApplyCount}");
        Console.WriteLine($"Full apply: {compositor.FullApplyCount}");
        Console.WriteLine($"Empty frames: {compositor.EmptyFrameCount}");
        Console.WriteLine($"Partial hit rate: {(compositor.RenderCount > 0 ? 100.0 * compositor.PartialApplyCount / compositor.RenderCount : 0):F1}%");
        var dirty = compositor.LastDirtyCommandRanges;
        Console.WriteLine($"Compositor dirty ranges: {dirty.Count} ranges");
        foreach (var (start, count) in dirty)
        {
            Console.WriteLine($"  [{start}..{start + count - 1}] ({count} commands)");
        }
        var backendDirty = d3d12Backend.LastDirtyCommandRanges;
        Console.WriteLine($"Backend dirty ranges: {backendDirty.Count} ranges");
        foreach (var (start, count) in backendDirty)
        {
            Console.WriteLine($"  [{start}..{start + count - 1}] ({count} commands)");
        }
        var rangesMatch = dirty.Count == backendDirty.Count &&
            dirty.Zip(backendDirty).All(p => p.First == p.Second);
        Console.WriteLine($"Dirty ranges aligned: {rangesMatch}");
        Console.WriteLine($"Clipped commands: {d3d12Backend.ClippedCommandCount}");

        // Layout-driven frame: render through VirtualNode → Layout → Pipeline → Compositor
        // to verify the clip chain produces clipped commands
        Console.WriteLine($"=== Layout Pipeline Diagnostics ===");
        var layoutPipeline = new RenderPipeline();
        var layoutRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Layout Pipeline Test", 2),
            VirtualNodeFactory.Button("LayoutBtn", 3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("LayoutBtn"))));
        var layoutViewport = new PixelRectangle(0, 0, d3d12Renderer.Width, d3d12Renderer.Height);
        using var layoutBatch = layoutPipeline.Build(layoutRoot, layoutViewport);

        // Render the layout-driven frame through the compositor
        compositor.RenderAsync(layoutBatch).AsTask().GetAwaiter().GetResult();

        var layoutClipCount = 0;
        for (var j = 0; j < layoutBatch.Commands.Count; j++)
        {
            var cmd = layoutBatch.Commands.Memory.Span[j];
            if (cmd.ClipBounds.Width > 0 && cmd.ClipBounds.Height > 0)
            {
                layoutClipCount++;
            }
        }
        Console.WriteLine($"Layout commands: {layoutBatch.Commands.Count}");
        Console.WriteLine($"Layout clipped commands: {layoutClipCount}");
        Console.WriteLine($"Layout hit targets: {layoutBatch.HitTargets.Count}");
        if (layoutBatch.HitTargets.Count > 0)
        {
            var ht = layoutBatch.HitTargets[0];
            Console.WriteLine($"  Hit target: {ht.ActionId} bounds=({ht.Bounds.X},{ht.Bounds.Y},{ht.Bounds.Width},{ht.Bounds.Height}) clip=({ht.ClipBounds.X},{ht.ClipBounds.Y},{ht.ClipBounds.Width},{ht.ClipBounds.Height})");
        }
        var layoutResult = layoutPipeline.LastLayoutResult;
        if (layoutResult is not null)
        {
            foreach (var sd in layoutResult.ScrollDiagnostics)
            {
                Console.WriteLine($"  ScrollContainer[{sd.DfsIndex}]: visible={sd.VisibleHeight} content={sd.ContentHeight} scrollY={sd.ScrollY} maxScrollY={sd.MaxScrollY} elements={sd.VisibleElementCount}/{sd.VisibleElementCount + sd.ClippedElementCount} visible");
            }
        }
        Console.WriteLine("=== Diagnostic mode complete ===");

        FrameDrawingResources.Return(resources);
    }

    private static void RunResizeDiagnosticMode()
    {
        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);

        var resources = FrameDrawingResources.Rent();
        try
        {
            var textStyle = resources.AddTextStyle(TextStyle.Default);
            var text = resources.AddText("Resize Diagnostic: DirectWrite + D3D12 fence stress");
            resources.Seal();

            var commands = new DrawCommand[]
            {
                new(DrawCommandKind.FillRect,
                    Rect: new DrawRect(0, 0, 1280, 720),
                    Color: DrawColor.Opaque(32, 32, 32)),
                new(DrawCommandKind.FillRect,
                    Rect: new DrawRect(16, 80, 300, 44),
                    Color: DrawColor.Opaque(52, 120, 246)),
                new(DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 900, 36),
                    Resource: textStyle,
                    Text: text,
                    Color: DrawColor.Opaque(255, 255, 255))
            };

            GC.Collect();
            GC.WaitForPendingFinalizers();

            for (var i = 0; i < 80; i++)
            {
                var width = 720 + i % 17 * 19;
                var height = 420 + i % 11 * 17;
                d3d12Renderer.Resize(width, height);
                _ = d3d12Renderer.ApplyPendingResize();

                d3d12Backend.BeginFrame(default);
                d3d12Backend.Execute(commands, resources);
                d3d12Backend.EndFrame();

                if (d3d12Renderer.IsDeviceRemoved)
                {
                    break;
                }

                if (i % 8 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            Console.WriteLine("=== D3D12 Resize Diagnostics ===");
            Console.WriteLine($"Device removed: {d3d12Renderer.IsDeviceRemoved}");
            Console.WriteLine($"Device error reason: {d3d12Renderer.DeviceErrorReason ?? "(none)"}");
            Console.WriteLine($"Swapchain size: {d3d12Renderer.Width}x{d3d12Renderer.Height}");
            Console.WriteLine("=== Resize diagnostic mode complete ===");
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }
}
