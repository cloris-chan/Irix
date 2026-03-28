using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed class DrawCommandRecorder
{
    public DrawCommandBatch Record(IReadOnlyList<LayoutElement> elements)
    {
        if (elements.Count == 0)
        {
            return new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0);
        }

        var commands = new List<DrawCommand>(elements.Count * 2);

        foreach (var element in elements)
        {
            switch (element.Kind)
            {
                case LayoutElementKind.Text:
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: ToDrawRect(element.Bounds),
                        Text: element.Text,
                        Color: DrawColor.Opaque(255, 255, 255)));
                    break;
                case LayoutElementKind.Rectangle:
                    commands.Add(new DrawCommand(
                        DrawCommandKind.FillRect,
                        Rect: ToDrawRect(element.Bounds),
                        Color: DrawColor.Opaque(72, 72, 72)));
                    break;
                case LayoutElementKind.Button:
                    var bounds = ToDrawRect(element.Bounds);
                    commands.Add(new DrawCommand(
                        DrawCommandKind.FillRect,
                        Rect: bounds,
                        Color: DrawColor.Opaque(52, 120, 246),
                        Metadata: element.Action));
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: bounds,
                        Text: element.Text,
                        Color: DrawColor.Opaque(255, 255, 255),
                        Metadata: element.Action));
                    break;
            }
        }

        return new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([.. commands]), commands.Count);
    }

    private static DrawRect ToDrawRect(PixelRectangle bounds)
    {
        return new DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
