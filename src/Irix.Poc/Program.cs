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
            RunDiagnosticMode();
            return;
        }

        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));

        // D3D12 rendering path
        var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        window.SizeChanged += (w, h) => d3d12Renderer.Resize(w, h);
        var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        var d3d12Compositor = new DrawingBackendCompositor(d3d12Backend);

        var drawCommandTranslator = new WindowDrawCommandTranslator(window);
        await using var compositorLoop = new CompositorLoop(drawCommandTranslator, new CompositeCompositor(
            new ConsoleCompositor(Console.Out),
            d3d12Compositor));
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);
        using var inputSubscription = platformHost.RawInputEvents.Subscribe(new PlatformInputObserver(HandleInput));

        platformHost.TopologyChanged += OnTopologyChanged;

        Console.WriteLine($"Detected screens: {platformHost.Screens.Count}");
        Console.WriteLine("Rendering: D3D12 (clear color from FillRect)");
        Console.WriteLine("Controls: Click buttons, Up/Down = +/-1, R = reset, Mouse wheel = +/-1.");

        await runtime.StartAsync();

        window.Show();
        window.RunMessageLoop();

        Console.WriteLine($"Final count: {runtime.CurrentModel.Count}");

        void HandleInput(RawInputEvent inputEvent)
        {
            if (CounterInputRouter.TryMapInput(inputEvent, TryGetActionIdAt, out var message))
            {
                runtime.Dispatch(message);
            }
        }

        string? TryGetActionIdAt(int x, int y)
        {
            return d3d12Compositor.TryGetActionIdAt(x, y, out var actionId) ? actionId : null;
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

    /// <summary>
    /// Diagnostic smoke mode: renders one frame with test rectangles and text,
    /// dumps cache stats, and exits. Use: --diagnose
    /// </summary>
    private static void RunDiagnosticMode()
    {
        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);

        // Build a test frame: one rectangle + one text + one button
        var resources = FrameDrawingResources.Rent();
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        var bgText = resources.AddText("Diagnostic Mode: Hello World 🎯");
        var btnText = resources.AddText("Click Me");
        resources.Seal();

        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect,
                Rect: new DrawRect(0, 0, 960, 540),
                Color: DrawColor.Opaque(32, 32, 32)),
            new(DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 120, 200, 40),
                Color: DrawColor.Opaque(52, 120, 246)),
            new(DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32),
                Resource: textStyle,
                Text: bgText,
                Color: DrawColor.Opaque(255, 255, 255)),
            new(DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 120, 200, 40),
                Resource: textStyle,
                Text: btnText,
                Color: DrawColor.Opaque(255, 255, 255))
        };

        // Render 3 frames to warm caches
        for (var i = 0; i < 3; i++)
        {
            d3d12Backend.BeginFrame(default);
            d3d12Backend.Execute(commands, resources);
            d3d12Backend.EndFrame();
        }

        // Dump diagnostics
        var diag = d3d12Renderer.GetTextDiagnostics();
        if (diag.HasValue)
        {
            var d = diag.Value;
            Console.WriteLine("=== D3D12 Text Renderer Diagnostics ===");
            Console.WriteLine($"Format cache: {d.CachedFormats} entries, {d.FormatHits} hits, {d.FormatMisses} misses, {d.FormatEvictions} evictions");
            Console.WriteLine($"Layout cache: {d.CachedLayouts} entries, {d.LayoutHits} hits, {d.LayoutMisses} misses, {d.LayoutEvictions} evictions");
            Console.WriteLine($"Format hit rate: {(d.FormatHits + d.FormatMisses > 0 ? 100.0 * d.FormatHits / (d.FormatHits + d.FormatMisses) : 0):F1}%");
            Console.WriteLine($"Layout hit rate: {(d.LayoutHits + d.LayoutMisses > 0 ? 100.0 * d.LayoutHits / (d.LayoutHits + d.LayoutMisses) : 0):F1}%");
        }

        Console.WriteLine($"Device removed: {d3d12Renderer.IsDeviceRemoved}");
        Console.WriteLine($"Device error reason: {d3d12Renderer.DeviceErrorReason ?? "(none)"}");
        Console.WriteLine($"Swapchain size: {d3d12Renderer.Width}x{d3d12Renderer.Height}");
        Console.WriteLine("=== Diagnostic mode complete ===");

        FrameDrawingResources.Return(resources);
    }
}
