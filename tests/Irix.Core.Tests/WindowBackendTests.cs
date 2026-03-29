using Irix.Drawing;
using Irix.Platform;
using Xunit;

namespace Irix.Core.Tests;

public sealed class WindowBackendTests
{
    [Fact]
    public void WindowBackend_builds_button_text_and_rectangle_elements()
    {
        var backend = new Irix.Poc.WindowBackend();
        var commands = new[]
        {
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 60, 220, 48),
                Color: DrawColor.Opaque(72, 72, 72)),
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 120, 140, 40),
                Color: DrawColor.Opaque(52, 120, 246),
                Metadata: "Increment"),
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 120, 140, 40),
                Text: "Increment",
                Color: DrawColor.Opaque(255, 255, 255),
                Metadata: "Increment"),
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32),
                Text: "Count: 0",
                Color: DrawColor.Opaque(255, 255, 255))
        };

        var result = backend.Build(commands);

        Assert.Equal(3, result.Elements.Count);
        Assert.Equal(new WindowContentElement(
            WindowContentElementKind.Rectangle,
            new PixelRectangle(16, 60, 220, 48)), result.Elements[0]);
        Assert.Equal(new WindowContentElement(
            WindowContentElementKind.Button,
            new PixelRectangle(16, 120, 140, 40),
            "Increment"), result.Elements[1]);
        Assert.Equal(new WindowContentElement(
            WindowContentElementKind.Text,
            new PixelRectangle(16, 16, 928, 32),
            "Count: 0"), result.Elements[2]);

        Assert.Single(result.HitTargets);
        Assert.Equal(new PixelRectangle(16, 120, 140, 40), result.HitTargets[0].Bounds);
        Assert.Equal("Increment", result.HitTargets[0].Action);
    }
}
