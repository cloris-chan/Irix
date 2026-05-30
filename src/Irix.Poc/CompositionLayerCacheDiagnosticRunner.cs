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
        output.WriteLine("=== d3d12 composition layer cache diagnostic complete ===");
    }

    internal static CompositionLayerCacheDiagnostics RunCore(DisplayScale diagnosticScale = default)
    {
        var displayScale = diagnosticScale.Normalize();
        var resources = FrameDrawingResources.Rent();
        try
        {
            var commands = CompositionTransformDiagnosticRunner.BuildCommands(resources);
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

            return new CompositionLayerCacheDiagnostics(first, second);
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }

    internal static string FormatFirst(in D3D12CompositionExecuteDiagnostics diagnostics)
    {
        return $"composition-layer-cache.first finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} cacheHits={diagnostics.LayerCacheHits} cacheMisses={diagnostics.LayerCacheMisses} cachedCommands={diagnostics.CachedLayerCommands} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands}";
    }

    internal static string FormatSecond(in D3D12CompositionExecuteDiagnostics diagnostics)
    {
        return $"composition-layer-cache.second finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} cacheHits={diagnostics.LayerCacheHits} cacheMisses={diagnostics.LayerCacheMisses} cachedCommands={diagnostics.CachedLayerCommands} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands}";
    }
}

internal readonly struct CompositionLayerCacheDiagnostics(
    D3D12CompositionExecuteDiagnostics First,
    D3D12CompositionExecuteDiagnostics Second)
{
    public D3D12CompositionExecuteDiagnostics First { get; } = First;
    public D3D12CompositionExecuteDiagnostics Second { get; } = Second;
}
