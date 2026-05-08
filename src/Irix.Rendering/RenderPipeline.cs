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
    private IReadOnlyList<LayoutElement>? _retainedLayout;
    private PixelRectangle _retainedViewport;

    /// <summary>
    /// Build a render frame for the given root and viewport.
    /// When <paramref name="dirtyNodes"/> is non-null, forces a full layout rebuild
    /// (v0: incremental layout not yet implemented).
    /// When null (render request), reuses the retained layout if tree and viewport match.
    /// </summary>
    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null)
    {
        var treeChanged = _retainedLayout is null || !VirtualNodeDiffer.NodesEqual(_retainedRoot, root);
        var viewportChanged = _retainedViewport != viewportBounds;
        var hasDirty = dirtyNodes is { Count: > 0 };

        if (treeChanged || viewportChanged || hasDirty)
        {
            _retainedLayout = _layoutTreeBuilder.Build(root, viewportBounds, dirtyNodes);
            _retainedRoot = root;
            _retainedViewport = viewportBounds;
        }

        var layout = _retainedLayout!;
        var result = _drawCommandRecorder.Record(layout);
        return new RenderFrameBatch(result.Commands, BuildHitTargets(layout), result.Resources);
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
