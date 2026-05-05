using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class WindowLayoutPipelineTests
{
    [Fact]
    public void LayoutTreeBuilder_builds_expected_layout_elements()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Rectangle(220, 48, 3),
            VirtualNodeFactory.Button(
                "Increment",
                4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var builder = new LayoutTreeBuilder();

        var elements = builder.Build(root, new PixelRectangle(0, 0, 960, 540));

        Assert.Equal(3, elements.Count);

        Assert.Equal(LayoutElementKind.Text, elements[0].Kind);
        Assert.Equal(new PixelRectangle(16, 16, 928, 32), elements[0].Bounds);
        Assert.Equal("Count: 0", elements[0].Text);

        Assert.Equal(LayoutElementKind.Rectangle, elements[1].Kind);
        Assert.Equal(new PixelRectangle(16, 60, 220, 48), elements[1].Bounds);

        Assert.Equal(LayoutElementKind.Button, elements[2].Kind);
        Assert.Equal(new PixelRectangle(16, 120, 140, 40), elements[2].Bounds);
        Assert.Equal("Increment", elements[2].Text);
        Assert.Equal("Increment", elements[2].ActionId);
    }

    [Fact]
    public void DrawCommandRecorder_records_button_as_fill_and_text_commands()
    {
        var recorder = new DrawCommandRecorder();
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 120, 140, 40),
                Text: "Increment",
                ActionId: "Increment")
        };

        var result = recorder.Record(elements);

        Assert.Equal(2, result.Commands.Count);

        var fillCommand = result.Commands.Memory.Span[0];
        Assert.Equal(DrawCommandKind.FillRect, fillCommand.Kind);
        Assert.Equal(new DrawRect(16, 120, 140, 40), fillCommand.Rect);
        var textCommand = result.Commands.Memory.Span[1];
        Assert.Equal(DrawCommandKind.DrawTextRun, textCommand.Kind);
        Assert.Equal(new DrawRect(16, 120, 140, 40), textCommand.Rect);
        Assert.Equal(DrawingResourceKind.TextStyle, textCommand.Resource.Kind);

        Assert.Single(result.TextRuns);
        Assert.Equal("Increment", result.TextRuns[0].Text);
        Assert.Equal(textCommand.Resource.Id, result.TextRuns[0].Id);

        result.Commands.Dispose();
    }

    [Fact]
    public void LayoutTreeBuilder_uses_supplied_layout_style()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Title", 2),
            VirtualNodeFactory.Button(
                "Go",
                3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Go"))));
        var builder = new LayoutTreeBuilder(new LayoutStyle(
            HorizontalPadding: 24,
            VerticalPadding: 20,
            ItemSpacing: 8,
            TextHeight: 28,
            ButtonHeight: 36,
            MinimumButtonWidth: 120,
            ButtonTextWidthFactor: 10,
            ButtonHorizontalPadding: 20));

        var elements = builder.Build(root, new PixelRectangle(0, 0, 400, 300));

        Assert.Equal(new PixelRectangle(24, 20, 352, 28), elements[0].Bounds);
        Assert.Equal(new PixelRectangle(24, 56, 120, 36), elements[1].Bounds);
    }

    [Fact]
    public void DrawCommandRecorder_uses_supplied_drawing_style()
    {
        var recorder = new DrawCommandRecorder(new DrawingStyle(
            TextColor: DrawColor.Opaque(230, 230, 230),
            RectangleFillColor: DrawColor.Opaque(20, 30, 40),
            ButtonFillColor: DrawColor.Opaque(10, 20, 30),
            ButtonTextColor: DrawColor.Opaque(200, 210, 220)));
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Rectangle,
                new PixelRectangle(16, 60, 220, 48)),
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 120, 140, 40),
                Text: "Increment",
                ActionId: "Increment")
        };

        var result = recorder.Record(elements);

        Assert.Equal(DrawColor.Opaque(20, 30, 40), result.Commands.Memory.Span[0].Color);
        Assert.Equal(DrawColor.Opaque(10, 20, 30), result.Commands.Memory.Span[1].Color);
        Assert.Equal(DrawColor.Opaque(200, 210, 220), result.Commands.Memory.Span[2].Color);

        result.Commands.Dispose();
    }

    [Fact]
    public void RenderPipeline_builds_draw_commands_from_virtual_node()
    {
        var pipeline = new RenderPipeline(
            new LayoutStyle(
                HorizontalPadding: 16,
                VerticalPadding: 16,
                ItemSpacing: 12,
                TextHeight: 32,
                ButtonHeight: 40,
                MinimumButtonWidth: 140,
                ButtonTextWidthFactor: 12,
                ButtonHorizontalPadding: 32),
            new DrawingStyle(
                TextColor: DrawColor.Opaque(255, 255, 255),
                RectangleFillColor: DrawColor.Opaque(72, 72, 72),
                ButtonFillColor: DrawColor.Opaque(52, 120, 246),
                ButtonTextColor: DrawColor.Opaque(255, 255, 255)));
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button(
                "Increment",
                3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        Assert.Equal(3, frame.Commands.Count);
        Assert.Equal(DrawCommandKind.DrawTextRun, frame.Commands.Memory.Span[0].Kind);
        Assert.Equal(new DrawRect(16, 16, 928, 32), frame.Commands.Memory.Span[0].Rect);
        Assert.Equal(DrawCommandKind.FillRect, frame.Commands.Memory.Span[1].Kind);
        Assert.Equal(DrawCommandKind.DrawTextRun, frame.Commands.Memory.Span[2].Kind);
        Assert.Single(frame.HitTargets);
        Assert.Equal(new HitTestTarget(new PixelRectangle(16, 60, 140, 40), "Increment"), frame.HitTargets[0]);

        Assert.Equal(2, frame.TextRuns.Count);
        Assert.Equal("Count: 0", frame.TextRuns[0].Text);
        Assert.Equal("Increment", frame.TextRuns[1].Text);
        Assert.Equal(frame.Commands.Memory.Span[0].Resource.Id, frame.TextRuns[0].Id);
        Assert.Equal(frame.Commands.Memory.Span[2].Resource.Id, frame.TextRuns[1].Id);
    }

    [Fact]
    public void WindowDrawCommandTranslator_uses_non_zero_default_layout_style()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button(
                "Increment",
                3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        using var patchBatch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root));
        using var frame = translator.Translate(patchBatch);

        Assert.Equal(3, frame.Commands.Count);
        Assert.Equal(new DrawRect(16, 16, 928, 32), frame.Commands.Memory.Span[0].Rect);
        Assert.Equal(new DrawRect(16, 60, 140, 40), frame.Commands.Memory.Span[1].Rect);
        Assert.Equal(new DrawRect(16, 60, 140, 40), frame.Commands.Memory.Span[2].Rect);
        Assert.Single(frame.HitTargets);
        Assert.Equal(new HitTestTarget(new PixelRectangle(16, 60, 140, 40), "Increment"), frame.HitTargets[0]);
    }

    private sealed class FakeWindow(ScreenRegion region) : INativeWindow
    {
        public string Title => "Test";

        public ScreenRegion Region => region;

        public nint Handle => nint.Zero;

        public void Dispose()
        {
        }

        public void RunMessageLoop()
        {
        }

        public void SetContentElements(IReadOnlyList<WindowContentElement> elements)
        {
        }

        public void Show()
        {
        }
    }
}
