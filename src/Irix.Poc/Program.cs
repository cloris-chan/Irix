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

        Action<double>? maxScrollYCallback = null;
        var drawCommandTranslator = new WindowDrawCommandTranslator(
            window,
            () => _ = d3d12Renderer.ApplyPendingResize(),
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
            },
            postFrameCallback: maxScrollY => maxScrollYCallback?.Invoke(maxScrollY));
        using var d3d12Compositor = new DrawingBackendCompositor(d3d12Backend);
        var compositor = args.Contains("--console")
            ? new CompositeCompositor(new ConsoleCompositor(Console.Out), d3d12Compositor)
            : (ICompositor)d3d12Compositor;
        await using var compositorLoop = new CompositorLoop(drawCommandTranslator, compositor);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);

        // Wire up MaxScrollY feedback after runtime is created
        var lastKnownMaxScrollY = 0.0;
        maxScrollYCallback = maxScrollY =>
        {
            if (Math.Abs(maxScrollY - lastKnownMaxScrollY) > 0.5)
            {
                lastKnownMaxScrollY = maxScrollY;
                runtime.Dispatch(new CounterMessage.UpdateMaxScrollY(maxScrollY));
            }
        };

        // Track previous scroll state for backpressure flag clearing.
        // The tick loop compares runtime.CurrentModel.Scroll before and after
        // each tick — when the Runtime has processed a ScrollFrame, the scroll
        // state changes and the _scrollFrameQueued flag can be cleared.

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
                if (message is CounterMessage.WheelRaw wheel)
                {
                    // Coalesce: accumulate raw delta, don't dispatch to Runtime
                    var pixels = ScrollController.ConvertToPixels(
                        new ScrollDelta(ScrollDeltaUnit.WheelRaw, wheel.RawDelta),
                        ScrollMetrics.DefaultText,
                        SystemScrollSettings.Default);
                    AddPendingScrollDelta(pixels);
                    EnsureScrollTickLoop(runtime, compositorLoop);
                }
                else
                {
                    runtime.Dispatch(message);
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
    private static int _scrollTickLoopRunning; // 0 = idle, 1 = running
    private static long _pendingScrollDeltaBits; // double encoded as long for Interlocked
    private static int _scrollFrameQueued; // 1 = ScrollFrame dispatched but not yet processed by Runtime
    private static double _lastDispatchedTarget; // scroll target at time of last dispatch (for backpressure detection)
    private static double _lastDispatchedPosition; // scroll position at time of last dispatch

    // ── Diagnostic readouts ────────────────────────────────────────────

    internal static double DiagPendingPx
    {
        get
        {
            var bits = Volatile.Read(ref _pendingScrollDeltaBits);
            return BitConverter.Int64BitsToDouble(bits);
        }
    }

    internal static bool DiagFrameQueued => Volatile.Read(ref _scrollFrameQueued) != 0;
    internal static bool DiagTickLoopRunning => Volatile.Read(ref _scrollTickLoopRunning) != 0;

    /// <summary>
    /// Ensures exactly one tick loop is running. If a loop is already active,
    /// this is a no-op — the existing loop will pick up the new scroll state
    /// on its next iteration.
    /// </summary>
    private static void EnsureScrollTickLoop(
        Runtime<CounterModel, CounterMessage> runtime,
        CompositorLoop compositorLoop)
    {
        if (Interlocked.Exchange(ref _scrollTickLoopRunning, 1) == 0)
        {
            _ = RunScrollTickLoopAsync(runtime, compositorLoop);
        }
    }

    /// <summary>
    /// Drain accumulated scroll delta. Thread-safe: called from animation loop,
    /// written from input thread via Interlocked.
    /// </summary>
    private static double DrainPendingScrollDelta()
    {
        var bits = Interlocked.Exchange(ref _pendingScrollDeltaBits, 0);
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static void AddPendingScrollDelta(double pixels)
    {
        long current, updated;
        do
        {
            current = Volatile.Read(ref _pendingScrollDeltaBits);
            var currentDouble = BitConverter.Int64BitsToDouble(current);
            var newDouble = currentDouble + pixels;
            updated = BitConverter.DoubleToInt64Bits(newDouble);
        } while (Interlocked.CompareExchange(ref _pendingScrollDeltaBits, updated, current) != current);
    }

    /// <summary>
    /// Animation loop: each frame drains pending scroll delta and dispatches
    /// a single coalesced ScrollFrame(delta, dt) message. Replaces both
    /// Scroll and Tick — one message per frame maximum.
    ///
    /// Design:
    /// - No probe drain — first delta is never lost.
    /// - First frame dispatches immediately (dt=0) before entering tick loop.
    /// - Backpressure: only one ScrollFrame queued at a time. While the Runtime
    ///   has an unprocessed ScrollFrame, new deltas accumulate in pending.
    ///   Flag is cleared by detecting model scroll state change.
    /// - RequestRenderAsync is fire-and-forget (coalescing signal, not await).
    /// - Exit after 3 consecutive idle frames.
    /// </summary>
    private static async Task RunScrollTickLoopAsync(
        Runtime<CounterModel, CounterMessage> runtime,
        CompositorLoop compositorLoop)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastTick = stopwatch.Elapsed;

            // First frame: drain and dispatch immediately — no probe, no delay.
            var firstPending = DrainPendingScrollDelta();
            if (firstPending != 0)
            {
                var firstDelta = new ScrollDelta(ScrollDeltaUnit.Pixel, firstPending);
                runtime.Dispatch(new CounterMessage.ScrollFrame(firstDelta, DeltaTime: 0));
                Volatile.Write(ref _scrollFrameQueued, 1);
                compositorLoop.RequestRenderAsync(); // fire-and-forget
            }

            // Tick loop
            var consecutiveIdle = 0;
            while (consecutiveIdle < 3)
            {
                await Task.Delay(TickInterval);
                var now = stopwatch.Elapsed;
                var dt = (now - lastTick).TotalSeconds;
                lastTick = now;

                // Clear backpressure flag: detect if Runtime processed the last
                // ScrollFrame by comparing scroll state. The Runtime processes
                // messages sequentially, so a state change means our frame was consumed.
                if (Volatile.Read(ref _scrollFrameQueued) != 0)
                {
                    var currentScroll = runtime.CurrentModel.Scroll;
                    if (currentScroll.TargetPosition != _lastDispatchedTarget
                        || currentScroll.Position != _lastDispatchedPosition)
                    {
                        Volatile.Write(ref _scrollFrameQueued, 0);
                    }
                }

                // Backpressure: if Runtime hasn't processed the last frame yet,
                // only accumulate deltas — don't dispatch another frame.
                if (Volatile.Read(ref _scrollFrameQueued) != 0)
                {
                    var stillAnimating = runtime.CurrentModel.Scroll.IsAnimating;
                    var hasPending = Volatile.Read(ref _pendingScrollDeltaBits) != 0;
                    consecutiveIdle = (stillAnimating || hasPending) ? 0 : consecutiveIdle + 1;
                    continue;
                }

                // Drain all accumulated scroll deltas for this frame
                var pendingPixels = DrainPendingScrollDelta();
                if (pendingPixels == 0 && !runtime.CurrentModel.Scroll.IsAnimating)
                {
                    consecutiveIdle++;
                    continue;
                }

                // Dispatch a single coalesced frame
                var delta = new ScrollDelta(ScrollDeltaUnit.Pixel, pendingPixels);
                _lastDispatchedTarget = runtime.CurrentModel.Scroll.TargetPosition + pendingPixels;
                _lastDispatchedPosition = runtime.CurrentModel.Scroll.Position;
                runtime.Dispatch(new CounterMessage.ScrollFrame(delta, dt));
                Volatile.Write(ref _scrollFrameQueued, 1);
                compositorLoop.RequestRenderAsync(); // fire-and-forget
                consecutiveIdle = 0;
            }
        }
        finally
        {
            Volatile.Write(ref _scrollFrameQueued, 0);
            Interlocked.Exchange(ref _scrollTickLoopRunning, 0);
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
