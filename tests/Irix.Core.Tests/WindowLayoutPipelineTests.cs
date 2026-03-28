using Irix.Drawing;
using Irix.Platform;
using Xunit;

namespace Irix.Core.Tests;

public sealed class WindowLayoutPipelineTests
{
    [Fact]
    public void WindowLayoutTreeBuilder_builds_expected_layout_elements()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Rectangle(220, 48, 3),
            VirtualNodeFactory.Button(
                "Increment",
                4,
                new VirtualNodeAttribute("Action", AttributeValue.FromText("Increment"))));
        var builder = new Irix.Poc.WindowLayoutTreeBuilder();

        var elements = builder.Build(root, new PixelRectangle(0, 0, 960, 540));

        Assert.Equal(3, elements.Count);

        Assert.Equal(Irix.Poc.WindowLayoutElementKind.Text, elements[0].Kind);
        Assert.Equal(new PixelRectangle(16, 16, 928, 32), elements[0].Bounds);
        Assert.Equal("Count: 0", elements[0].Text);

        Assert.Equal(Irix.Poc.WindowLayoutElementKind.Rectangle, elements[1].Kind);
        Assert.Equal(new PixelRectangle(16, 60, 220, 48), elements[1].Bounds);

        Assert.Equal(Irix.Poc.WindowLayoutElementKind.Button, elements[2].Kind);
        Assert.Equal(new PixelRectangle(16, 120, 140, 40), elements[2].Bounds);
        Assert.Equal("Increment", elements[2].Text);
        Assert.Equal("Increment", elements[2].Action);
    }

    [Fact]
    public void WindowDrawCommandRecorder_records_button_as_fill_and_text_commands()
    {
        var recorder = new Irix.Poc.WindowDrawCommandRecorder();
        var elements = new[]
        {
            new Irix.Poc.WindowLayoutElement(
                Irix.Poc.WindowLayoutElementKind.Button,
                new PixelRectangle(16, 120, 140, 40),
                Text: "Increment",
                Action: "Increment")
        };

        using var batch = recorder.Record(elements);

        Assert.Equal(2, batch.Count);

        var fillCommand = batch.Memory.Span[0];
        Assert.Equal(DrawCommandKind.FillRect, fillCommand.Kind);
        Assert.Equal(new DrawRect(16, 120, 140, 40), fillCommand.Rect);
        Assert.Equal("Increment", fillCommand.Metadata);

        var textCommand = batch.Memory.Span[1];
        Assert.Equal(DrawCommandKind.DrawTextRun, textCommand.Kind);
        Assert.Equal(new DrawRect(16, 120, 140, 40), textCommand.Rect);
        Assert.Equal("Increment", textCommand.Text);
        Assert.Equal("Increment", textCommand.Metadata);
    }
}
