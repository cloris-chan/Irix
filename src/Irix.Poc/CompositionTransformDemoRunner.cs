using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CompositionTransformDemoRunner
{
    private const int DefaultDemoDurationMs = 4000;
    private const int AnimationDurationMs = 1600;

    internal static async Task RunAsync(
        TextWriter output,
        int demoDurationMs = DefaultDemoDurationMs,
        DisplayScale diagnosticScale = default,
        CancellationToken cancellationToken = default)
    {
        demoDurationMs = Math.Max(1, demoDurationMs);
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = diagnosticScale == default ? screen.Scale.Normalize() : diagnosticScale.Normalize();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));
        window.ExternalRenderingEnabled = true;
        window.Show();

        var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);
        window.SizeChanged += (width, height) => d3d12Renderer.Resize(width, height);
        window.DpiChanged += scale =>
        {
            displayScale = scale.Normalize();
            compositor.SetViewport(new PixelRectangle(0, 0, d3d12Renderer.Width, d3d12Renderer.Height), displayScale);
        };

        var resources = FrameDrawingResources.Rent();
        try
        {
            var commands = CompositionTransformDiagnosticRunner.BuildCommands(resources);
            resources.Seal();
            compositor.SetViewport(new PixelRectangle(0, 0, d3d12Renderer.Width, d3d12Renderer.Height), displayScale);
            using var staticFrame = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(commands), commands.Length),
                [],
                resources);
            await compositor.RenderAsync(staticFrame, cancellationToken);
            var animationStartTimestamp = CompositionTimestamp.Now();
            var animationPlan = BuildAnimationPlan(commands.Length, animationStartTimestamp);
            compositor.SetCompositionAnimationPlan(animationPlan);

            output.WriteLine("=== D3D12 Composition Transform Demo ===");
            output.WriteLine($"Duration: {demoDurationMs}ms");
            output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
            output.WriteLine($"Initial display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
            output.WriteLine($"Animation clock: Stopwatch, duration: {AnimationDurationMs}ms, repeat: alternate");
            output.WriteLine("Demo model: static retained frame, compositor-owned transform/opacity animation ticks, D3D12-backed presentation.");

            var frameDelayMs = ResolveFrameDelayMilliseconds(screen.RefreshRateHz);
            var demoEndTimestamp = animationStartTimestamp + CompositionDuration.FromMilliseconds(demoDurationMs);
            var lastExecution = default(CompositionBackendExecutionResult);
            var tickIndex = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var timestamp = CompositionTimestamp.Now();
                if (tickIndex > 0 && timestamp >= demoEndTimestamp)
                {
                    break;
                }

                _ = d3d12Renderer.ApplyPendingResize();
                compositor.SetViewport(new PixelRectangle(0, 0, d3d12Renderer.Width, d3d12Renderer.Height), displayScale);
                lastExecution = await compositor.RenderCompositionAnimationTickAsync(cancellationToken);
                tickIndex++;
                if (d3d12Renderer.IsDeviceRemoved)
                {
                    break;
                }

                var remaining = demoEndTimestamp - CompositionTimestamp.Now();
                if (!remaining.IsPositive)
                {
                    break;
                }

                if (frameDelayMs > 0)
                {
                    await Task.Delay(Math.Min(frameDelayMs, remaining.ToPositiveMillisecondsCeiling()), cancellationToken);
                }
            }

            var frameSerial = d3d12Backend.FrameSerialDiagnostics;
            output.WriteLine(FormatDemoSummary(
                lastExecution,
                demoDurationMs,
                compositor.RenderCount,
                compositor.CompositionTickCount,
                frameSerial.FrameSerial,
                frameSerial.PresentSerial,
                frameSerial.SyncWaitCount,
                d3d12Renderer.IsDeviceRemoved));
            output.WriteLine("=== d3d12 composition transform demo complete ===");
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }

    internal static CompositionAnimationPlan BuildAnimationPlan(int commandCount, CompositionTimestamp startTimestamp)
    {
        return BuildAnimationPlan(commandCount, startTimestamp, AnimationDuration);
    }

    internal static CompositionAnimationPlan BuildAnimationPlan(int commandCount, CompositionTimestamp startTimestamp, CompositionDuration duration)
    {
        return new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 1,
            CommandCount: commandCount - 1,
            new CompositionAnimationTimeline(startTimestamp, duration.IsPositive ? duration : CompositionDuration.FromMilliseconds(AnimationDurationMs), CompositionAnimationRepeatMode.Alternate),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(24f, 120f, CompositionAnimationEasing.SineInOut),
                new CompositionScalarAnimation(18f, 42f, CompositionAnimationEasing.SineInOut)),
            new CompositionScalarAnimation(0.95f, 0.55f, CompositionAnimationEasing.SineInOut)));
    }

    internal static CompositionFrame BuildAnimatedCompositionFrameAt(int commandCount, CompositionDuration elapsed)
    {
        return BuildAnimationPlan(commandCount, CompositionTimestamp.Zero).Evaluate(commandCount, CompositionTimestamp.Zero + elapsed);
    }

    internal static string FormatDemoSummary(
        CompositionBackendExecutionResult execution,
        int demoDurationMs,
        long renderCount,
        long compositionTickCount,
        long frameSerial,
        long presentSerial,
        long syncWaits,
        bool deviceRemoved)
    {
        return $"composition.demo finalComposition=D3D12 d3d12Backed={execution.D3D12Backed} layers={execution.LayerCount} commands={execution.CommandCount} translatedCommands={execution.TranslatedCommands} opacityAppliedCommands={execution.OpacityAppliedCommands} clock=Stopwatch demoDurationMs={demoDurationMs} animationDurationMs={AnimationDurationMs} renderCount={renderCount} compositionTicks={compositionTickCount} frameSerial={frameSerial} presentSerial={presentSerial} syncWaits={syncWaits} deviceRemoved={deviceRemoved}";
    }

    private static CompositionDuration AnimationDuration => CompositionDuration.FromMilliseconds(AnimationDurationMs);

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
