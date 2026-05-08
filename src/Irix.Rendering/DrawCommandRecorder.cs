using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed class DrawCommandRecorder(DrawingStyle style)
{
    private const int StackCommandThreshold = 64;

    private readonly DrawingStyle _style = style;

    public DrawCommandRecorder()
        : this(DrawingStyle.Default)
    {
    }

    /// <summary>
    /// Record draw commands for all layout elements.
    /// Returns the full command batch plus element→command range mapping.
    /// </summary>
    public DrawCommandRecordResult Record(IReadOnlyList<LayoutElement> elements)
    {
        return Record(elements, dirtyElementRanges: null);
    }

    /// <summary>
    /// Record draw commands for all layout elements.
    /// When <paramref name="dirtyElementRanges"/> is provided, computes the corresponding
    /// dirty draw command ranges using the element→command mapping.
    /// </summary>
    public DrawCommandRecordResult Record(
        IReadOnlyList<LayoutElement> elements,
        IReadOnlyList<(int Start, int Count)>? dirtyElementRanges)
    {
        if (elements.Count == 0)
        {
            return new DrawCommandRecordResult(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0),
                FrameDrawingResources.Empty);
        }

        var maximumCommandCount = elements.Count * 2;
        var resources = FrameDrawingResources.Rent();
        var textStyle = resources.AddTextStyle(_style.TextStyle);
        var buttonTextStyle = resources.AddTextStyle(_style.ButtonTextStyle);

        var (batch, resolver, elementRanges) = maximumCommandCount <= StackCommandThreshold
            ? RecordSmallBatch(elements, resources, textStyle, buttonTextStyle, maximumCommandCount)
            : RecordLargeBatch(elements, resources, textStyle, buttonTextStyle, maximumCommandCount);

        var dirtyCommandRanges = dirtyElementRanges is { Count: > 0 }
            ? ComputeDirtyCommandRanges(elementRanges, dirtyElementRanges)
            : (IReadOnlyList<(int Start, int Count)>)[];
        var mergedDirtyCommandRanges = MergeRanges(dirtyCommandRanges);

        return new DrawCommandRecordResult(batch, resolver, elementRanges, mergedDirtyCommandRanges);
    }

    private (DrawCommandBatch, IFrameResourceResolver, ElementCommandRange[]) RecordSmallBatch(
        IReadOnlyList<LayoutElement> elements,
        FrameDrawingResources resources,
        ResourceHandle textStyle,
        ResourceHandle buttonTextStyle,
        int maximumCommandCount)
    {
        Span<DrawCommand> stackCommands = stackalloc DrawCommand[maximumCommandCount];
        Span<ElementCommandRange> stackRanges = stackalloc ElementCommandRange[elements.Count];
        var stackCommandCount = RecordInto(elements, resources, _style, textStyle, buttonTextStyle, stackCommands, stackRanges);
        resources.Seal();

        var owner = PooledArrayMemoryOwner<DrawCommand>.Rent(stackCommandCount);
        stackCommands[..stackCommandCount].CopyTo(owner.Memory.Span);
        var elementRanges = stackRanges.ToArray();
        return (new DrawCommandBatch(owner, stackCommandCount), resources, elementRanges);
    }

    private (DrawCommandBatch, IFrameResourceResolver, ElementCommandRange[]) RecordLargeBatch(
        IReadOnlyList<LayoutElement> elements,
        FrameDrawingResources resources,
        ResourceHandle textStyle,
        ResourceHandle buttonTextStyle,
        int maximumCommandCount)
    {
        var pooledOwner = PooledArrayMemoryOwner<DrawCommand>.Rent(maximumCommandCount);
        var elementRanges = new ElementCommandRange[elements.Count];
        var success = false;
        try
        {
            var commandCount = RecordInto(elements, resources, _style, textStyle, buttonTextStyle, pooledOwner.Memory.Span, elementRanges);
            resources.Seal();
            success = true;
            return (new DrawCommandBatch(pooledOwner, commandCount), resources, elementRanges);
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
        Span<DrawCommand> commands,
        Span<ElementCommandRange> elementRanges)
    {
        var commandCount = 0;

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var startCommand = commandCount;

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

            elementRanges[i] = new ElementCommandRange(startCommand, commandCount - startCommand);
        }

        return commandCount;
    }

    private static IReadOnlyList<(int Start, int Count)> ComputeDirtyCommandRanges(
        ElementCommandRange[] elementRanges,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges)
    {
        var ranges = new List<(int Start, int Count)>();
        foreach (var (elementStart, elementCount) in dirtyElementRanges)
        {
            var elementEnd = elementStart + elementCount;
            if (elementStart >= elementRanges.Length)
            {
                continue;
            }

            var clampedEnd = Math.Min(elementEnd, elementRanges.Length);
            var cmdStart = elementRanges[elementStart].CommandStart;
            var lastRange = elementRanges[clampedEnd - 1];
            var cmdEnd = lastRange.CommandStart + lastRange.CommandCount;
            ranges.Add((cmdStart, cmdEnd - cmdStart));
        }

        return MergeRanges(ranges);
    }

    private static IReadOnlyList<(int Start, int Count)> MergeRanges(IReadOnlyList<(int Start, int Count)> ranges)
    {
        if (ranges.Count <= 1)
        {
            return ranges;
        }

        var sorted = new List<(int Start, int Count)>(ranges);
        sorted.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(int Start, int Count)> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var last = merged[^1];
            var current = sorted[i];
            var lastEnd = last.Start + last.Count;

            if (current.Start <= lastEnd)
            {
                var newEnd = Math.Max(lastEnd, current.Start + current.Count);
                merged[^1] = (last.Start, newEnd - last.Start);
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    private static DrawRect ToDrawRect(PixelRectangle bounds)
    {
        return new DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
