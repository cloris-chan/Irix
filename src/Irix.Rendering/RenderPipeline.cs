using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed class RenderPipeline
{
    private readonly LayoutTreeBuilder _layoutTreeBuilder;
    private readonly DrawCommandRecorder _drawCommandRecorder;

    public RenderPipeline()
        : this(LayoutStyle.Default, DrawingStyle.Default)
    {
    }

    public RenderPipeline(LayoutStyle layoutStyle, DrawingStyle drawingStyle)
    {
        _layoutTreeBuilder = new LayoutTreeBuilder(layoutStyle);
        _drawCommandRecorder = new DrawCommandRecorder(drawingStyle);
    }

    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds)
    {
        var layoutElements = _layoutTreeBuilder.Build(root, viewportBounds);
        var result = _drawCommandRecorder.Record(layoutElements);
        return new RenderFrameBatch(result.Commands, BuildHitTargets(layoutElements), result.TextRuns);
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
