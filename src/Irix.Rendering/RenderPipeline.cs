using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed class RenderPipeline(LayoutStyle layoutStyle, DrawingStyle drawingStyle, ControlVisualStateResolver visualStateResolver)
{
    private readonly LayoutTreeBuilder _layoutTreeBuilder = new LayoutTreeBuilder(layoutStyle);
    private readonly DrawCommandRecorder _drawCommandRecorder = new DrawCommandRecorder(drawingStyle, visualStateResolver);

    public RenderPipeline()
        : this(RenderStylePreset.Default)
    {
    }

    public RenderPipeline(RenderStylePreset stylePreset)
        : this(stylePreset.Layout, stylePreset.Drawing, stylePreset.VisualStates)
    {
    }

    public RenderPipeline(LayoutStyle layoutStyle, DrawingStyle drawingStyle)
        : this(layoutStyle, drawingStyle, ControlVisualStateResolver.Default)
    {
    }

    private VirtualNode _retainedRoot;
    private LayoutTreeResult? _retainedLayoutResult;
    private IReadOnlyList<LayoutElement>? _retainedLayout;
    private PixelRectangle _retainedViewport;
    private readonly RetainedRenderFrame _retainedFrame = new();

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
    /// The retained render frame from the last Build call.
    /// Contains the retained command buffer, resource resolver, and dirty ranges.
    /// </summary>
    public RetainedRenderFrame RetainedFrame => _retainedFrame;

    /// <summary>
    /// The layout tree result from the last Build call, if available.
    /// Exposes scroll container diagnostics and tree structure.
    /// </summary>
    public LayoutTreeResult? LastLayoutResult => _retainedLayoutResult;

    /// <summary>
    /// The MaxScrollY from the first ScrollContainer in the last Build call.
    /// 0 if no ScrollContainer or no scroll needed.
    /// </summary>
    public double LastMaxScrollY { get; private set; }

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

            // Extract MaxScrollY from the first ScrollContainer's diagnostics
            LastMaxScrollY = _retainedLayoutResult.ScrollDiagnostics.Count > 0
                ? _retainedLayoutResult.ScrollDiagnostics[0].MaxScrollY
                : 0;
        }

        var layout = _retainedLayout!;
        var dirtyElementRanges = hasDirty && _retainedLayoutResult is not null
            ? _retainedLayoutResult.DirtyElementRanges
            : null;

        LastDirtyElementRanges = dirtyElementRanges ?? [];

        var result = _drawCommandRecorder.Record(layout, dirtyElementRanges);
        LastDirtyCommandRanges = result.DirtyCommandRanges;
        LastElementCommandRanges = result.ElementCommandRanges;

        var batch = new RenderFrameBatch(result.Commands, BuildHitTargets(layout), result.Resources, result.DirtyCommandRanges);

        // Update retained render frame: try partial apply when dirty ranges exist,
        // which only succeeds when resources are the same instance (same frame scope).
        // Falls back to full apply when resources differ or no dirty ranges.
        if (!hasDirty || result.DirtyCommandRanges.Count == 0 || !_retainedFrame.TryApplyPartial(batch))
        {
            _retainedFrame.ApplyFull(batch);
        }

        return batch;
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
                hitTargets[index++] = new HitTestTarget(element.Bounds, element.ActionId, element.ClipBounds);
            }
        }

        return hitTargets;
    }
}
