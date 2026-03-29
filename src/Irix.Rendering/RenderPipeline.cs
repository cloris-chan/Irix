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

    public DrawCommandBatch Build(VirtualNode root, PixelRectangle viewportBounds)
    {
        var layoutElements = _layoutTreeBuilder.Build(root, viewportBounds);
        return _drawCommandRecorder.Record(layoutElements);
    }
}
