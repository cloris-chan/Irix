using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static partial class Program
{
    public static async Task Main(string[] args)
    {
        var diagnosticTask = TryCreateDiagnosticCliTask(args);
        if (diagnosticTask is not null)
        {
            await diagnosticTask;
            return;
        }

        if (args.Contains("--composition-demo"))
        {
            var durationMs = 4000;
            var durationArg = args.SkipWhile(a => a != "--composition-demo").Skip(1).FirstOrDefault();
            if (int.TryParse(durationArg, out var n) && n > 0) durationMs = n;
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            await CompositionTransformDemoRunner.RunAsync(
                diagnosticOutput ?? Console.Out,
                durationMs,
                ParseDiagnosticScale(args));
            return;
        }

        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));
        window.ExternalRenderingEnabled = true;
        window.Show();

        // D3D12 rendering path
        var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        var clipMode = ParseClipMode(args);
        var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer, clipMode);
        var enablePartialApply = !args.Contains("--no-partial-apply");
        d3d12Renderer.TextCompositionMode = ParseTextCompositionMode(args);
        var displayScale = platformHost.Screens[0].Scale.Normalize();

        Action<double>? maxScrollYCallback = null;
        WindowDrawCommandTranslator? drawCommandTranslator = null;
        var ownerOptions = enablePartialApply
            ? RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled
            : RenderPipelineProductionOwnerOptions.Disabled;
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
                RefreshDebugUiLayoutDiagnosticsAfterFrame(drawCommandTranslator);
            },
            ownerOptions: ownerOptions,
            displayScale: displayScale);
        var handoffOptions = enablePartialApply
            ? DrawingBackendCompositorHandoffOptions.Enabled
            : DrawingBackendCompositorHandoffOptions.Disabled;
        using var d3d12Compositor = new DrawingBackendCompositor(d3d12Backend, handoffOptions);
        d3d12Compositor.SetViewport(window.Region.PhysicalBounds, displayScale);
        var compositor = args.Contains("--console")
            ? new CompositeCompositor(new ConsoleCompositor(Console.Out), d3d12Compositor)
            : (ICompositor)d3d12Compositor;
        Func<RetainedRenderFrameSegmentOwnership?>? ownershipProvider = enablePartialApply
            ? () => drawCommandTranslator?.SegmentOwnership
            : null;
        await using var compositorLoop = new CompositorLoop(drawCommandTranslator, compositor, ownershipProvider);
        var counterApplication = new CounterApplication();
        ConfigureDebugUi(args, counterApplication, window, d3d12Renderer, drawCommandTranslator, displayScale);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(counterApplication, compositorLoop);
        SetDebugUiRuntime(runtime);
        var scrollFramePump = new ScrollFramePump();
        var scrollPresentationCoordinator = new ScrollPresentationCoordinator();
        var inputOwnershipState = new InputOwnershipState();
        SetDebugUiRuntimeSources(scrollFramePump, inputOwnershipState, d3d12Backend.ClipMode);

        // Wire up MaxScrollY feedback after runtime is created
        double? lastKnownMaxScrollY = null;
        maxScrollYCallback = maxScrollY =>
        {
            if (!lastKnownMaxScrollY.HasValue || Math.Abs(maxScrollY - lastKnownMaxScrollY.Value) > 0.5)
            {
                lastKnownMaxScrollY = maxScrollY;
                _ = compositorLoop.CancelCompositionScrollPresentationAsync();
                runtime.Dispatch(new CounterMessage.UpdateMaxScrollY(maxScrollY));
            }
        };

        window.SizeChanged += (w, h) =>
        {
            _ = compositorLoop.CancelCompositionScrollPresentationAsync();
            d3d12Renderer.Resize(w, h);
            d3d12Compositor.SetViewport(new PixelRectangle(0, 0, w, h), displayScale);
            var handledByDebugUi = false;
            RequestDebugUiRenderAfterViewportChange(compositorLoop, ref handledByDebugUi);
            if (!handledByDebugUi)
            {
                _ = compositorLoop.RequestRenderAsync();
            }
        };

        window.DpiChanged += newScale =>
        {
            _ = compositorLoop.CancelCompositionScrollPresentationAsync();
            displayScale = newScale.Normalize();
            drawCommandTranslator.SetDisplayScale(displayScale);
            d3d12Compositor.SetViewport(new PixelRectangle(0, 0, d3d12Renderer.Width, d3d12Renderer.Height), displayScale);
            UpdateDebugUiViewportContext(window, d3d12Renderer, drawCommandTranslator, displayScale);
            var handledByDebugUi = false;
            RequestDebugUiRenderAfterViewportChange(compositorLoop, ref handledByDebugUi);
            if (!handledByDebugUi)
            {
                _ = compositorLoop.RequestRenderAsync();
            }

            Console.WriteLine($"DPI changed: scale={displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        };

        using var inputSubscription = platformHost.RawInputEvents.Subscribe(new PlatformInputObserver(HandleInput));

        platformHost.TopologyChanged += OnTopologyChanged;

        Console.WriteLine($"Detected screens: {platformHost.Screens.Count}");
        Console.WriteLine("Rendering: D3D12 (clear color from FillRect)");
        Console.WriteLine($"Backend clip mode: {d3d12Backend.ClipMode}");
        Console.WriteLine($"Partial apply: {(enablePartialApply ? "ENABLED (default)" : "DISABLED (--no-partial-apply)")}");
        Console.WriteLine($"Text composition mode: {d3d12Renderer.TextCompositionMode}");
        Console.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        Console.WriteLine("Controls: Click buttons, Up/Down = +/-1, R = reset, Mouse wheel = +/-1.");

        await runtime.StartAsync();

        window.RunMessageLoop();

        Console.WriteLine($"Final count: {runtime.CurrentModel.Count}");

        void HandleInput(RawInputEvent inputEvent)
        {
            var hitTestResolver = new DrawingBackendCompositorActionHitTestResolver(d3d12Compositor);
            if (TryMapInputForRuntime(inputEvent, inputOwnershipState, hitTestResolver, out var message))
            {
                if (message is CounterMessage.WheelRaw wheel)
                {
                    var pixels = ScrollController.ConvertToPixels(
                        new ScrollDelta(ScrollDeltaUnit.WheelRaw, wheel.RawDelta),
                        ScrollMetrics.DefaultText,
                        SystemScrollSettings.Default);
                    scrollPresentationCoordinator.AddPendingPixels(pixels);
                    scrollPresentationCoordinator.EnsureRunning(runtime, compositorLoop, drawCommandTranslator, new NodeKey(1));
                }
                else if (message is not null)
                {
                    runtime.Dispatch(message);
                }
            }
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

    private static Task? TryCreateDiagnosticCliTask(string[] args)
    {
        Task? task = null;
        CreateDiagnosticCliTask(args, ref task);
        return task;
    }

    static partial void CreateDiagnosticCliTask(string[] args, ref Task? task);

    static partial void ConfigureDebugUi(
        string[] args,
        CounterApplication application,
        INativeWindow window,
        D3D12Renderer renderer,
        WindowDrawCommandTranslator translator,
        DisplayScale displayScale);

    static partial void SetDebugUiRuntime(Runtime<CounterModel, CounterMessage> runtime);

    static partial void SetDebugUiRuntimeSources(
        ScrollFramePump scrollFramePump,
        InputOwnershipState inputOwnershipState,
        DrawingBackendClipMode backendClipMode);

    static partial void RefreshDebugUiLayoutDiagnosticsAfterFrame(WindowDrawCommandTranslator? translator);

    static partial void UpdateDebugUiViewportContext(
        INativeWindow window,
        D3D12Renderer renderer,
        WindowDrawCommandTranslator translator,
        DisplayScale displayScale);

    static partial void RequestDebugUiRenderAfterViewportChange(CompositorLoop compositorLoop, ref bool handled);

    private static StreamWriter? TryCreateDiagnosticOutput(string[] args)
    {
        var outputPath = args.SkipWhile(a => a != "--diagnostic-output").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(fullPath, false) { AutoFlush = true };
    }

    internal static TextCompositionMode ParseTextCompositionMode(string[] args)
    {
        var value = args.SkipWhile(a => a != "--text-composition").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return TextCompositionMode.GlyphAtlas;
        }

        return value?.ToLowerInvariant() switch
        {
            "glyph-atlas" or "glyphatlas" or "atlas" => TextCompositionMode.GlyphAtlas,
            _ => throw new ArgumentException($"Unsupported text composition mode '{value}'. GlyphAtlas is the only active text composition mode.")
        };
    }

    internal static DrawingBackendClipMode ParseClipMode(string[] args)
    {
        var value = args.SkipWhile(a => a != "--clip-mode").Skip(1).FirstOrDefault();
        return value?.ToLowerInvariant() switch
        {
            "diagnostic" or "diagnostics" => DrawingBackendClipMode.Diagnostic,
            "scissor" => DrawingBackendClipMode.Scissor,
            _ when args.Contains("--disable-scissor") => DrawingBackendClipMode.Diagnostic,
            _ => DrawingBackendClipMode.Scissor
        };
    }

    internal static DisplayScale ParseDiagnosticScale(string[] args)
    {
        var value = args.SkipWhile(a => a != "--diagnose-scale").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        value = value.Trim().TrimEnd('%');
        if (!float.TryParse(value, out var parsed) || parsed <= 0 || !float.IsFinite(parsed))
        {
            return default;
        }

        var scale = parsed > 10 ? parsed / 100f : parsed;
        return new DisplayScale(scale, scale).Normalize();
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

    internal static bool TryMapInputForRuntime<THitTestResolver>(
        RawInputEvent inputEvent,
        InputOwnershipState ownershipState,
        THitTestResolver hitTestResolver,
        out CounterMessage? message)
        where THitTestResolver : struct, IActionHitTestResolver
    {
        var before = ownershipState.Snapshot;
        var mapped = CounterInputRouter.TryMapInput(inputEvent, ownershipState, hitTestResolver, out var mappedMessage);
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

}
