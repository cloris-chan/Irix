using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CompositionMultiLayerDiagnosticRunner
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
            var commands = BuildCommands(resources);
            resources.Seal();
            var compositionFrame = BuildCompositionFrame(commands.Length);
            using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
            using var texts = new FrameRenderList<D3D12TextRun>();
            var expected = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
                DrawingBackendClipMode.Scissor,
                viewport,
                commands,
                resources,
                compositionFrame,
                displayScale,
                rects,
                texts);

            output.WriteLine("=== D3D12 Composition MultiLayer Diagnostic ===");
            output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
            output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
            output.WriteLine(FormatExpected(expected, compositionFrame));
            for (var i = 0; i < compositionFrame.LayerCount; i++)
            {
                output.WriteLine(FormatLayer(i, compositionFrame.GetLayer(i)));
            }

            d3d12Backend.BeginFrame(new FrameContext((int)viewport.Width, (int)viewport.Height, displayScale));
            var actual = d3d12Backend.ExecuteCompositionDiagnostic(commands, resources, compositionFrame);
            d3d12Backend.EndFrame();

            var frameSerial = d3d12Backend.FrameSerialDiagnostics;
            output.WriteLine(FormatActual(actual, compositionFrame, frameSerial.FrameSerial, frameSerial.PresentSerial, frameSerial.SyncWaitCount, d3d12Renderer.IsDeviceRemoved));
            output.WriteLine("=== d3d12 composition multilayer diagnostic complete ===");
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }

    internal static DrawCommand[] BuildCommands(FrameDrawingResources resources)
    {
        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 640, 360), Color: DrawColor.Opaque(18, 24, 32)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(32, 48, 180, 80), Color: DrawColor.Opaque(72, 150, 210)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(48, 72, 120, 40), Color: DrawColor.Opaque(245, 248, 255))
        ];
    }

    internal static CompositionFrame BuildCompositionFrame(int commandCount)
    {
        Span<CompositionLayer> layers =
        [
            new CompositionLayer(
                new CompositionLayerId(1),
                CommandStart: 1,
                CommandCount: commandCount - 1,
                new CompositionTransform(24, 18),
                new CompositionOpacity(0.75f)),
            new CompositionLayer(
                new CompositionLayerId(2),
                CommandStart: 2,
                CommandCount: 1,
                new CompositionTransform(0, 36),
                CompositionOpacity.Opaque,
                CompositionClipMode.Fixed,
                new DrawRect(24, 48, 240, 120))
        ];

        return CompositionFrame.FromLayers(layers);
    }

    internal static string FormatExpected(D3D12CompositionExecuteDiagnostics diagnostics, in CompositionFrame compositionFrame)
    {
        return $"composition-multilayer.expected finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} firstLayerStart={diagnostics.LayerCommandStart} firstLayerCommands={diagnostics.LayerCommandCount} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands} fixedClipLayers={CountFixedClipLayers(compositionFrame)}";
    }

    internal static string FormatActual(
        D3D12CompositionExecuteDiagnostics diagnostics,
        in CompositionFrame compositionFrame,
        long frameSerial,
        long presentSerial,
        long syncWaits,
        bool deviceRemoved)
    {
        return $"composition-multilayer.actual finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands} fixedClipLayers={CountFixedClipLayers(compositionFrame)} frameSerial={frameSerial} presentSerial={presentSerial} syncWaits={syncWaits} deviceRemoved={deviceRemoved}";
    }

    private static string FormatLayer(int index, in CompositionLayer layer)
    {
        return $"composition-multilayer.layer index={index} id={layer.Id.Value} commandStart={layer.CommandStart} commandCount={layer.CommandCount} clipMode={layer.ClipMode} clip=({layer.ClipBounds.X:0.##},{layer.ClipBounds.Y:0.##},{layer.ClipBounds.Width:0.##},{layer.ClipBounds.Height:0.##}) translate=({layer.Transform.TranslateX:0.##},{layer.Transform.TranslateY:0.##}) opacity={layer.Opacity.Normalized:0.##}";
    }

    private static int CountFixedClipLayers(in CompositionFrame compositionFrame)
    {
        var count = 0;
        for (var i = 0; i < compositionFrame.LayerCount; i++)
        {
            if (compositionFrame.GetLayer(i).HasFixedClip)
            {
                count++;
            }
        }

        return count;
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
