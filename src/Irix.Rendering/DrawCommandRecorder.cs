using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly record struct DrawCommandRecordResult(
    DrawCommandBatch Commands,
    IFrameResourceResolver Resources);

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
                FrameDrawingResources.Empty);
        }

        var commands = new List<DrawCommand>(elements.Count * 2);
        var resources = new FrameDrawingResources();
        var textStyle = resources.AddTextStyle(_style.TextStyle);
        var buttonTextStyle = resources.AddTextStyle(_style.ButtonTextStyle);

        foreach (var element in elements)
        {
            switch (element.Kind)
            {
                case LayoutElementKind.Text:
                    var text = resources.AddText(element.Text);
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: ToDrawRect(element.Bounds),
                        Resource: textStyle,
                        Text: text,
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
                    var buttonText = resources.AddText(element.Text);
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: bounds,
                        Resource: buttonTextStyle,
                        Text: buttonText,
                        Color: _style.ButtonTextColor));
                    break;
            }
        }

        resources.Seal();

        return new DrawCommandRecordResult(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([.. commands]), commands.Count),
            resources);
    }

    private static DrawRect ToDrawRect(PixelRectangle bounds)
    {
        return new DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
