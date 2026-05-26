using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CompositionTransformDemoRunner
{
    private const int DefaultFrameCount = 240;

    internal static async Task RunAsync(
        TextWriter output,
        int frameCount = DefaultFrameCount,
        DisplayScale diagnosticScale = default,
        CancellationToken cancellationToken = default)
    {
        frameCount = Math.Max(1, frameCount);
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = diagnosticScale == default ? screen.Scale.Normalize() : diagnosticScale.Normalize();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));
        window.ExternalRenderingEnabled = true;
        window.Show();

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        window.SizeChanged += (width, height) => d3d12Renderer.Resize(width, height);
        window.DpiChanged += scale => displayScale = scale.Normalize();

        var resources = FrameDrawingResources.Rent();
        try
        {
            var commands = CompositionTransformDiagnosticRunner.BuildCommands(resources);
            resources.Seal();
            output.WriteLine("=== D3D12 Composition Transform Demo ===");
            output.WriteLine($"Frames: {frameCount}");
            output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
            output.WriteLine($"Initial display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
            output.WriteLine("Demo model: static draw commands, per-frame CompositionFrame transform/opacity updates, D3D12-backed presentation.");

            var frameDelayMs = ResolveFrameDelayMilliseconds(screen.RefreshRateHz);
            var lastDiagnostics = default(D3D12CompositionExecuteDiagnostics);
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = d3d12Renderer.ApplyPendingResize();
                var compositionFrame = BuildAnimatedCompositionFrame(commands.Length, frameIndex, frameCount);
                d3d12Backend.BeginFrame(new FrameContext(d3d12Renderer.Width, d3d12Renderer.Height, displayScale, frameIndex));
                lastDiagnostics = d3d12Backend.ExecuteCompositionDiagnostic(commands, resources, compositionFrame);
                d3d12Backend.EndFrame();
                if (d3d12Renderer.IsDeviceRemoved)
                {
                    break;
                }

                if (frameDelayMs > 0)
                {
                    await Task.Delay(frameDelayMs, cancellationToken);
                }
            }

            var frameSerial = d3d12Backend.FrameSerialDiagnostics;
            output.WriteLine(FormatDemoSummary(lastDiagnostics, frameSerial.FrameSerial, frameSerial.PresentSerial, frameSerial.SyncWaitCount, d3d12Renderer.IsDeviceRemoved));
            output.WriteLine("=== d3d12 composition transform demo complete ===");
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }

    internal static CompositionFrame BuildAnimatedCompositionFrame(int commandCount, int frameIndex, int frameCount)
    {
        frameCount = Math.Max(1, frameCount);
        var t = frameCount == 1 ? 1f : frameIndex / (float)(frameCount - 1);
        var wave = MathF.Sin(t * MathF.Tau);
        var x = 24f + wave * 96f;
        var y = 18f + MathF.Sin(t * MathF.Tau * 2f) * 24f;
        var opacity = 0.55f + (MathF.Sin(t * MathF.Tau + MathF.PI * 0.5f) + 1f) * 0.2f;

        return new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(1),
            CommandStart: 1,
            CommandCount: commandCount - 1,
            new CompositionTransform(x, y),
            new CompositionOpacity(opacity)));
    }

    internal static string FormatDemoSummary(
        D3D12CompositionExecuteDiagnostics diagnostics,
        long frameSerial,
        long presentSerial,
        long syncWaits,
        bool deviceRemoved)
    {
        return $"composition.demo finalComposition=D3D12 d3d12Backed={diagnostics.D3D12Backed} layers={diagnostics.LayerCount} commands={diagnostics.CommandCount} translatedCommands={diagnostics.TranslatedCommands} opacityAppliedCommands={diagnostics.OpacityAppliedCommands} frameSerial={frameSerial} presentSerial={presentSerial} syncWaits={syncWaits} deviceRemoved={deviceRemoved}";
    }

    private static int ResolveFrameDelayMilliseconds(int refreshRateHz)
    {
        return refreshRateHz > 0 ? Math.Max(1, (int)MathF.Round(1000f / refreshRateHz)) : 16;
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
