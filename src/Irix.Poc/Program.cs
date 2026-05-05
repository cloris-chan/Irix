using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class Program
{
    public static async Task Main()
    {
        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));

        // D3D12 rendering path
        var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer, d3d12Renderer.Renderer2D);
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
}
