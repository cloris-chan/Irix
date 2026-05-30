using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CompositionRenderTargetCacheDiagnosticRunner
{
    internal static void Run(
        TextWriter output,
        DisplayScale diagnosticScale = default)
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = diagnosticScale == default ? screen.Scale.Normalize() : diagnosticScale.Normalize();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        var viewport = new DrawRect(0, 0, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);

        var resources = FrameDrawingResources.Rent();
        try
        {
            var commands = BuildCommands();
            resources.Seal();
            var compositionFrame = BuildCompositionFrame();

            output.WriteLine("=== D3D12 Composition Render Target Cache Diagnostic ===");
            output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
            output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
            output.WriteLine($"composition-render-target-cache.layer id={compositionFrame.Layer.Id.Value} commandStart={compositionFrame.Layer.CommandStart} commandCount={compositionFrame.Layer.CommandCount} translate=({compositionFrame.Layer.Transform.TranslateX:0.##},{compositionFrame.Layer.Transform.TranslateY:0.##}) opacity={compositionFrame.Layer.Opacity.Normalized:0.##}");

            d3d12Backend.BeginFrame(new FrameContext((int)viewport.Width, (int)viewport.Height, displayScale));
            var first = d3d12Backend.ExecuteCompositionDiagnostic(commands, resources, compositionFrame);
            d3d12Backend.EndFrame();
            var firstSerial = d3d12Backend.FrameSerialDiagnostics;
            output.WriteLine(FormatFirst(first, firstSerial.FrameSerial, firstSerial.PresentSerial, d3d12Renderer.IsDeviceRemoved));

            d3d12Backend.BeginFrame(new FrameContext((int)viewport.Width, (int)viewport.Height, displayScale));
            var second = d3d12Backend.ExecuteCompositionDiagnostic(commands, resources, compositionFrame);
            d3d12Backend.EndFrame();
            var secondSerial = d3d12Backend.FrameSerialDiagnostics;
            output.WriteLine(FormatSecond(second, secondSerial.FrameSerial, secondSerial.PresentSerial, d3d12Renderer.IsDeviceRemoved));
            output.WriteLine("=== d3d12 composition render target cache diagnostic complete ===");
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }

    internal static DrawCommand[] BuildCommands()
    {
        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 640, 360), Color: DrawColor.Opaque(18, 24, 32)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(44, 52, 180, 76), ClipBounds: new DrawRect(32, 40, 360, 160), Color: DrawColor.Opaque(72, 150, 210)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(72, 144, 240, 42), ClipBounds: new DrawRect(32, 40, 360, 160), Color: new DrawColor(224, 235, 190, 90))
        ];
    }

    internal static CompositionFrame BuildCompositionFrame()
    {
        return new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(21),
            CommandStart: 1,
            CommandCount: 2,
            new CompositionTransform(28, 18),
            new CompositionOpacity(0.72f),
            CompositionClipMode.Fixed,
            new DrawRect(24, 32, 420, 220)));
    }

    internal static string FormatFirst(
        D3D12CompositionExecuteDiagnostics diagnostics,
        long frameSerial,
        long presentSerial,
        bool deviceRemoved)
    {
        return Format("first", diagnostics, frameSerial, presentSerial, deviceRemoved);
    }

    internal static string FormatSecond(
        D3D12CompositionExecuteDiagnostics diagnostics,
        long frameSerial,
        long presentSerial,
        bool deviceRemoved)
    {
        return Format("second", diagnostics, frameSerial, presentSerial, deviceRemoved);
    }

    private static string Format(
        string phase,
        D3D12CompositionExecuteDiagnostics diagnostics,
        long frameSerial,
        long presentSerial,
        bool deviceRemoved)
    {
        return $"composition-render-target-cache.{phase} finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} renderTargetBacked={diagnostics.RenderTargetBacked} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} layerCacheHits={diagnostics.LayerCacheHits} layerCacheMisses={diagnostics.LayerCacheMisses} cachedLayerCommands={diagnostics.CachedLayerCommands} renderTargetHits={diagnostics.RenderTargetCacheHits} renderTargetMisses={diagnostics.RenderTargetCacheMisses} cachedRenderTargetCommands={diagnostics.CachedRenderTargetCommands} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands} frameSerial={frameSerial} presentSerial={presentSerial} deviceRemoved={deviceRemoved}";
    }

    private static ScreenRegion CreatePrimaryWindowRegion(IScreenInfo screen)
    {
        const int windowWidth = 640;
        const int windowHeight = 360;
        var bounds = screen.PhysicalBounds;
        var x = bounds.X + Math.Max((bounds.Width - windowWidth) / 2, 0);
        var y = bounds.Y + Math.Max((bounds.Height - windowHeight) / 2, 0);
        return new ScreenRegion(screen.Id, new PixelRectangle(x, y, windowWidth, windowHeight));
    }
}
