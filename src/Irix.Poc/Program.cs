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
        await using var compositorLoop = new CompositorLoop(new ConsoleCompositor(Console.Out));
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);

        Console.WriteLine($"Detected screens: {platformHost.Screens.Count}");

        await runtime.StartAsync();
        runtime.Dispatch(new CounterMessage.Increment());
        runtime.Dispatch(new CounterMessage.Increment());
        runtime.Dispatch(new CounterMessage.Decrement());

        window.Show();
        window.RunMessageLoop();

        Console.WriteLine($"Final count: {runtime.CurrentModel.Count}");
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
}
