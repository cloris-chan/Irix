#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static partial class Program
{
    private static bool _debugUiEnabled;
    private static bool _manualDebugLayoutRefresh;
    private static bool _suppressNextDebugLayoutRefresh;
    private static Runtime<CounterModel, CounterMessage>? _debugRuntime;
    private static INativeWindow? _debugWindow;
    private static D3D12Renderer? _debugRenderer;
    private static WindowDrawCommandTranslator? _debugTranslator;
    private static DisplayScale _debugDisplayScale;
    private static ScrollFramePump? _debugScrollFramePump;
    private static InputOwnershipState? _debugInputOwnershipState;
    private static DrawingBackendClipMode _debugBackendClipMode = DrawingBackendClipMode.Scissor;
    private static CounterLayoutDiagnostics _lastDispatchedLayoutDiagnostics = CounterLayoutDiagnostics.Empty;

    static partial void CreateDiagnosticCliTask(string[] args, ref Task? task)
    {
        if (args.Contains("--diagnose"))
        {
            task = RunFullDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-resize"))
        {
            task = RunResizeDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-scroll"))
        {
            task = RunScrollDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-scroll-presentation-policy"))
        {
            task = RunScrollPresentationPolicyDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-scroll-presentation-runtime"))
        {
            task = RunScrollPresentationRuntimeDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-input"))
        {
            task = RunInputDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-sync"))
        {
            task = RunSyncDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-mixed-fallback"))
        {
            task = RunGlyphAtlasMixedFallbackDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-wrap"))
        {
            task = RunGlyphAtlasWrapDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-matrix"))
        {
            task = RunGlyphAtlasMatrixDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-color-formats"))
        {
            task = RunGlyphAtlasColorFormatDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-bidi-oracle"))
        {
            task = RunGlyphAtlasBidiOracleDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-glyph-oracle"))
        {
            task = RunGlyphAtlasGlyphOracleDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-stress"))
        {
            task = RunGlyphAtlasStressDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-glyph-atlas-soak"))
        {
            task = RunGlyphAtlasSoakDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-text-cache"))
        {
            task = RunTextCacheDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-composition-transform"))
        {
            task = RunCompositionTransformDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-composition-scroll"))
        {
            task = RunCompositionScrollDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-composition-multilayer"))
        {
            task = RunCompositionMultiLayerDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-composition-layer-cache"))
        {
            task = RunCompositionLayerCacheDiagnosticsAsync(args);
            return;
        }

        if (args.Contains("--diagnose-composition-marker-runtime"))
        {
            task = RunCompositionMarkerRuntimeDiagnosticsAsync(args);
        }
    }

    static partial void ConfigureDebugUi(
        string[] args,
        CounterApplication application,
        INativeWindow window,
        D3D12Renderer renderer,
        WindowDrawCommandTranslator translator,
        DisplayScale displayScale)
    {
        _debugUiEnabled = args.Contains("--debug-ui");
        if (!_debugUiEnabled)
        {
            return;
        }

        UpdateDebugUiViewportContext(window, renderer, translator, displayScale);
        application.ConfigureDiagnostics(true, CreateViewportDiagnostics(), CounterLayoutDiagnostics.Empty);
    }

    static partial void SetDebugUiRuntime(Runtime<CounterModel, CounterMessage> runtime)
    {
        _debugRuntime = runtime;
    }

    static partial void SetDebugUiRuntimeSources(
        ScrollFramePump scrollFramePump,
        InputOwnershipState inputOwnershipState,
        DrawingBackendClipMode backendClipMode)
    {
        _debugScrollFramePump = scrollFramePump;
        _debugInputOwnershipState = inputOwnershipState;
        _debugBackendClipMode = backendClipMode;
    }

    static partial void RefreshDebugUiLayoutDiagnosticsAfterFrame(WindowDrawCommandTranslator? translator)
    {
        if (!_debugUiEnabled || _manualDebugLayoutRefresh || translator is null)
        {
            return;
        }

        DispatchLayoutDiagnostics(CreateLayoutDiagnostics(translator));
    }

    static partial void UpdateDebugUiViewportContext(
        INativeWindow window,
        D3D12Renderer renderer,
        WindowDrawCommandTranslator translator,
        DisplayScale displayScale)
    {
        _debugWindow = window;
        _debugRenderer = renderer;
        _debugTranslator = translator;
        _debugDisplayScale = displayScale.Normalize();
    }

    static partial void RequestDebugUiRenderAfterViewportChange(CompositorLoop compositorLoop, ref bool handled)
    {
        if (!_debugUiEnabled)
        {
            return;
        }

        handled = true;
        _ = RequestResizeRenderAndRefreshDiagnosticsAsync(compositorLoop);
    }

    internal static double DiagPendingPx => _debugScrollFramePump?.PendingPixels ?? 0;
    internal static bool DiagScrollFrameQueued => _debugScrollFramePump?.IsFrameQueued ?? false;
    internal static bool DiagTickLoopRunning => _debugScrollFramePump?.IsLoopRunning ?? false;
    internal static long DiagScrollDispatchedFrameCount => _debugScrollFramePump?.DispatchedFrameCount ?? 0;
    internal static double DiagScrollRenderWaitMs => _debugScrollFramePump?.RenderWaitMs ?? 0;
    internal static double DiagScrollLastDt => _debugScrollFramePump?.LastDt ?? 0;
    internal static double DiagScrollDrainedPixels => _debugScrollFramePump?.DrainedPixels ?? 0;
    internal static OwnershipSnapshot DiagInputOwnership => _debugInputOwnershipState?.Snapshot ?? default;
    internal static DrawingBackendClipMode DiagBackendClipMode => _debugBackendClipMode;

    private static async Task RequestResizeRenderAndRefreshDiagnosticsAsync(CompositorLoop compositorLoop)
    {
        try
        {
            _manualDebugLayoutRefresh = true;
            await compositorLoop.RequestRenderAndWaitAsync();
            _manualDebugLayoutRefresh = false;
            if (_debugWindow is not null && _debugRenderer is not null && _debugTranslator is not null)
            {
                DispatchDebugDiagnostics(CreateViewportDiagnostics(), CreateLayoutDiagnostics(_debugTranslator));
            }
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _manualDebugLayoutRefresh = false;
        }
    }

    private static void DispatchLayoutDiagnostics(CounterLayoutDiagnostics diagnostics)
    {
        if (!_debugUiEnabled || _debugRuntime is null)
        {
            return;
        }

        if (_suppressNextDebugLayoutRefresh)
        {
            _suppressNextDebugLayoutRefresh = false;
            return;
        }

        if (diagnostics == _lastDispatchedLayoutDiagnostics)
        {
            return;
        }

        _lastDispatchedLayoutDiagnostics = diagnostics;
        _suppressNextDebugLayoutRefresh = true;
        _debugRuntime.Dispatch(new CounterMessage.LayoutDiagnosticsChanged(diagnostics));
    }

    private static void DispatchDebugDiagnostics(CounterViewportDiagnostics viewportDiagnostics, CounterLayoutDiagnostics layoutDiagnostics)
    {
        if (!_debugUiEnabled || _debugRuntime is null)
        {
            return;
        }

        _lastDispatchedLayoutDiagnostics = layoutDiagnostics;
        _suppressNextDebugLayoutRefresh = true;
        _debugRuntime.Dispatch(new CounterMessage.DebugDiagnosticsChanged(viewportDiagnostics, layoutDiagnostics));
    }

    private static CounterViewportDiagnostics CreateViewportDiagnostics()
    {
        if (_debugWindow is null || _debugRenderer is null)
        {
            return default;
        }

        var bounds = _debugWindow.Region.PhysicalBounds;
        var rendererViewport = new PixelRectangle(bounds.X, bounds.Y, _debugRenderer.Width, _debugRenderer.Height);
        var layoutViewport = _debugTranslator?.LastLayoutViewport ?? rendererViewport;
        if (layoutViewport.Width <= 0 || layoutViewport.Height <= 0)
        {
            layoutViewport = rendererViewport;
        }

        var logicalViewport = _debugDisplayScale.IsIdentity
            ? rendererViewport
            : new PixelRectangle(0, 0, (int)(rendererViewport.Width / _debugDisplayScale.ScaleX), (int)(rendererViewport.Height / _debugDisplayScale.ScaleY));

        return new CounterViewportDiagnostics(rendererViewport, layoutViewport, ViewportScaleMode.PhysicalPixelsV0, _debugDisplayScale, logicalViewport);
    }

    private static CounterLayoutDiagnostics CreateLayoutDiagnostics(WindowDrawCommandTranslator translator)
    {
        return new CounterLayoutDiagnostics(translator.LayoutRebuildCount, translator.LastLayoutRebuildReason, translator.LastDirtyClassifications);
    }

    private static Task RunFullDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        FullDiagnosticRunner.Run(diagnosticOutput ?? Console.Out);
        return Task.CompletedTask;
    }

    private static Task RunResizeDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        ResizeDiagnosticRunner.Run(
            diagnosticOutput ?? Console.Out,
            ParseTextCompositionMode(args),
            ParseDiagnosticScale(args));
        return Task.CompletedTask;
    }

    private static async Task RunScrollDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        await ScrollDiagnosticRunner.RunAsync(diagnosticOutput ?? Console.Out, Path.Combine("TestResults", "diagnose-scroll.txt"));
    }

    private static Task RunScrollPresentationPolicyDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        ScrollPresentationPolicyDiagnosticRunner.Run(diagnosticOutput ?? Console.Out);
        return Task.CompletedTask;
    }

    private static async Task RunScrollPresentationRuntimeDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        await ScrollPresentationRuntimeDiagnosticRunner.RunAsync(diagnosticOutput ?? Console.Out);
    }

    private static async Task RunInputDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        await InputDiagnosticRunner.RunAsync(diagnosticOutput ?? Console.Out, Path.Combine("TestResults", "diagnose-input.txt"));
    }

    private static Task RunSyncDiagnosticsAsync(string[] args)
    {
        var frameCount = 300;
        var frameArg = args.SkipWhile(a => a != "--diagnose-sync").Skip(1).FirstOrDefault();
        if (int.TryParse(frameArg, out var n) && n > 0) frameCount = n;
        var sampleCount = 1;
        var sampleArg = args.SkipWhile(a => a != "--diagnose-sync").Skip(2).FirstOrDefault();
        if (int.TryParse(sampleArg, out var samples) && samples > 0) sampleCount = samples;
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        SyncDiagnosticRunner.Run(
            diagnosticOutput ?? Console.Out,
            frameCount,
            sampleCount,
            ParseTextCompositionMode(args),
            args.Contains("--diagnose-sync-non-ascii"));
        return Task.CompletedTask;
    }

    private static Task RunGlyphAtlasMixedFallbackDiagnosticsAsync(string[] args)
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
        return Task.CompletedTask;
    }

    private static Task RunGlyphAtlasWrapDiagnosticsAsync(string[] args)
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
        return Task.CompletedTask;
    }

    private static Task RunGlyphAtlasMatrixDiagnosticsAsync(string[] args)
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
        return Task.CompletedTask;
    }

    private static Task RunGlyphAtlasColorFormatDiagnosticsAsync(string[] args)
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
        return Task.CompletedTask;
    }

    private static Task RunGlyphAtlasBidiOracleDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        GlyphAtlasBidiOracleDiagnosticRunner.Run(diagnosticOutput ?? Console.Out);
        return Task.CompletedTask;
    }

    private static Task RunGlyphAtlasGlyphOracleDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        GlyphAtlasGlyphOracleDiagnosticRunner.Run(diagnosticOutput ?? Console.Out);
        return Task.CompletedTask;
    }

    private static Task RunGlyphAtlasStressDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        GlyphAtlasStressDiagnosticRunner.Run(
            diagnosticOutput ?? Console.Out,
            args.Contains("--mixed-fallback"),
            args.Contains("--reuse-page"));
        return Task.CompletedTask;
    }

    private static Task RunGlyphAtlasSoakDiagnosticsAsync(string[] args)
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
        return Task.CompletedTask;
    }

    private static Task RunTextCacheDiagnosticsAsync(string[] args)
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
        return Task.CompletedTask;
    }

    private static Task RunCompositionTransformDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        CompositionTransformDiagnosticRunner.Run(
            diagnosticOutput ?? Console.Out,
            ParseDiagnosticScale(args));
        return Task.CompletedTask;
    }

    private static Task RunCompositionScrollDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        CompositionScrollDiagnosticRunner.Run(
            diagnosticOutput ?? Console.Out,
            ParseDiagnosticScale(args));
        return Task.CompletedTask;
    }

    private static Task RunCompositionMultiLayerDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        CompositionMultiLayerDiagnosticRunner.Run(
            diagnosticOutput ?? Console.Out,
            ParseDiagnosticScale(args));
        return Task.CompletedTask;
    }

    private static Task RunCompositionLayerCacheDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        CompositionLayerCacheDiagnosticRunner.Run(
            diagnosticOutput ?? Console.Out,
            ParseDiagnosticScale(args));
        return Task.CompletedTask;
    }

    private static async Task RunCompositionMarkerRuntimeDiagnosticsAsync(string[] args)
    {
        using var diagnosticOutput = TryCreateDiagnosticOutput(args);
        await CompositionMarkerRuntimeDiagnosticRunner.RunAsync(diagnosticOutput ?? Console.Out);
    }
}
#endif
