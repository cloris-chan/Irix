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

        var button = result.Elements[1];
        Assert.Equal(WindowContentElementKind.Button, button.Kind);
        Assert.Equal(new PixelRectangle(16, 120, 140, 40), button.Bounds);
        Assert.Equal("Increment", result.TextResolver.Resolve(button.Text).ToString());
        Assert.Equal(WindowColor.Opaque(255, 255, 255), button.ForegroundColor);
        Assert.Equal(WindowColor.Opaque(52, 120, 246), button.BackgroundColor);
        Assert.Equal(WindowColor.Opaque(24, 48, 96), button.BorderColor);
        Assert.Equal(1, button.BorderThickness);

        var text = result.Elements[2];
        Assert.Equal(WindowContentElementKind.Text, text.Kind);
        Assert.Equal(new PixelRectangle(16, 16, 928, 32), text.Bounds);
        Assert.Equal("Count: 0", result.TextResolver.Resolve(text.Text).ToString());
        Assert.Equal(WindowColor.Opaque(255, 255, 255), text.ForegroundColor);

        Assert.Single(result.HitTargets);
        Assert.Equal(new PixelRectangle(16, 120, 140, 40), result.HitTargets[0].Bounds);
        Assert.Equal(new ActionId(1), result.HitTargets[0].ActionId);
    }

    [Fact]
    public void WindowBackend_maps_internal_linear_gradient_material_to_sdr_fallback()
    {
        var backend = new Irix.Poc.WindowBackend();
        var resources = new FrameDrawingResources();
        resources.Seal();
        var material = DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            new DrawPoint(0, 0),
            new DrawPoint(120, 0));
        var command = DrawCommand.FromMaterial(
            DrawCommandKind.FillRect,
            Rect: new DrawRect(16, 60, 220, 48),
            Material: material);
        var expected = ColorOutputMapping.SdrSrgb.MapToSdr(material);

        var result = backend.Build([command], [], resources);

        Assert.Single(result.Elements);
        Assert.Equal(new WindowContentElement(
            WindowContentElementKind.Rectangle,
            new PixelRectangle(16, 60, 220, 48),
            BackgroundColor: new WindowColor(expected.A, expected.R, expected.G, expected.B)), result.Elements[0]);
    }

    [Fact]
    public void WindowBackend_maps_semantic_gradient_stroke_to_border_fallback()
    {
        var backend = new Irix.Poc.WindowBackend();
        var resources = new FrameDrawingResources();
        resources.Seal();
        var material = DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            new DrawPoint(0, 0),
            new DrawPoint(120, 0));
        var command = DrawCommand.FromMaterial(
            DrawCommandKind.StrokeRect,
            Rect: new DrawRect(16, 60, 220, 48),
            Material: material,
            StrokeWidth: 3);
        var expected = ColorOutputMapping.SdrSrgb.MapToSdr(material);

        var result = backend.Build([command], [], resources);

        Assert.Single(result.Elements);
        Assert.Equal(new WindowContentElement(
            WindowContentElementKind.Rectangle,
            new PixelRectangle(16, 60, 220, 48),
            BorderColor: new WindowColor(expected.A, expected.R, expected.G, expected.B),
            BorderThickness: 3), result.Elements[0]);
    }
}
