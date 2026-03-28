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
        var visualCompositor = new WindowVisualCompositor(window);
        var drawCommandTranslator = new WindowDrawCommandTranslator(window);
        await using var compositorLoop = new CompositorLoop(drawCommandTranslator, new CompositeCompositor(
            new ConsoleCompositor(Console.Out),
            visualCompositor));
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);
        using var inputSubscription = platformHost.RawInputEvents.Subscribe(new PlatformInputObserver(HandleInput));

        platformHost.TopologyChanged += OnTopologyChanged;

        Console.WriteLine($"Detected screens: {platformHost.Screens.Count}");
        Console.WriteLine("Controls: Click buttons, Up/Down = +/-1, R = reset, Mouse wheel = +/-1.");

        await runtime.StartAsync();

        window.Show();
        window.RunMessageLoop();

        Console.WriteLine($"Final count: {runtime.CurrentModel.Count}");

        void HandleInput(RawInputEvent inputEvent)
        {
            switch (inputEvent.Kind)
            {
                case RawInputEventKind.PointerReleased
                    when inputEvent.Button == PointerButton.Left
                    && visualCompositor.TryGetActionAt(inputEvent.X, inputEvent.Y, out var action):
                    runtime.Dispatch(MapAction(action));
                    break;
                case RawInputEventKind.KeyPressed when inputEvent.KeyCode == 0x26:
                    runtime.Dispatch(new CounterMessage.Increment());
                    break;
                case RawInputEventKind.KeyPressed when inputEvent.KeyCode == 0x28:
                    runtime.Dispatch(new CounterMessage.Decrement());
                    break;
                case RawInputEventKind.PointerWheel when inputEvent.Delta > 0:
                    runtime.Dispatch(new CounterMessage.Increment());
                    break;
                case RawInputEventKind.PointerWheel when inputEvent.Delta < 0:
                    runtime.Dispatch(new CounterMessage.Decrement());
                    break;
                case RawInputEventKind.CharacterInput when inputEvent.Character is 'r' or 'R':
                    runtime.Dispatch(new CounterMessage.Reset(0));
                    break;
            }
        }

        void OnTopologyChanged(object? sender, ScreenTopologyChangedEventArgs args)
        {
            Console.WriteLine($"Topology changed. Screen count: {args.Screens.Count}");
        }

        platformHost.TopologyChanged -= OnTopologyChanged;
    }

    private static CounterMessage MapAction(string action)
    {
        return action switch
        {
            nameof(CounterMessage.Increment) => new CounterMessage.Increment(),
            nameof(CounterMessage.Decrement) => new CounterMessage.Decrement(),
            nameof(CounterMessage.Reset) => new CounterMessage.Reset(0),
            _ => throw new NotSupportedException($"Unsupported action: {action}")
        };
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
