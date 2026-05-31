#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CompositionLayerCacheDiagnosticRunner
{
    internal static void Run(TextWriter output, DisplayScale diagnosticScale = default)
    {
        var diagnostics = RunCore(diagnosticScale);
        output.WriteLine("=== D3D12 Composition Layer Cache Diagnostic ===");
        output.WriteLine(FormatFirst(diagnostics.First));
        output.WriteLine(FormatSecond(diagnostics.Second));
        output.WriteLine(FormatScaleChanged(diagnostics.ScaleChanged));
        output.WriteLine(FormatResourceChanged(diagnostics.ResourceChanged));
        output.WriteLine(FormatResourceFrameReset(diagnostics.ResourceFrameReset));
        output.WriteLine("=== d3d12 composition layer cache diagnostic complete ===");
    }

    internal static CompositionLayerCacheDiagnostics RunCore(DisplayScale diagnosticScale = default)
    {
        var displayScale = diagnosticScale.Normalize();
        using var resources = new FrameDrawingResources();
        var commands = BuildCommands(resources, fontSize: 18);
        resources.Seal();
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        var cache = new D3D12CompositionLayerContentCache();
        var firstFrame = CompositionTransformDiagnosticRunner.BuildCompositionFrame(commands.Length);
        var first = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            commands,
            resources,
            firstFrame,
            displayScale,
            rects,
            texts,
            cache);

        rects.Reset();
        texts.Reset();
        var secondFrame = new CompositionFrame(new CompositionLayer(
            firstFrame.Layer.Id,
            firstFrame.Layer.CommandStart,
            firstFrame.Layer.CommandCount,
            new CompositionTransform(48, 36),
            new CompositionOpacity(0.5f)));
        var second = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            commands,
            resources,
            secondFrame,
            displayScale,
            rects,
            texts,
            cache);

        rects.Reset();
        texts.Reset();
        var changedScale = new DisplayScale(displayScale.ScaleX + 1f, displayScale.ScaleY + 1f).Normalize();
        var scaleChanged = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            commands,
            resources,
            secondFrame,
            changedScale,
            rects,
            texts,
            cache);

        using var changedResources = new FrameDrawingResources();
        var changedCommands = BuildCommands(changedResources, fontSize: 24);
        changedResources.Seal();
        rects.Reset();
        texts.Reset();
        var resourceChanged = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            changedCommands,
            changedResources,
            secondFrame,
            displayScale,
            rects,
            texts,
            cache);

        using var frameResetResources = new FrameDrawingResources();
        var frameResetCommands = BuildFrameResetCommands();
        frameResetResources.Seal();
        var frameResetFrame = CompositionTransformDiagnosticRunner.BuildCompositionFrame(frameResetCommands.Length);
        var frameResetCache = new D3D12CompositionLayerContentCache();
        rects.Reset();
        texts.Reset();
        _ = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            frameResetCommands,
            frameResetResources,
            frameResetFrame,
            displayScale,
            rects,
            texts,
            frameResetCache);

        frameResetResources.Reset();
        frameResetResources.Seal();
        rects.Reset();
        texts.Reset();
        var resourceFrameReset = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            frameResetCommands,
            frameResetResources,
            frameResetFrame,
            displayScale,
            rects,
            texts,
            frameResetCache);

        return new CompositionLayerCacheDiagnostics(first, second, scaleChanged, resourceChanged, resourceFrameReset);
    }

    internal static string FormatFirst(in D3D12CompositionExecuteDiagnostics diagnostics)
    {
        return $"composition-layer-cache.first finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} cacheHits={diagnostics.LayerCacheHits} cacheMisses={diagnostics.LayerCacheMisses} cachedCommands={diagnostics.CachedLayerCommands} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands}";
    }

    internal static string FormatSecond(in D3D12CompositionExecuteDiagnostics diagnostics)
    {
        return $"composition-layer-cache.second finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} cacheHits={diagnostics.LayerCacheHits} cacheMisses={diagnostics.LayerCacheMisses} cachedCommands={diagnostics.CachedLayerCommands} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands}";
    }

    internal static string FormatScaleChanged(in D3D12CompositionExecuteDiagnostics diagnostics)
    {
        return $"composition-layer-cache.scaleChanged finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} cacheHits={diagnostics.LayerCacheHits} cacheMisses={diagnostics.LayerCacheMisses} cachedCommands={diagnostics.CachedLayerCommands} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands}";
    }

    internal static string FormatResourceChanged(in D3D12CompositionExecuteDiagnostics diagnostics)
    {
        return $"composition-layer-cache.resourceChanged finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} cacheHits={diagnostics.LayerCacheHits} cacheMisses={diagnostics.LayerCacheMisses} cachedCommands={diagnostics.CachedLayerCommands} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands}";
    }

    internal static string FormatResourceFrameReset(in D3D12CompositionExecuteDiagnostics diagnostics)
    {
        return $"composition-layer-cache.resourceFrameReset finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} cacheHits={diagnostics.LayerCacheHits} cacheMisses={diagnostics.LayerCacheMisses} cachedCommands={diagnostics.CachedLayerCommands} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands}";
    }

    private static DrawCommand[] BuildCommands(FrameDrawingResources resources, float fontSize)
    {
        var style = resources.AddTextStyle(new TextStyle(
            TextFontFamily.SegoeUi,
            fontSize,
            TextFontWeight.Normal,
            TextFontStyle.Normal,
            TextFontStretch.Normal,
            TextHorizontalAlignment.Leading,
            TextVerticalAlignment.Top,
            TextWrapping.NoWrap));
        var text = resources.AddText("composition d3d12");

        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 640, 360), Color: DrawColor.Opaque(18, 24, 32)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(32, 48, 180, 80), Color: DrawColor.Opaque(72, 150, 210)),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(48, 72, 220, 32), Resource: style, Text: text, Color: DrawColor.Opaque(245, 248, 255))
        ];
    }

    private static DrawCommand[] BuildFrameResetCommands()
    {
        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 640, 360), Color: DrawColor.Opaque(18, 24, 32)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(32, 48, 180, 80), Color: DrawColor.Opaque(72, 150, 210))
        ];
    }
}

internal readonly struct CompositionLayerCacheDiagnostics(
    D3D12CompositionExecuteDiagnostics First,
    D3D12CompositionExecuteDiagnostics Second,
    D3D12CompositionExecuteDiagnostics ScaleChanged,
    D3D12CompositionExecuteDiagnostics ResourceChanged,
    D3D12CompositionExecuteDiagnostics ResourceFrameReset)
{
    public D3D12CompositionExecuteDiagnostics First { get; } = First;
    public D3D12CompositionExecuteDiagnostics Second { get; } = Second;
    public D3D12CompositionExecuteDiagnostics ScaleChanged { get; } = ScaleChanged;
    public D3D12CompositionExecuteDiagnostics ResourceChanged { get; } = ResourceChanged;
    public D3D12CompositionExecuteDiagnostics ResourceFrameReset { get; } = ResourceFrameReset;
}
#endif
