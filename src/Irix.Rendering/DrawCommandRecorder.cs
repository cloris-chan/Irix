using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly record struct DrawCommandRecordResult(
    DrawCommandBatch Commands,
    IReadOnlyList<TextRunEntry> TextRuns);

internal sealed class DrawCommandRecorder(DrawingStyle style)
{
    private readonly DrawingStyle _style = style;

    public DrawCommandRecorder()
        : this(DrawingStyle.Default)
    {
    }

    public DrawCommandRecordResult Record(IReadOnlyList<LayoutElement> elements)
    {
        if (elements.Count == 0)
        {
            return new DrawCommandRecordResult(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0),
                []);
        }

        var commands = new List<DrawCommand>(elements.Count * 2);
        var textRuns = new List<TextRunEntry>();
        var nextTextId = 0;

        foreach (var element in elements)
        {
            switch (element.Kind)
            {
                case LayoutElementKind.Text:
                    var textId = nextTextId++;
                    textRuns.Add(new TextRunEntry(textId, element.Text ?? string.Empty));
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: ToDrawRect(element.Bounds),
                        Resource: new ResourceHandle(textId, DrawingResourceKind.TextStyle),
                        Color: _style.TextColor));
                    break;
                case LayoutElementKind.Rectangle:
                    commands.Add(new DrawCommand(
                        DrawCommandKind.FillRect,
                        Rect: ToDrawRect(element.Bounds),
                        Color: _style.RectangleFillColor));
                    break;
                case LayoutElementKind.Button:
                    var bounds = ToDrawRect(element.Bounds);
                    commands.Add(new DrawCommand(
                        DrawCommandKind.FillRect,
                        Rect: bounds,
                        Color: _style.ButtonFillColor));
                    var buttonTextId = nextTextId++;
                    textRuns.Add(new TextRunEntry(buttonTextId, element.Text ?? string.Empty));
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: bounds,
                        Resource: new ResourceHandle(buttonTextId, DrawingResourceKind.TextStyle),
                        Color: _style.ButtonTextColor));
                    break;
            }
        }

        return new DrawCommandRecordResult(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([.. commands]), commands.Count),
            textRuns);
    }

    private static DrawRect ToDrawRect(PixelRectangle bounds)
    {
        return new DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
