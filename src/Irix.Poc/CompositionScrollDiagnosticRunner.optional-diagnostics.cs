#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CompositionScrollDiagnosticRunner
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

            output.WriteLine("=== D3D12 Composition Scroll Diagnostic ===");
            output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
            output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
            output.WriteLine(FormatExpected(expected));
            output.WriteLine(FormatLayer(compositionFrame.Layer));

            d3d12Backend.BeginFrame(new FrameContext((int)viewport.Width, (int)viewport.Height, displayScale));
            var actual = d3d12Backend.ExecuteCompositionDiagnostic(commands, resources, compositionFrame);
            d3d12Backend.EndFrame();

            var frameSerial = d3d12Backend.FrameSerialDiagnostics;
            output.WriteLine(FormatActual(actual, frameSerial.FrameSerial, frameSerial.PresentSerial, frameSerial.SyncWaitCount, d3d12Renderer.IsDeviceRemoved));
            output.WriteLine("=== d3d12 composition scroll diagnostic complete ===");
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }

    internal static DrawCommand[] BuildCommands(FrameDrawingResources resources)
    {
        var style = resources.AddTextStyle(new TextStyle(
            TextFontFamily.SegoeUi,
            18,
            TextFontWeight.Normal,
            TextFontStyle.Normal,
            TextFontStretch.Normal,
            TextHorizontalAlignment.Leading,
            TextVerticalAlignment.Top,
            TextWrapping.NoWrap));
        var text = resources.AddText("scroll presentation");

        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 640, 360), Color: DrawColor.Opaque(18, 24, 32)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(32, -24, 180, 80), ClipBounds: new DrawRect(24, 24, 280, 96), Color: DrawColor.Opaque(72, 150, 210)),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(48, -4, 220, 32), Resource: style, Text: text, ClipBounds: new DrawRect(24, 24, 280, 96), Color: DrawColor.Opaque(245, 248, 255))
        ];
    }

    internal static CompositionFrame BuildCompositionFrame(int commandCount)
    {
        return new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(1),
            CommandStart: 1,
            CommandCount: commandCount - 1,
            new CompositionTransform(0, 42),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(24, 24, 280, 96)));
    }

    internal static string FormatExpected(D3D12CompositionExecuteDiagnostics diagnostics)
    {
        var layer = diagnostics.ExecuteResult.FillRectDiagnostics.LastEffectiveScissor.Bounds;
        return $"composition-scroll.expected finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} layerStart={diagnostics.LayerCommandStart} layerCommands={diagnostics.LayerCommandCount} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands} fixedClip=True clip=({layer.X:0.##},{layer.Y:0.##},{layer.Width:0.##},{layer.Height:0.##}) translate=({diagnostics.AppliedTransform.TranslateX:0.##},{diagnostics.AppliedTransform.TranslateY:0.##})";
    }

    internal static string FormatActual(
        D3D12CompositionExecuteDiagnostics diagnostics,
        long frameSerial,
        long presentSerial,
        long syncWaits,
        bool deviceRemoved)
    {
        return $"composition-scroll.actual finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands} fixedClip=True frameSerial={frameSerial} presentSerial={presentSerial} syncWaits={syncWaits} deviceRemoved={deviceRemoved}";
    }

    private static string FormatLayer(CompositionLayer layer)
    {
        return $"composition-scroll.layer id={layer.Id.Value} commandStart={layer.CommandStart} commandCount={layer.CommandCount} clipMode={layer.ClipMode} clip=({layer.ClipBounds.X:0.##},{layer.ClipBounds.Y:0.##},{layer.ClipBounds.Width:0.##},{layer.ClipBounds.Height:0.##}) translate=({layer.Transform.TranslateX:0.##},{layer.Transform.TranslateY:0.##}) opacity={layer.Opacity.Normalized:0.##}";
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
#endif
