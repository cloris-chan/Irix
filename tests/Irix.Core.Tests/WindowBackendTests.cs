using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class WindowBackendTests
{
    [Fact]
    public void WindowBackend_builds_button_text_and_rectangle_elements()
    {
        var backend = new Irix.Poc.WindowBackend();
        var resources = new FrameDrawingResources();
        var increment = resources.AddText("Increment");
        var count = resources.AddText("Count: 0");
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        resources.Seal();
        var commands = new[]
        {
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 60, 220, 48),
                Color: DrawColor.Opaque(72, 72, 72)),
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 120, 140, 40),
                Color: DrawColor.Opaque(52, 120, 246)),
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 120, 140, 40),
                Resource: textStyle,
                Text: increment,
                Color: DrawColor.Opaque(255, 255, 255)),
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32),
                Resource: textStyle,
                Text: count,
                Color: DrawColor.Opaque(255, 255, 255))
        };
        var hitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1))
        };
        var result = backend.Build(commands, hitTargets, resources);

        Assert.Equal(3, result.Elements.Count);
        Assert.Equal(new WindowContentElement(
            WindowContentElementKind.Rectangle,
            new PixelRectangle(16, 60, 220, 48),
            BackgroundColor: WindowColor.Opaque(72, 72, 72)), result.Elements[0]);
        Assert.Equal(new WindowContentElement(
            WindowContentElementKind.Button,
            new PixelRectangle(16, 120, 140, 40),
            "Increment",
            ForegroundColor: WindowColor.Opaque(255, 255, 255),
            BackgroundColor: WindowColor.Opaque(52, 120, 246),
            BorderColor: WindowColor.Opaque(24, 48, 96)), result.Elements[1]);
        Assert.Equal(new WindowContentElement(
            WindowContentElementKind.Text,
            new PixelRectangle(16, 16, 928, 32),
            "Count: 0",
            ForegroundColor: WindowColor.Opaque(255, 255, 255)), result.Elements[2]);

        Assert.Single(result.HitTargets);
        Assert.Equal(new PixelRectangle(16, 120, 140, 40), result.HitTargets[0].Bounds);
        Assert.Equal(new ActionId(1), result.HitTargets[0].ActionId);
    }
}
