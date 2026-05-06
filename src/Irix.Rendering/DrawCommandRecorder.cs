using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly record struct DrawCommandRecordResult(
    DrawCommandBatch Commands,
    ITextResolver TextResolver);

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
                FrameTextArena.Empty);
        }

        var commands = new List<DrawCommand>(elements.Count * 2);
        var textArena = new FrameTextArena();

        foreach (var element in elements)
        {
            switch (element.Kind)
            {
                case LayoutElementKind.Text:
                    var text = textArena.Add(element.Text);
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: ToDrawRect(element.Bounds),
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
                    var buttonText = textArena.Add(element.Text);
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: bounds,
                        Text: buttonText,
                        Color: _style.ButtonTextColor));
                    break;
            }
        }

        textArena.Seal();

        return new DrawCommandRecordResult(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([.. commands]), commands.Count),
            textArena);
    }

    private static DrawRect ToDrawRect(PixelRectangle bounds)
    {
        return new DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
