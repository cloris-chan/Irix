using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly record struct DrawCommandRecordResult(
    DrawCommandBatch Commands,
    IFrameResourceResolver Resources);

internal sealed class DrawCommandRecorder(DrawingStyle style)
{
    private const int StackCommandThreshold = 64;

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

        var maximumCommandCount = elements.Count * 2;
        var resources = new FrameDrawingResources();
        var textStyle = resources.AddTextStyle(_style.TextStyle);
        var buttonTextStyle = resources.AddTextStyle(_style.ButtonTextStyle);

        if (maximumCommandCount <= StackCommandThreshold)
        {
            Span<DrawCommand> stackCommands = stackalloc DrawCommand[maximumCommandCount];
            var stackCommandCount = RecordInto(elements, resources, _style, textStyle, buttonTextStyle, stackCommands);
            resources.Seal();

            var owner = PooledArrayMemoryOwner<DrawCommand>.Rent(stackCommandCount);
            stackCommands[..stackCommandCount].CopyTo(owner.Memory.Span);
            return new DrawCommandRecordResult(new DrawCommandBatch(owner, stackCommandCount), resources);
        }

        var pooledOwner = PooledArrayMemoryOwner<DrawCommand>.Rent(maximumCommandCount);
        var success = false;
        try
        {
            var commandCount = RecordInto(elements, resources, _style, textStyle, buttonTextStyle, pooledOwner.Memory.Span);
            resources.Seal();
            success = true;
            return new DrawCommandRecordResult(new DrawCommandBatch(pooledOwner, commandCount), resources);
        }
        finally
        {
            if (!success)
            {
                pooledOwner.Dispose();
            }
        }
    }

    private static int RecordInto(
        IReadOnlyList<LayoutElement> elements,
        FrameDrawingResources resources,
        DrawingStyle style,
        ResourceHandle textStyle,
        ResourceHandle buttonTextStyle,
        Span<DrawCommand> commands)
    {
        var commandCount = 0;

        foreach (var element in elements)
        {
            switch (element.Kind)
            {
                case LayoutElementKind.Text:
                    var text = resources.AddText(element.Text);
                    commands[commandCount++] = new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: ToDrawRect(element.Bounds),
                        Resource: textStyle,
                        Text: text,
                        Color: style.TextColor);
                    break;
                case LayoutElementKind.Rectangle:
                    commands[commandCount++] = new DrawCommand(
                        DrawCommandKind.FillRect,
                        Rect: ToDrawRect(element.Bounds),
                        Color: style.RectangleFillColor);
                    break;
                case LayoutElementKind.Button:
                    var bounds = ToDrawRect(element.Bounds);
                    commands[commandCount++] = new DrawCommand(
                        DrawCommandKind.FillRect,
                        Rect: bounds,
                        Color: style.ButtonFillColor);
                    var buttonText = resources.AddText(element.Text);
                    commands[commandCount++] = new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: bounds,
                        Resource: buttonTextStyle,
                        Text: buttonText,
                        Color: style.ButtonTextColor);
                    break;
            }
        }

        return commandCount;
    }

    private static DrawRect ToDrawRect(PixelRectangle bounds)
    {
        return new DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
