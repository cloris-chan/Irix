using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed class RenderPipeline(LayoutStyle layoutStyle, DrawingStyle drawingStyle)
{
    private readonly LayoutTreeBuilder _layoutTreeBuilder = new LayoutTreeBuilder(layoutStyle);
    private readonly DrawCommandRecorder _drawCommandRecorder = new DrawCommandRecorder(drawingStyle);

    private VirtualNode _retainedRoot;
    private IReadOnlyList<LayoutElement>? _retainedLayout;
    private PixelRectangle _retainedViewport;

    public RenderPipeline()
        : this(LayoutStyle.Default, DrawingStyle.Default)
    {
    }

    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds)
    {
        var treeChanged = _retainedLayout is null || !VirtualNodeDiffer.NodesEqual(_retainedRoot, root);
        var viewportChanged = _retainedViewport != viewportBounds;

        if (treeChanged || viewportChanged)
        {
            _retainedLayout = _layoutTreeBuilder.Build(root, viewportBounds);
            _retainedRoot = root;
            _retainedViewport = viewportBounds;
        }

        var result = _drawCommandRecorder.Record(_retainedLayout);
        return new RenderFrameBatch(result.Commands, BuildHitTargets(_retainedLayout), result.TextRuns);
    }

    private static IReadOnlyList<HitTestTarget> BuildHitTargets(IReadOnlyList<LayoutElement> layoutElements)
    {
        if (layoutElements.Count == 0)
        {
            return [];
        }

        var hitTargets = new List<HitTestTarget>();

        foreach (var element in layoutElements)
        {
            if (!string.IsNullOrWhiteSpace(element.ActionId))
            {
                hitTargets.Add(new HitTestTarget(element.Bounds, element.ActionId));
            }
        }

        return [.. hitTargets];
    }
}
