using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed partial class DrawCommandRecorder(DrawingStyle style, ControlVisualStateResolver visualStateResolver)
{
    private const int StackCommandThreshold = 64;

    private readonly DrawingStyle _style = style;
    private readonly ControlVisualStateResolver _visualStateResolver = visualStateResolver;

    public DrawCommandRecorder()
        : this(RenderStylePreset.Default.Drawing, RenderStylePreset.Default.VisualStates)
    {
    }

    public DrawCommandRecorder(DrawingStyle style)
        : this(style, ControlVisualStateResolver.Default)
    {
    }

    public DrawCommandRecordResult Record(LayoutElement[] elements, TextBufferSnapshot textSnapshot)
    {
        return Record(elements.AsSpan(), textSnapshot);
    }

    /// <summary>
    /// Record draw commands for all layout elements.
    /// Returns the full command batch plus element→command range mapping.
    /// </summary>
    public DrawCommandRecordResult Record(IReadOnlyList<LayoutElement> elements, TextBufferSnapshot textSnapshot)
    {
        return elements is LayoutElement[] elementArray
            ? Record(elementArray.AsSpan(), textSnapshot)
            : RecordCore(new LayoutElementSource(elements), IndexRangeList.Empty, textSnapshot);
    }

    public DrawCommandRecordResult Record(ReadOnlySpan<LayoutElement> elements, TextBufferSnapshot textSnapshot)
    {
        return Record(elements, IndexRangeList.Empty, textSnapshot);
    }

    public DrawCommandRecordResult Record(
        LayoutElement[] elements,
        IndexRangeList dirtyElementRanges,
        TextBufferSnapshot textSnapshot)
    {
        return Record(elements.AsSpan(), dirtyElementRanges, textSnapshot);
    }

    /// <summary>
    /// Record draw commands for all layout elements.
    /// When <paramref name="dirtyElementRanges"/> is provided, computes the corresponding
    /// dirty draw command ranges using the element→command mapping.
    /// </summary>
    public DrawCommandRecordResult Record(
        IReadOnlyList<LayoutElement> elements,
        IndexRangeList dirtyElementRanges,
        TextBufferSnapshot textSnapshot)
    {
        return elements is LayoutElement[] elementArray
            ? Record(elementArray.AsSpan(), dirtyElementRanges, textSnapshot)
            : RecordCore(new LayoutElementSource(elements), dirtyElementRanges, textSnapshot);
    }

    public DrawCommandRecordResult Record(
        ReadOnlySpan<LayoutElement> elements,
        IndexRangeList dirtyElementRanges,
        TextBufferSnapshot textSnapshot)
    {
        return RecordCore(new LayoutElementSource(elements), dirtyElementRanges, textSnapshot);
    }

    private DrawCommandRecordResult RecordCore(
        LayoutElementSource elements,
        IndexRangeList dirtyElementRanges,
        TextBufferSnapshot textSnapshot)
    {
        OnRecordAllocationStarted();
        if (elements.Count == 0)
        {
            return new DrawCommandRecordResult(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0),
                FrameDrawingResources.Empty);
        }

        var maximumCommandCount = elements.Count * 3;
        OnRecordAllocationPhaseStarted();
        var resources = FrameDrawingResources.Rent();
        OnRecordResourcesAllocated();
        var success = false;
        try
        {
            OnRecordAllocationPhaseStarted();
            var textStyle = resources.AddTextStyle(_style.TextStyle);
            var buttonTextStyle = resources.AddTextStyle(_style.ButtonTextStyle);
            OnRecordStylesAllocated();

            OnRecordAllocationPhaseStarted();
            var (batch, resolver, elementRanges) = maximumCommandCount <= StackCommandThreshold
                ? RecordSmallBatch(elements, resources, textStyle, buttonTextStyle, maximumCommandCount, textSnapshot)
                : RecordLargeBatch(elements, resources, textStyle, buttonTextStyle, maximumCommandCount, textSnapshot);
            OnRecordCommandBuildAllocated();

            OnRecordAllocationPhaseStarted();
            var dirtyCommandRanges = dirtyElementRanges.Count > 0
                ? RangeUtils.MapAndMerge(elementRanges, dirtyElementRanges)
                : IndexRangeList.Empty;
            OnRecordDirtyRangesAllocated();

            success = true;
            return new DrawCommandRecordResult(batch, resolver, elementRanges, dirtyCommandRanges);
        }
        finally
        {
            if (!success)
            {
                FrameDrawingResources.Return(resources);
            }
        }
    }

    partial void OnRecordAllocationStarted();
    partial void OnRecordAllocationPhaseStarted();
    partial void OnRecordResourcesAllocated();
    partial void OnRecordStylesAllocated();
    partial void OnRecordCommandBuildAllocated();
    partial void OnRecordDirtyRangesAllocated();

    private readonly ref struct LayoutElementSource
    {
        private readonly ReadOnlySpan<LayoutElement> _span;
        private readonly IReadOnlyList<LayoutElement>? _list;

        public LayoutElementSource(ReadOnlySpan<LayoutElement> span)
        {
            _span = span;
            _list = null;
            Count = span.Length;
        }

        public LayoutElementSource(IReadOnlyList<LayoutElement> list)
        {
            _span = default;
            _list = list;
            Count = list.Count;
        }

        public int Count { get; }

        public LayoutElement this[int index] => _list is null ? _span[index] : _list[index];
    }

    private (DrawCommandBatch, IFrameResourceResolver, ElementCommandRange[]) RecordSmallBatch(
        LayoutElementSource elements,
        FrameDrawingResources resources,
        ResourceHandle textStyle,
        ResourceHandle buttonTextStyle,
        int maximumCommandCount,
        TextBufferSnapshot textSnapshot)
    {
        Span<DrawCommand> stackCommands = stackalloc DrawCommand[maximumCommandCount];
        Span<ElementCommandRange> stackRanges = stackalloc ElementCommandRange[elements.Count];
        var stackCommandCount = RecordInto(elements, resources, _style, _visualStateResolver, textStyle, buttonTextStyle, textSnapshot, stackCommands, stackRanges);
        resources.Seal();

        var owner = PooledArrayMemoryOwner<DrawCommand>.Rent(stackCommandCount);
        stackCommands[..stackCommandCount].CopyTo(owner.Memory.Span);
        var elementRanges = stackRanges.ToArray();
        return (new DrawCommandBatch(owner, stackCommandCount), resources, elementRanges);
    }

    private (DrawCommandBatch, IFrameResourceResolver, ElementCommandRange[]) RecordLargeBatch(
        LayoutElementSource elements,
        FrameDrawingResources resources,
        ResourceHandle textStyle,
        ResourceHandle buttonTextStyle,
        int maximumCommandCount,
        TextBufferSnapshot textSnapshot)
    {
        var pooledOwner = PooledArrayMemoryOwner<DrawCommand>.Rent(maximumCommandCount);
        var elementRanges = new ElementCommandRange[elements.Count];
        var success = false;
        try
        {
            var commandCount = RecordInto(elements, resources, _style, _visualStateResolver, textStyle, buttonTextStyle, textSnapshot, pooledOwner.Memory.Span, elementRanges);
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
        LayoutElementSource elements,
        FrameDrawingResources resources,
        DrawingStyle style,
        ControlVisualStateResolver visualStateResolver,
        ResourceHandle textStyle,
        ResourceHandle buttonTextStyle,
        TextBufferSnapshot textSnapshot,
        Span<DrawCommand> commands,
        Span<ElementCommandRange> elementRanges)
    {
        var commandCount = 0;

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var startCommand = commandCount;
            var clip = ToDrawRect(element.ClipBounds);

            switch (element.Kind)
            {
                case LayoutElementKind.Text:
                    var text = resources.AddText(ResolveText(element.Text, textSnapshot));
                    commands[commandCount++] = DrawCommand.FromCanonicalColor(
                        DrawCommandKind.DrawTextRun,
                        Rect: ToDrawRect(element.Bounds),
                        Resource: textStyle,
                        Text: text,
                        ClipBounds: clip,
                        Color: ResolveColor(element.ForegroundColor, style.TextColor));
                    break;
                case LayoutElementKind.Rectangle:
                    var rectangleBounds = ToDrawRect(element.Bounds);
                    commands[commandCount++] = CreateFillCommand(
                        resources,
                        rectangleBounds,
                        clip,
                        element.Background,
                        style.RectangleFillColor);
                    if (element.Border.HasValue)
                    {
                        commands[commandCount++] = CreateBorderCommand(resources, rectangleBounds, clip, element.Border.Value);
                    }
                    break;
                case LayoutElementKind.Button:
                    var bounds = ToDrawRect(element.Bounds);
                    commands[commandCount++] = CreateFillCommand(
                        resources,
                        bounds,
                        clip,
                        element.Background,
                        visualStateResolver.ResolveButtonFillColor(style, element.ButtonState));
                    if (element.Border.HasValue)
                    {
                        commands[commandCount++] = CreateBorderCommand(resources, bounds, clip, element.Border.Value);
                    }
                    var buttonSpan = ResolveText(element.Text, textSnapshot);
                    if (!buttonSpan.IsEmpty)
                    {
                        var buttonText = resources.AddText(buttonSpan);
                        commands[commandCount++] = DrawCommand.FromCanonicalColor(
                            DrawCommandKind.DrawTextRun,
                            Rect: bounds,
                            Resource: buttonTextStyle,
                            Text: buttonText,
                            ClipBounds: clip,
                            Color: ResolveColor(element.ForegroundColor, style.ButtonTextColor));
                    }
                    break;
            }

            elementRanges[i] = new ElementCommandRange(startCommand, commandCount - startCommand);
        }

        return commandCount;
    }

    private static ReadOnlySpan<char> ResolveText(TextContentResource content, TextBufferSnapshot snapshot)
    {
        return snapshot.ResolveRequired(content);
    }

    private static DrawRect ToDrawRect(PixelRectangle bounds)
    {
        return new DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static Color ResolveColor(StyleColorSlot styleColor, DrawColor defaultColor)
    {
        if (!styleColor.HasValue)
        {
            return Color.FromSrgb(defaultColor.A, defaultColor.R, defaultColor.G, defaultColor.B);
        }

        return styleColor.Value.Value;
    }

    private static DrawCommand CreateFillCommand(
        FrameDrawingResources resources,
        DrawRect bounds,
        DrawRect clip,
        PaintSlot stylePaint,
        DrawColor defaultColor)
    {
        if (!stylePaint.HasValue)
        {
            return DrawCommand.FromCanonicalColor(
                DrawCommandKind.FillRect,
                Rect: bounds,
                ClipBounds: clip,
                Color: Color.FromSrgb(defaultColor.A, defaultColor.R, defaultColor.G, defaultColor.B));
        }

        var paint = stylePaint.Value;
        if (paint.TryGetSolidColor(out var color))
        {
            return DrawCommand.FromCanonicalColor(
                DrawCommandKind.FillRect,
                Rect: bounds,
                ClipBounds: clip,
                Color: color);
        }

        if (!paint.TryGetLinearGradient(out var startColor, out var endColor, out var direction))
        {
            throw new InvalidOperationException($"Unsupported paint kind {paint.Kind}.");
        }

        var material = CreateLinearGradientMaterial(startColor, endColor, direction, bounds);
        return DrawCommand.FromMaterial(
            DrawCommandKind.FillRect,
            Rect: bounds,
            Resource: resources.AddBrush(material),
            ClipBounds: clip,
            Material: material);
    }

    private static DrawCommand CreateBorderCommand(
        FrameDrawingResources resources,
        DrawRect bounds,
        DrawRect clip,
        BorderStroke border)
    {
        var paint = border.Paint;
        if (paint.TryGetSolidColor(out var color))
        {
            return DrawCommand.FromCanonicalColor(
                DrawCommandKind.StrokeRect,
                Rect: bounds,
                ClipBounds: clip,
                StrokeWidth: border.Thickness,
                Color: color);
        }

        if (!paint.TryGetLinearGradient(out var startColor, out var endColor, out var direction))
        {
            throw new InvalidOperationException($"Unsupported border paint kind {paint.Kind}.");
        }

        var material = CreateLinearGradientMaterial(startColor, endColor, direction, bounds);
        return DrawCommand.FromMaterial(
            DrawCommandKind.StrokeRect,
            Rect: bounds,
            Resource: resources.AddBrush(material),
            ClipBounds: clip,
            StrokeWidth: border.Thickness,
            Material: material);
    }

    private static DrawMaterial CreateLinearGradientMaterial(
        Color startColor,
        Color endColor,
        LinearGradientDirection direction,
        DrawRect bounds)
    {
        var (startPoint, endPoint) = direction switch
        {
            LinearGradientDirection.LeftToRight =>
                (new DrawPoint(0, 0), new DrawPoint(bounds.Width, 0)),
            LinearGradientDirection.TopToBottom =>
                (new DrawPoint(0, 0), new DrawPoint(0, bounds.Height)),
            LinearGradientDirection.TopLeftToBottomRight =>
                (new DrawPoint(0, 0), new DrawPoint(bounds.Width, bounds.Height)),
            LinearGradientDirection.TopRightToBottomLeft =>
                (new DrawPoint(bounds.Width, 0), new DrawPoint(0, bounds.Height)),
            _ => throw new InvalidOperationException($"Unsupported linear-gradient direction {direction}.")
        };

        return DrawMaterial.LinearGradient(startColor, endColor, startPoint, endPoint);
    }
}
