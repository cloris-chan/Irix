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

        if (args.Contains("--diagnose-scroll"))
        {
            await RunScrollDiagnosticModeAsync(Console.Out, Path.Combine("TestResults", "diagnose-scroll.txt"));
            return;
        }

        if (args.Contains("--diagnose-input"))
        {
            await RunInputDiagnosticModeAsync(Console.Out, Path.Combine("TestResults", "diagnose-input.txt"));
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
        var scrollFramePump = new ScrollFramePump();
        _scrollFramePump = scrollFramePump;
        var inputOwnershipState = new InputOwnershipState();
        _inputOwnershipState = inputOwnershipState;

        // Wire up MaxScrollY feedback after runtime is created
        double? lastKnownMaxScrollY = null;
        maxScrollYCallback = maxScrollY =>
        {
            if (!lastKnownMaxScrollY.HasValue || Math.Abs(maxScrollY - lastKnownMaxScrollY.Value) > 0.5)
            {
                lastKnownMaxScrollY = maxScrollY;
                runtime.Dispatch(new CounterMessage.UpdateMaxScrollY(maxScrollY));
            }
        };

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
            if (CounterInputRouter.TryMapInput(inputEvent, inputOwnershipState, TryGetActionIdAt, out var message))
            {
                if (message is CounterMessage.WheelRaw wheel)
                {
                    // Coalesce: accumulate raw delta, don't dispatch to Runtime
                    var pixels = ScrollController.ConvertToPixels(
                        new ScrollDelta(ScrollDeltaUnit.WheelRaw, wheel.RawDelta),
                        ScrollMetrics.DefaultText,
                        SystemScrollSettings.Default);
                    scrollFramePump.AddPendingPixels(pixels);
                    scrollFramePump.EnsureRunning(
                        (frame, cancellationToken) => runtime.DispatchAndWaitAsync(frame, cancellationToken),
                        () => runtime.CurrentModel.Scroll);
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

    private static ScrollFramePump? _scrollFramePump;
    private static InputOwnershipState? _inputOwnershipState;

    // ── Diagnostic readouts ────────────────────────────────────────────

    internal static double DiagPendingPx => _scrollFramePump?.PendingPixels ?? 0;
    internal static bool DiagScrollFrameQueued => _scrollFramePump?.IsFrameQueued ?? false;
    internal static bool DiagTickLoopRunning => _scrollFramePump?.IsLoopRunning ?? false;
    internal static long DiagScrollDispatchedFrameCount => _scrollFramePump?.DispatchedFrameCount ?? 0;
    internal static double DiagScrollRenderWaitMs => _scrollFramePump?.RenderWaitMs ?? 0;
    internal static double DiagScrollLastDt => _scrollFramePump?.LastDt ?? 0;
    internal static double DiagScrollDrainedPixels => _scrollFramePump?.DrainedPixels ?? 0;
    internal static OwnershipSnapshot DiagInputOwnership => _inputOwnershipState?.Snapshot ?? default;

    internal static async Task RunInputDiagnosticModeAsync(
        TextWriter output,
        string? reportPath = null,
        CancellationToken cancellationToken = default)
    {
        var ownershipState = new InputOwnershipState();
        var lines = new List<string>
        {
            "=== Input Ownership Diagnostics ==="
        };

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            HitDiagnosticTarget,
            out _);
        lines.Add($"afterMove {FormatOwnership(ownershipState.Snapshot)}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitDiagnosticTarget,
            out _);
        lines.Add($"afterPress {FormatOwnership(ownershipState.Snapshot)}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 3, X: 32, Y: 200),
            ownershipState,
            HitDiagnosticTarget,
            out _);
        lines.Add($"duringCaptureMove {FormatOwnership(ownershipState.Snapshot)}");

        var releaseMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 4,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitDiagnosticTarget,
            out var releaseMessage);
        lines.Add($"releaseOutside mapped={releaseMapped} message={FormatMessage(releaseMessage)} {FormatOwnership(ownershipState.Snapshot)}");

        var enterMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 5, X: 0, Y: 0, KeyCode: 0x0D),
            ownershipState,
            HitDiagnosticTarget,
            out var enterMessage);
        lines.Add($"keyboardEnter mapped={enterMapped} message={FormatMessage(enterMessage)} {FormatOwnership(ownershipState.Snapshot)}");

        var spaceMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 6, X: 0, Y: 0, KeyCode: 0x20),
            ownershipState,
            HitDiagnosticTarget,
            out var spaceMessage);
        lines.Add($"keyboardSpace mapped={spaceMapped} message={FormatMessage(spaceMessage)} {FormatOwnership(ownershipState.Snapshot)}");

        var pressEmptyMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 7,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitDiagnosticTarget,
            out _);
        lines.Add($"pressEmpty mapped={pressEmptyMapped} {FormatOwnership(ownershipState.Snapshot)}");

        var releaseEmptyMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 8,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitDiagnosticTarget,
            out _);
        lines.Add($"releaseAfterEmptyPress mapped={releaseEmptyMapped} {FormatOwnership(ownershipState.Snapshot)}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.FocusLost, Timestamp: 9, X: 0, Y: 0),
            ownershipState,
            HitDiagnosticTarget,
            out _);
        lines.Add($"focusLost {FormatOwnership(ownershipState.Snapshot)}");
        lines.Add("events:");
        foreach (var diagnosticEvent in ownershipState.DiagnosticEvents)
        {
            lines.Add($"  {FormatOwnershipEvent(diagnosticEvent)}");
        }

        lines.Add("=== Input diagnostic mode complete ===");

        foreach (var line in lines)
        {
            await output.WriteLineAsync(line);
        }

        if (reportPath is not null)
        {
            var directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllLinesAsync(reportPath, lines, cancellationToken);
        }

        static string? HitDiagnosticTarget(int x, int y)
        {
            return (x, y) switch
            {
                (32, 140) => nameof(CounterMessage.Increment),
                (32, 200) => nameof(CounterMessage.Decrement),
                _ => null
            };
        }
    }

    private static string FormatOwnership(OwnershipSnapshot snapshot)
    {
        return $"hover={FormatTarget(snapshot.HoveredTarget)} focus={FormatTarget(snapshot.FocusedTarget)} pressed={FormatTarget(snapshot.PressedTarget)} capture={FormatTarget(snapshot.CapturedTarget)} hoverChanges={snapshot.HoverChangeCount} pointerPressed={snapshot.IsPointerPressed}";
    }

    private static string FormatTarget(string? target)
    {
        return string.IsNullOrWhiteSpace(target) ? "-" : target;
    }

    private static string FormatMessage(CounterMessage? message)
    {
        return message?.GetType().Name ?? "-";
    }

    private static string FormatOwnershipEvent(InputOwnershipEvent diagnosticEvent)
    {
        return diagnosticEvent switch
        {
            InputOwnershipEvent.HoverChanged hover =>
                $"HoverChanged previous={FormatTarget(hover.PreviousTarget)} current={FormatTarget(hover.CurrentTarget)}",
            InputOwnershipEvent.FocusChanged focus =>
                $"FocusChanged previous={FormatTarget(focus.PreviousTarget)} current={FormatTarget(focus.CurrentTarget)}",
            InputOwnershipEvent.PressedChanged pressed =>
                $"PressedChanged previousPressed={FormatTarget(pressed.PreviousPressedTarget)} currentPressed={FormatTarget(pressed.CurrentPressedTarget)} previousCapture={FormatTarget(pressed.PreviousCapturedTarget)} currentCapture={FormatTarget(pressed.CurrentCapturedTarget)} pointerPressed={pressed.IsPointerPressed}",
            _ => diagnosticEvent.GetType().Name
        };
    }

    internal static async Task RunScrollDiagnosticModeAsync(
        TextWriter output,
        string? reportPath = null,
        CancellationToken cancellationToken = default)
    {
        var pump = new ScrollFramePump();
        var scrollState = ScrollState.Default with { MaxScrollY = 240, HasMaxScrollY = true };
        var frameCount = 0;
        var totalDrainedPixels = 0.0;

        pump.AddPendingPixels(54);

        await pump.RunUntilIdleAsync(
            async (frame, token) =>
            {
                frameCount++;
                totalDrainedPixels += frame.Delta.Value;
                scrollState = ScrollController.ApplyScrollDelta(
                    scrollState,
                    frame.Delta,
                    ScrollMetrics.DefaultText,
                    SystemScrollSettings.Default);
                scrollState = ScrollController.Tick(scrollState, frame.DeltaTime);

                await Task.Delay(20, token);

                if (frameCount >= 2)
                {
                    scrollState = scrollState with
                    {
                        Position = scrollState.TargetPosition,
                        IsAnimating = false
                    };
                }
            },
            () => scrollState,
            cancellationToken);

        var lines = new[]
        {
            "=== Scroll Pump Diagnostics ===",
            $"frames={pump.DispatchedFrameCount}",
            $"waitMs={pump.RenderWaitMs:F3}",
            $"dt={pump.LastDt:F4}",
            $"drained={totalDrainedPixels:F1}",
            $"lastFrameDrained={pump.DrainedPixels:F1}",
            $"pending={pump.PendingPixels:F1}",
            "=== Scroll diagnostic mode complete ==="
        };

        foreach (var line in lines)
        {
            await output.WriteLineAsync(line);
        }

        if (reportPath is not null)
        {
            var directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllLinesAsync(reportPath, lines, cancellationToken);
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
