using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed class RenderPipeline(LayoutStyle layoutStyle, DrawingStyle drawingStyle)
{
    private readonly LayoutTreeBuilder _layoutTreeBuilder = new LayoutTreeBuilder(layoutStyle);
    private readonly DrawCommandRecorder _drawCommandRecorder = new DrawCommandRecorder(drawingStyle);

    public RenderPipeline()
        : this(LayoutStyle.Default, DrawingStyle.Default)
    {
    }

    private VirtualNode _retainedRoot;
    private LayoutTreeResult? _retainedLayoutResult;
    private IReadOnlyList<LayoutElement>? _retainedLayout;
    private PixelRectangle _retainedViewport;

    /// <summary>
    /// The dirty element ranges from the last Build call, if any.
    /// Each tuple is (startIndex, count) into the flat LayoutElement array.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> LastDirtyElementRanges { get; private set; } = [];

    /// <summary>
    /// The dirty draw command ranges from the last Build call, if any.
    /// Each tuple is (startIndex, count) into the DrawCommand batch.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges { get; private set; } = [];

    /// <summary>
    /// The element→command range mapping from the last Build call.
    /// <c>LastElementCommandRanges[elementIndex]</c> gives (commandStart, commandCount).
    /// </summary>
    public ElementCommandRange[] LastElementCommandRanges { get; private set; } = [];

    /// <summary>
    /// Build a render frame for the given root and viewport.
    /// When <paramref name="dirtyNodes"/> is non-null, the layout tree is rebuilt
    /// and dirty element/command ranges are computed. When null (render request),
    /// reuses the retained layout if tree and viewport match.
    /// </summary>
    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null)
    {
        var treeChanged = _retainedLayout is null || !VirtualNodeDiffer.NodesEqual(_retainedRoot, root);
        var viewportChanged = _retainedViewport != viewportBounds;
        var hasDirty = dirtyNodes is { Count: > 0 };

        if (treeChanged || viewportChanged || hasDirty)
        {
            _retainedLayoutResult = _layoutTreeBuilder.BuildLayoutTree(root, viewportBounds, dirtyNodes);
            _retainedLayout = _retainedLayoutResult.Elements;
            _retainedRoot = root;
            _retainedViewport = viewportBounds;
        }

        var layout = _retainedLayout!;
        var dirtyElementRanges = hasDirty && _retainedLayoutResult is not null
            ? _retainedLayoutResult.DirtyElementRanges
            : null;

        LastDirtyElementRanges = dirtyElementRanges ?? [];

        var result = _drawCommandRecorder.Record(layout, dirtyElementRanges);
        LastDirtyCommandRanges = result.DirtyCommandRanges;
        LastElementCommandRanges = result.ElementCommandRanges;

        return new RenderFrameBatch(result.Commands, BuildHitTargets(layout), result.Resources, result.DirtyCommandRanges);
    }

    private static IReadOnlyList<HitTestTarget> BuildHitTargets(IReadOnlyList<LayoutElement> layoutElements)
    {
        if (layoutElements.Count == 0)
        {
            return [];
        }

        var hitTargetCount = 0;
        foreach (var element in layoutElements)
        {
            if (!string.IsNullOrWhiteSpace(element.ActionId))
            {
                hitTargetCount++;
            }
        }

        if (hitTargetCount == 0)
        {
            return [];
        }

        var hitTargets = new HitTestTarget[hitTargetCount];
        var index = 0;
        foreach (var element in layoutElements)
        {
            if (!string.IsNullOrWhiteSpace(element.ActionId))
            {
                hitTargets[index++] = new HitTestTarget(element.Bounds, element.ActionId);
            }
        }

        return hitTargets;
    }
}
