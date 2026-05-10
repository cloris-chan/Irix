using Irix;
using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class Program
{
    private const string ScaleModePhysicalPixelsV0 = "PhysicalPixelsV0";

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
        var clipMode = args.Contains("--enable-scissor") ? DrawingBackendClipMode.Scissor : DrawingBackendClipMode.Diagnostic;
        var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer, clipMode);
        _backendClipMode = d3d12Backend.ClipMode;
        var showDiagnostics = args.Contains("--debug-ui");

        Action<double>? maxScrollYCallback = null;
        Action<CounterLayoutDiagnostics>? layoutDiagnosticsCallback = null;
        var manualLayoutDiagnosticsRefresh = false;
        WindowDrawCommandTranslator? drawCommandTranslator = null;
        drawCommandTranslator = new WindowDrawCommandTranslator(
            window,
            () => _ = d3d12Renderer.ApplyPendingResize(),
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
            },
            postFrameCallback: maxScrollY =>
            {
                maxScrollYCallback?.Invoke(maxScrollY);
                if (showDiagnostics && !manualLayoutDiagnosticsRefresh && drawCommandTranslator is not null)
                {
                    layoutDiagnosticsCallback?.Invoke(CreateLayoutDiagnostics(drawCommandTranslator));
                }
            });
        using var d3d12Compositor = new DrawingBackendCompositor(d3d12Backend);
        var compositor = args.Contains("--console")
            ? new CompositeCompositor(new ConsoleCompositor(Console.Out), d3d12Compositor)
            : (ICompositor)d3d12Compositor;
        await using var compositorLoop = new CompositorLoop(drawCommandTranslator, compositor);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(showDiagnostics, CreateViewportDiagnostics(window, d3d12Renderer, drawCommandTranslator), CounterLayoutDiagnostics.Empty), compositorLoop);
        var scrollFramePump = new ScrollFramePump();
        _scrollFramePump = scrollFramePump;
        var inputOwnershipState = new InputOwnershipState();
        _inputOwnershipState = inputOwnershipState;
        var lastDispatchedLayoutDiagnostics = CounterLayoutDiagnostics.Empty;
        var suppressNextLayoutDiagnosticsRefresh = false;

        layoutDiagnosticsCallback = diagnostics =>
        {
            if (suppressNextLayoutDiagnosticsRefresh)
            {
                if (runtime.CurrentModel.LayoutDiagnostics == lastDispatchedLayoutDiagnostics)
                {
                    suppressNextLayoutDiagnosticsRefresh = false;
                }

                return;
            }

            DispatchLayoutDiagnostics(diagnostics);
        };

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
            if (showDiagnostics)
            {
                _ = RequestResizeRenderAndRefreshDiagnosticsAsync();
            }
            else
            {
                _ = compositorLoop.RequestRenderAsync();
            }
        };
        using var inputSubscription = platformHost.RawInputEvents.Subscribe(new PlatformInputObserver(HandleInput));

        platformHost.TopologyChanged += OnTopologyChanged;

        Console.WriteLine($"Detected screens: {platformHost.Screens.Count}");
        Console.WriteLine("Rendering: D3D12 (clear color from FillRect)");
        Console.WriteLine($"Backend clip mode: {d3d12Backend.ClipMode}");
        Console.WriteLine("Controls: Click buttons, Up/Down = +/-1, R = reset, Mouse wheel = +/-1.");

        await runtime.StartAsync();

        window.RunMessageLoop();

        Console.WriteLine($"Final count: {runtime.CurrentModel.Count}");

        void HandleInput(RawInputEvent inputEvent)
        {
            if (TryMapInputForRuntime(inputEvent, inputOwnershipState, TryGetActionIdAt, out var message))
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
                else if (message is not null)
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

        async Task RequestResizeRenderAndRefreshDiagnosticsAsync()
        {
            try
            {
                manualLayoutDiagnosticsRefresh = true;
                await compositorLoop.RequestRenderAndWaitAsync();
                manualLayoutDiagnosticsRefresh = false;
                DispatchDebugDiagnostics(CreateViewportDiagnostics(window, d3d12Renderer, drawCommandTranslator), CreateLayoutDiagnostics(drawCommandTranslator));
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                manualLayoutDiagnosticsRefresh = false;
            }
        }

        void DispatchLayoutDiagnostics(CounterLayoutDiagnostics diagnostics)
        {
            if (!showDiagnostics || diagnostics == lastDispatchedLayoutDiagnostics)
            {
                return;
            }

            lastDispatchedLayoutDiagnostics = diagnostics;
            suppressNextLayoutDiagnosticsRefresh = true;
            runtime.Dispatch(new CounterMessage.LayoutDiagnosticsChanged(diagnostics));
        }

        void DispatchDebugDiagnostics(CounterViewportDiagnostics viewportDiagnostics, CounterLayoutDiagnostics layoutDiagnostics)
        {
            if (!showDiagnostics)
            {
                return;
            }

            lastDispatchedLayoutDiagnostics = layoutDiagnostics;
            suppressNextLayoutDiagnosticsRefresh = true;
            runtime.Dispatch(new CounterMessage.DebugDiagnosticsChanged(viewportDiagnostics, layoutDiagnostics));
        }

        platformHost.TopologyChanged -= OnTopologyChanged;
    }

    private static CounterViewportDiagnostics CreateViewportDiagnostics(INativeWindow window, D3D12Renderer renderer, WindowDrawCommandTranslator? translator)
    {
        var bounds = window.Region.PhysicalBounds;
        var rendererViewport = new PixelRectangle(bounds.X, bounds.Y, renderer.Width, renderer.Height);
        var layoutViewport = translator?.LastLayoutViewport ?? rendererViewport;
        if (layoutViewport.Width <= 0 || layoutViewport.Height <= 0)
        {
            layoutViewport = rendererViewport;
        }

        return new CounterViewportDiagnostics(rendererViewport, layoutViewport, ScaleModePhysicalPixelsV0);
    }

    private static CounterLayoutDiagnostics CreateLayoutDiagnostics(WindowDrawCommandTranslator translator)
    {
        return new CounterLayoutDiagnostics(translator.LayoutRebuildCount, translator.LastLayoutRebuildReason, DiagnosticsFormatter.FormatLayoutDirtyClassifications(translator.LastDirtyClassifications));
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

    internal static string[] BuildStyleOnlyPatchPlanSmokeDiagnosticLines()
    {
        var hoverOnlySnapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan("hoverOnly", BuildHoverOnlyStyleOnlyPatchPlan());
        var layoutAffectingSnapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan("layoutAffecting", BuildLayoutAffectingStyleOnlyPatchPlan());

        return [
            "=== StyleOnly Patch Plan Diagnostics ===",
            DiagnosticsFormatter.BuildStyleOnlyPatchPlanDiagnosticLine(hoverOnlySnapshot),
            DiagnosticsFormatter.BuildStyleOnlyPatchPlanDiagnosticLine(layoutAffectingSnapshot)
        ];
    }

    private static StyleOnlyPatchPlan BuildHoverOnlyStyleOnlyPatchPlan()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var root2 = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))));

        using var frame1 = pipeline.Build(root1, viewport);
        var retainedLayout = pipeline.LastLayoutResult;
        ElementCommandRange[] retainedCommandRanges = [.. pipeline.LastElementCommandRanges];
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, [1]);
        return StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Elements,
            pipeline.LastDirtyElementRanges);
    }

    private static StyleOnlyPatchPlan BuildLayoutAffectingStyleOnlyPatchPlan()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(0))],
            children: [VirtualNodeFactory.Button("Increment", 2, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")))]);
        var root2 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(24))],
            children: [VirtualNodeFactory.Button("Increment", 2, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")))]);

        using var frame1 = pipeline.Build(root1, viewport);
        var retainedLayout = pipeline.LastLayoutResult;
        ElementCommandRange[] retainedCommandRanges = [.. pipeline.LastElementCommandRanges];
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, [0]);
        return StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Elements,
            pipeline.LastDirtyElementRanges);
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
    private static DrawingBackendClipMode _backendClipMode = DrawingBackendClipMode.Diagnostic;

    // ── Diagnostic readouts ────────────────────────────────────────────

    internal static double DiagPendingPx => _scrollFramePump?.PendingPixels ?? 0;
    internal static bool DiagScrollFrameQueued => _scrollFramePump?.IsFrameQueued ?? false;
    internal static bool DiagTickLoopRunning => _scrollFramePump?.IsLoopRunning ?? false;
    internal static long DiagScrollDispatchedFrameCount => _scrollFramePump?.DispatchedFrameCount ?? 0;
    internal static double DiagScrollRenderWaitMs => _scrollFramePump?.RenderWaitMs ?? 0;
    internal static double DiagScrollLastDt => _scrollFramePump?.LastDt ?? 0;
    internal static double DiagScrollDrainedPixels => _scrollFramePump?.DrainedPixels ?? 0;
    internal static OwnershipSnapshot DiagInputOwnership => _inputOwnershipState?.Snapshot ?? default;
    internal static DrawingBackendClipMode DiagBackendClipMode => _backendClipMode;

    internal static bool TryMapInputForRuntime(
        RawInputEvent inputEvent,
        InputOwnershipState ownershipState,
        Func<int, int, string?> tryGetActionIdAt,
        out CounterMessage? message)
    {
        var before = ownershipState.Snapshot;
        var mapped = CounterInputRouter.TryMapInput(inputEvent, ownershipState, tryGetActionIdAt, out var mappedMessage);
        var after = ownershipState.Snapshot;

        if (mapped)
        {
            message = mappedMessage is CounterMessage.WheelRaw
                ? mappedMessage
                : new CounterMessage.RoutedInput(mappedMessage, after);
            return true;
        }

        if (before != after)
        {
            message = new CounterMessage.InputVisualStateChanged(after);
            return true;
        }

        message = null;
        return false;
    }

    internal static async Task RunInputDiagnosticModeAsync(
        TextWriter output,
        string? reportPath = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = BuildInputDiagnosticsSnapshot();
        var lines = DiagnosticsFormatter.BuildInputDiagnosticLines(snapshot);

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

    internal static InputDiagnosticsSnapshot BuildInputDiagnosticsSnapshot()
    {
        var ownershipState = new InputOwnershipState();
        var lines = new List<string>();
        var ownershipLines = new List<string>();
        var buttonVisualStateLines = new List<string>();

        lines.Add("buttonPriorityOrder Pressed > Hovered > Focused > Normal");
        AddButtonVisualStateLine($"buttonState normal Increment {DiagnosticsFormatter.FormatButtonState(default)}");
        AddButtonVisualStateLine($"buttonState hovered Increment {DiagnosticsFormatter.FormatButtonState(new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: true))}");
        AddButtonVisualStateLine($"buttonState pressed Increment {DiagnosticsFormatter.FormatButtonState(new ButtonVisualState(IsHovered: true, IsPressed: true, IsFocused: true))}");
        AddButtonVisualStateLine($"buttonState focused Increment {DiagnosticsFormatter.FormatButtonState(new ButtonVisualState(IsHovered: false, IsPressed: false, IsFocused: true))}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"afterMove {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState afterMove Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, nameof(CounterMessage.Increment)))}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"afterPress {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState afterPress Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, nameof(CounterMessage.Increment)))}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 3, X: 32, Y: 200),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"duringCaptureMove {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState duringCaptureMove Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, nameof(CounterMessage.Increment)))}");

        var releaseMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 4,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitInputDiagnosticTarget,
            out var releaseMessage);
        AddOwnershipLine($"releaseOutside mapped={releaseMapped} message={DiagnosticsFormatter.FormatMessage(releaseMessage)} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState releaseOutside Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, nameof(CounterMessage.Increment)))}");

        var enterMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 5, X: 0, Y: 0, KeyCode: 0x0D),
            ownershipState,
            HitInputDiagnosticTarget,
            out var enterMessage);
        AddOwnershipLine($"keyboardEnter mapped={enterMapped} message={DiagnosticsFormatter.FormatMessage(enterMessage)} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");

        var spaceMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 6, X: 0, Y: 0, KeyCode: 0x20),
            ownershipState,
            HitInputDiagnosticTarget,
            out var spaceMessage);
        AddOwnershipLine($"keyboardSpace mapped={spaceMapped} message={DiagnosticsFormatter.FormatMessage(spaceMessage)} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");

        var pressEmptyMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 7,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"pressEmpty mapped={pressEmptyMapped} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");

        var releaseEmptyMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 8,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"releaseAfterEmptyPress mapped={releaseEmptyMapped} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.FocusLost, Timestamp: 9, X: 0, Y: 0),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"focusLost {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState focusLost Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, nameof(CounterMessage.Increment)))}");
        lines.Add("events:");
        var eventLines = new List<string>();
        foreach (var diagnosticEvent in ownershipState.DiagnosticEvents)
        {
            var eventLine = $"  {DiagnosticsFormatter.FormatOwnershipEvent(diagnosticEvent)}";
            eventLines.Add(eventLine);
            lines.Add(eventLine);
        }

        var dirtyReasonLines = BuildInputDirtyReasonDiagnosticLines();
        lines.AddRange(dirtyReasonLines);

        return new InputDiagnosticsSnapshot(ownershipState.Snapshot, lines, ownershipLines, buttonVisualStateLines, eventLines, dirtyReasonLines);

        void AddOwnershipLine(string line)
        {
            ownershipLines.Add(line);
            lines.Add(line);
        }

        void AddButtonVisualStateLine(string line)
        {
            buttonVisualStateLines.Add(line);
            lines.Add(line);
        }
    }

    private static string? HitInputDiagnosticTarget(int x, int y)
    {
        return (x, y) switch
        {
            (32, 140) => nameof(CounterMessage.Increment),
            (32, 200) => nameof(CounterMessage.Decrement),
            _ => null
        };
    }

    internal static string[] BuildInputDirtyReasonDiagnosticLines()
    {
        var app = new CounterApplication();
        var ownershipState = new InputOwnershipState();
        var model = app.Initialize();
        var currentTree = app.BuildView(model);
        var retainedTree = new RetainedTree(default);
        var pipeline = new RenderPipeline(CounterStylePreset.Default);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        using (var initialPatch = VirtualNodeDiffer.CreatePatchBatch(default, currentTree))
        {
            var initialDirty = retainedTree.Apply(initialPatch);
            using var initialFrame = pipeline.Build(retainedTree.Tree.Root, viewport, initialDirty);
        }

        var lines = new List<string>
        {
            "dirtyReasons:"
        };

        ApplyInput("hoverOnly", new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140));
        ApplyInput("press", new RawInputEvent(
            RawInputEventKind.PointerPressed,
            Timestamp: 2,
            X: 32,
            Y: 140,
            Button: PointerButton.Left));
        ApplyInput("release", new RawInputEvent(
            RawInputEventKind.PointerReleased,
            Timestamp: 3,
            X: 500,
            Y: 500,
            Button: PointerButton.Left));

        return lines.ToArray();

        void ApplyInput(string name, RawInputEvent inputEvent)
        {
            if (!TryMapInputForRuntime(inputEvent, ownershipState, HitDiagnosticTarget, out var message) || message is null or CounterMessage.WheelRaw)
            {
                lines.Add($"dirtyReason {name} reason={LayoutRebuildReason.None} classifications=(none)");
                return;
            }

            model = app.Update(model, message).NextModel;
            var nextTree = app.BuildView(model);
            using var patch = VirtualNodeDiffer.CreatePatchBatch(currentTree, nextTree);
            var dirty = retainedTree.Apply(patch);
            using var frame = pipeline.Build(retainedTree.Tree.Root, viewport, dirty);
            lines.Add($"dirtyReason {name} reason={pipeline.LastLayoutRebuildReason} classifications={DiagnosticsFormatter.FormatLayoutDirtyClassifications(pipeline.LastDirtyClassifications)}");
            currentTree = nextTree;
        }

        static string? HitDiagnosticTarget(int x, int y)
        {
            return x == 32 && y == 140 ? nameof(CounterMessage.Increment) : null;
        }
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

        var snapshot = new ScrollDiagnosticsSnapshot(
            pump.DispatchedFrameCount,
            pump.RenderWaitMs,
            pump.LastDt,
            totalDrainedPixels,
            pump.DrainedPixels,
            pump.PendingPixels,
            pump.IsFrameQueued,
            pump.IsLoopRunning,
            ScrollController.GetScrollY(scrollState),
            scrollState.TargetPosition,
            scrollState.MaxScrollY,
            scrollState.HasMaxScrollY);
        var lines = DiagnosticsFormatter.BuildScrollDiagnosticLines(snapshot);

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
        _backendClipMode = d3d12Backend.ClipMode;
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

        var initialBackendSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        foreach (var line in DiagnosticsFormatter.BuildBackendDeviceDiagnosticLines(initialBackendSnapshot))
        {
            Console.WriteLine(line);
        }
        Console.WriteLine($"Swapchain size: {d3d12Renderer.Width}x{d3d12Renderer.Height}");
        foreach (var line in DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePreset.DefaultName, RenderStylePreset.Default))
        {
            Console.WriteLine(line);
        }
        Console.WriteLine($"=== Compositor Diagnostics ===");
        var renderCount = compositor.RenderCount;
        var partialApplyCount = compositor.PartialApplyCount;
        var fullApplyCount = compositor.FullApplyCount;
        var emptyFrameCount = compositor.EmptyFrameCount;
        var dirty = compositor.LastDirtyCommandRanges;
        var backendDirty = d3d12Backend.LastDirtyCommandRanges;
        var backendClippedCommandCount = d3d12Backend.ClippedCommandCount;

        // Layout-driven frame: render through VirtualNode → Layout → Pipeline → Compositor
        // to verify the clip chain produces clipped commands
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
        var renderingSnapshot = new RenderingPipelineDiagnosticSnapshot(
            renderCount,
            partialApplyCount,
            fullApplyCount,
            emptyFrameCount,
            dirty,
            backendDirty,
            backendClippedCommandCount,
            layoutBatch.Commands.Count,
            layoutClipCount,
            layoutPipeline.LayoutRebuildCount,
            layoutPipeline.LastLayoutRebuildReason,
            layoutPipeline.LastDirtyClassifications,
            layoutBatch.HitTargets,
            layoutPipeline.LastLayoutResult?.ScrollDiagnostics ?? []);
        foreach (var line in DiagnosticsFormatter.BuildRenderingPipelineCompositorDiagnosticLines(renderingSnapshot))
        {
            Console.WriteLine(line);
        }
        Console.WriteLine($"=== Layout Pipeline Diagnostics ===");
        foreach (var line in DiagnosticsFormatter.BuildRenderingPipelineLayoutDiagnosticLines(renderingSnapshot))
        {
            Console.WriteLine(line);
        }
        foreach (var line in BuildStyleOnlyPatchPlanSmokeDiagnosticLines())
        {
            Console.WriteLine(line);
        }
        Console.WriteLine($"=== Pipeline Scissor Smoke ===");
        d3d12Backend.SetClipMode(DrawingBackendClipMode.Scissor);
        _backendClipMode = d3d12Backend.ClipMode;
        RunPipelineScissorSmokeDiagnostic(compositor, d3d12Backend, d3d12Renderer);
        var pipelineScissorSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        Console.WriteLine(DiagnosticsFormatter.BuildPipelineScissorSmokeDiagnosticLine(pipelineScissorSnapshot));
        Console.WriteLine($"=== Pipeline Text Clip Smoke ===");
        RunPipelineTextClipSmokeDiagnostic(compositor, d3d12Backend, d3d12Renderer);
        var pipelineTextClipSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        Console.WriteLine(DiagnosticsFormatter.BuildPipelineTextClipSmokeDiagnosticLine(pipelineTextClipSnapshot));

        Console.WriteLine($"=== Clip Scissor Diagnostics ===");
        var smokeClip = new DrawRect(32, 32, 80, 40);
        d3d12Backend.SetClipMode(DrawingBackendClipMode.Scissor);
        _backendClipMode = d3d12Backend.ClipMode;
        RunClipScissorSmokeDiagnostic(d3d12Backend);
        var clipScissorSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        Console.WriteLine(DiagnosticsFormatter.BuildBackendClipModeDiagnosticLine(clipScissorSnapshot));
        Console.WriteLine(DiagnosticsFormatter.BuildClipScissorSmokeDiagnosticLine(smokeClip, clipScissorSnapshot));
        Console.WriteLine($"=== Empty Scissor Diagnostics ===");
        RunEmptyScissorSmokeDiagnostic(d3d12Backend);
        var emptyScissorSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        Console.WriteLine(DiagnosticsFormatter.BuildEmptyScissorSmokeDiagnosticLine(emptyScissorSnapshot));
        Console.WriteLine($"=== Text Clip Diagnostics ===");
        RunTextClipSmokeDiagnostic(d3d12Backend);
        var textClipSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        Console.WriteLine(DiagnosticsFormatter.BuildTextClipSmokeDiagnosticLine(textClipSnapshot));
        Console.WriteLine("=== Diagnostic mode complete ===");

        FrameDrawingResources.Return(resources);
    }

    private static void RunPipelineScissorSmokeDiagnostic(DrawingBackendCompositor compositor, D3D12DrawingBackend backend, D3D12Renderer renderer)
    {
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1000,
            attributes: [new VirtualNodeAttribute("Height", AttributeValue.FromNumber(40))],
            children: [VirtualNodeFactory.Rectangle(160, 80, 1001)]);
        var viewport = new PixelRectangle(0, 0, renderer.Width, renderer.Height);
        using var batch = pipeline.Build(root, viewport);

        backend.SetClipMode(DrawingBackendClipMode.Scissor);
        compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
    }

    private static void RunPipelineTextClipSmokeDiagnostic(DrawingBackendCompositor compositor, D3D12DrawingBackend backend, D3D12Renderer renderer)
    {
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1100,
            attributes: [new VirtualNodeAttribute("Height", AttributeValue.FromNumber(20))],
            children:
            [
                VirtualNodeFactory.Button("PipelineClip", 1101, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("PipelineClip")))
            ]);
        var viewport = new PixelRectangle(0, 0, renderer.Width, renderer.Height);
        using var batch = pipeline.Build(root, viewport);

        backend.SetClipMode(DrawingBackendClipMode.Scissor);
        compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
    }

    private static void RunClipScissorSmokeDiagnostic(D3D12DrawingBackend backend)
    {
        var commands = new[]
        {
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 16, 160, 80),
                ClipBounds: new DrawRect(32, 32, 80, 40),
                Color: DrawColor.Opaque(72, 136, 255))
        };

        backend.BeginFrame(default);
        backend.Execute(commands, FrameDrawingResources.Empty);
        backend.EndFrame();
    }

    private static void RunEmptyScissorSmokeDiagnostic(D3D12DrawingBackend backend)
    {
        var commands = new[]
        {
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 16, 160, 80),
                ClipBounds: new DrawRect(2048, 2048, 80, 40),
                Color: DrawColor.Opaque(72, 136, 255))
        };

        backend.BeginFrame(default);
        backend.Execute(commands, FrameDrawingResources.Empty);
        backend.EndFrame();
    }

    private static void RunTextClipSmokeDiagnostic(D3D12DrawingBackend backend)
    {
        var resources = FrameDrawingResources.Rent();
        try
        {
            var textStyle = resources.AddTextStyle(TextStyle.Default);
            var skippedText = resources.AddText("Skipped text clip smoke");
            var clippedText = resources.AddText("Clipped text clip smoke");
            resources.Seal();

            var commands = new[]
            {
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 160, 80),
                    Resource: textStyle,
                    Text: skippedText,
                    ClipBounds: new DrawRect(2048, 2048, 80, 40),
                    Color: DrawColor.Opaque(255, 255, 255)),
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 160, 80),
                    Resource: textStyle,
                    Text: clippedText,
                    ClipBounds: new DrawRect(32, 32, 80, 40),
                    Color: DrawColor.Opaque(255, 255, 255))
            };

            backend.BeginFrame(default);
            backend.Execute(commands, resources);
            backend.EndFrame();
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }

    private static void RunResizeDiagnosticMode()
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);
        var lastAppliedPendingResize = window.Region.PhysicalBounds;
        var translator = new WindowDrawCommandTranslator(
            window,
            () =>
            {
                if (d3d12Renderer.ApplyPendingResize())
                {
                    var bounds = window.Region.PhysicalBounds;
                    lastAppliedPendingResize = new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
                }
            },
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
            },
            postFrameCallback: null);

        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1200,
            children:
            [
                VirtualNodeFactory.Text("Resize Diagnostic: renderer/layout viewport", 1201),
                VirtualNodeFactory.Rectangle(300, 44, 1202),
                VirtualNodeFactory.Button("ResizeBtn", 1203, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("ResizeBtn")))
            ]);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (var i = 0; i < 80; i++)
        {
            var width = 720 + i % 17 * 19;
            var height = 420 + i % 11 * 17;
            var oldBounds = window.Region.PhysicalBounds;
            window.Region = new ScreenRegion(window.Region.ScreenId, new PixelRectangle(oldBounds.X, oldBounds.Y, width, height));
            d3d12Renderer.Resize(width, height);

            using var patch = i == 0
                ? VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root))
                : PatchBatch.CreateRenderRequest();
            using var frame = translator.Translate(patch);
            compositor.RenderAsync(frame).AsTask().GetAwaiter().GetResult();

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

        var windowBounds = window.Region.PhysicalBounds;
        var rendererBounds = new PixelRectangle(windowBounds.X, windowBounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
        var snapshot = new ViewportDiagnosticsSnapshot(
            windowBounds,
            rendererBounds,
            translator.LastViewport,
            translator.LastLayoutViewport,
            lastAppliedPendingResize,
            compositor.RenderCount,
            translator.LayoutRebuildCount,
            translator.LastLayoutRebuildReason.ToString(),
            screen.DpiScale,
            "ProcessDefault",
            ScaleModePhysicalPixelsV0);

        Console.WriteLine("=== D3D12 Resize Diagnostics ===");
        Console.WriteLine($"Device removed: {d3d12Renderer.IsDeviceRemoved}");
        Console.WriteLine($"Device error reason: {d3d12Renderer.DeviceErrorReason ?? "(none)"}");
        Console.WriteLine($"Swapchain size: {d3d12Renderer.Width}x{d3d12Renderer.Height}");
        foreach (var line in DiagnosticsFormatter.BuildResizeViewportDiagnosticLines(snapshot))
        {
            Console.WriteLine(line);
        }
        Console.WriteLine("=== Resize diagnostic mode complete ===");
    }
}
