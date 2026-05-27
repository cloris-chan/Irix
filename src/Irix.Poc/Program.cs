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
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            FullDiagnosticRunner.Run(diagnosticOutput ?? Console.Out);
            return;
        }

        if (args.Contains("--diagnose-resize"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            ResizeDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                ParseTextCompositionMode(args),
                ParseDiagnosticScale(args));
            return;
        }

        if (args.Contains("--diagnose-scroll"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            await ScrollDiagnosticRunner.RunAsync(diagnosticOutput ?? Console.Out, Path.Combine("TestResults", "diagnose-scroll.txt"));
            return;
        }

        if (args.Contains("--diagnose-input"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            await InputDiagnosticRunner.RunAsync(diagnosticOutput ?? Console.Out, Path.Combine("TestResults", "diagnose-input.txt"));
            return;
        }

        if (args.Contains("--diagnose-sync"))
        {
            var syncFrameCount = 300;
            var syncFrameArg = args.SkipWhile(a => a != "--diagnose-sync").Skip(1).FirstOrDefault();
            if (int.TryParse(syncFrameArg, out var n) && n > 0) syncFrameCount = n;
            var syncSampleCount = 1;
            var syncSampleArg = args.SkipWhile(a => a != "--diagnose-sync").Skip(2).FirstOrDefault();
            if (int.TryParse(syncSampleArg, out var sampleCount) && sampleCount > 0) syncSampleCount = sampleCount;
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            SyncDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                syncFrameCount,
                syncSampleCount,
                ParseTextCompositionMode(args),
                args.Contains("--diagnose-sync-non-ascii"));
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-mixed-fallback"))
        {
            var frameCount = 30;
            var frameArg = args.SkipWhile(a => a != "--diagnose-glyph-atlas-mixed-fallback").Skip(1).FirstOrDefault();
            if (int.TryParse(frameArg, out var n) && n > 0) frameCount = n;
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            GlyphAtlasMixedFallbackDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                frameCount,
                ParseTextCompositionMode(args),
                ParseDiagnosticScale(args));
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-wrap"))
        {
            var frameCount = 30;
            var frameArg = args.SkipWhile(a => a != "--diagnose-glyph-atlas-wrap").Skip(1).FirstOrDefault();
            if (int.TryParse(frameArg, out var n) && n > 0) frameCount = n;
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            GlyphAtlasWrapDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                frameCount,
                ParseTextCompositionMode(args),
                ParseDiagnosticScale(args));
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-matrix"))
        {
            var frameCount = 3;
            var frameArg = args.SkipWhile(a => a != "--diagnose-glyph-atlas-matrix").Skip(1).FirstOrDefault();
            if (int.TryParse(frameArg, out var n) && n > 0) frameCount = n;
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            GlyphAtlasRegressionMatrixDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                frameCount,
                ParseTextCompositionMode(args),
                ParseDiagnosticScale(args));
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-color-formats"))
        {
            var pixelsPerEm = 64u;
            var pixelsPerEmArg = args.SkipWhile(a => a != "--diagnose-glyph-atlas-color-formats").Skip(1).FirstOrDefault();
            if (uint.TryParse(pixelsPerEmArg, out var n) && n > 0) pixelsPerEm = n;
            var familyName = args.SkipWhile(a => a != "--diagnose-color-glyph-family").Skip(1).FirstOrDefault();
            var fontFilePath = args.SkipWhile(a => a != "--diagnose-color-glyph-font-file").Skip(1).FirstOrDefault();
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            GlyphAtlasColorFormatDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                string.IsNullOrWhiteSpace(familyName) ? "Segoe UI Emoji" : familyName,
                pixelsPerEm,
                fontFilePath);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-bidi-oracle"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            GlyphAtlasBidiOracleDiagnosticRunner.Run(diagnosticOutput ?? Console.Out);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-glyph-oracle"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            GlyphAtlasGlyphOracleDiagnosticRunner.Run(diagnosticOutput ?? Console.Out);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-stress"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            GlyphAtlasStressDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                args.Contains("--mixed-fallback"),
                args.Contains("--reuse-page"));
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-soak"))
        {
            var frameCount = 60;
            var frameArg = args.SkipWhile(a => a != "--diagnose-glyph-atlas-soak").Skip(1).FirstOrDefault();
            if (int.TryParse(frameArg, out var n) && n > 0) frameCount = n;
            var pressureEvery = 6;
            var pressureArg = args.SkipWhile(a => a != "--pressure-every").Skip(1).FirstOrDefault();
            if (int.TryParse(pressureArg, out var cadence) && cadence > 0) pressureEvery = cadence;
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            GlyphAtlasSoakDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                frameCount,
                pressureEvery,
                ParseTextCompositionMode(args),
                ParseDiagnosticScale(args));
            return;
        }

        if (args.Contains("--diagnose-text-cache"))
        {
            var frameCount = 180;
            var frameArg = args.SkipWhile(a => a != "--diagnose-text-cache").Skip(1).FirstOrDefault();
            if (int.TryParse(frameArg, out var n) && n > 0) frameCount = n;
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            TextCacheAllocationDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                frameCount,
                ParseTextCompositionMode(args),
                ParseDiagnosticScale(args));
            return;
        }

        if (args.Contains("--diagnose-composition-transform"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            CompositionTransformDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                ParseDiagnosticScale(args));
            return;
        }

        if (args.Contains("--diagnose-composition-scroll"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            CompositionScrollDiagnosticRunner.Run(
                diagnosticOutput ?? Console.Out,
                ParseDiagnosticScale(args));
            return;
        }

        if (args.Contains("--diagnose-composition-marker-runtime"))
        {
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            await CompositionMarkerRuntimeDiagnosticRunner.RunAsync(
                diagnosticOutput ?? Console.Out);
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
        _backendClipMode = d3d12Backend.ClipMode;
        var showDiagnostics = args.Contains("--debug-ui");
        var enablePartialApply = !args.Contains("--no-partial-apply");
        d3d12Renderer.TextCompositionMode = ParseTextCompositionMode(args);
        var displayScale = platformHost.Screens[0].Scale.Normalize();

        Action<double>? maxScrollYCallback = null;
        Action<CounterLayoutDiagnostics>? layoutDiagnosticsCallback = null;
        var manualLayoutDiagnosticsRefresh = false;
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
                if (showDiagnostics && !manualLayoutDiagnosticsRefresh && drawCommandTranslator is not null)
                {
                    layoutDiagnosticsCallback?.Invoke(CreateLayoutDiagnostics(drawCommandTranslator));
                }
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
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(showDiagnostics, CreateViewportDiagnostics(window, d3d12Renderer, drawCommandTranslator, displayScale), CounterLayoutDiagnostics.Empty), compositorLoop);
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
            d3d12Compositor.SetViewport(new PixelRectangle(0, 0, w, h), displayScale);
            if (showDiagnostics)
            {
                _ = RequestResizeRenderAndRefreshDiagnosticsAsync();
            }
            else
            {
                _ = compositorLoop.RequestRenderAsync();
            }
        };

        window.DpiChanged += newScale =>
        {
            displayScale = newScale.Normalize();
            drawCommandTranslator.SetDisplayScale(displayScale);
            d3d12Compositor.SetViewport(new PixelRectangle(0, 0, d3d12Renderer.Width, d3d12Renderer.Height), displayScale);
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
                DispatchDebugDiagnostics(CreateViewportDiagnostics(window, d3d12Renderer, drawCommandTranslator, displayScale), CreateLayoutDiagnostics(drawCommandTranslator));
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

    private static CounterViewportDiagnostics CreateViewportDiagnostics(INativeWindow window, D3D12Renderer renderer, WindowDrawCommandTranslator? translator, DisplayScale displayScale = default)
    {
        displayScale = displayScale.Normalize();
        var bounds = window.Region.PhysicalBounds;
        var rendererViewport = new PixelRectangle(bounds.X, bounds.Y, renderer.Width, renderer.Height);
        var layoutViewport = translator?.LastLayoutViewport ?? rendererViewport;
        if (layoutViewport.Width <= 0 || layoutViewport.Height <= 0)
        {
            layoutViewport = rendererViewport;
        }

        var logicalViewport = displayScale.IsIdentity
            ? rendererViewport
            : new PixelRectangle(0, 0, (int)(rendererViewport.Width / displayScale.ScaleX), (int)(rendererViewport.Height / displayScale.ScaleY));

        return new CounterViewportDiagnostics(rendererViewport, layoutViewport, ViewportScaleMode.PhysicalPixelsV0, displayScale, logicalViewport);
    }

    private static CounterLayoutDiagnostics CreateLayoutDiagnostics(WindowDrawCommandTranslator translator)
    {
        return new CounterLayoutDiagnostics(translator.LayoutRebuildCount, translator.LastLayoutRebuildReason, translator.LastDirtyClassifications);
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

    private static ScrollFramePump? _scrollFramePump;
    private static InputOwnershipState? _inputOwnershipState;
    private static DrawingBackendClipMode _backendClipMode = DrawingBackendClipMode.Scissor;

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
