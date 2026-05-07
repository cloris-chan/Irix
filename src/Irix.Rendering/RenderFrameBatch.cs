using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

public readonly record struct HitTestTarget(PixelRectangle Bounds, string ActionId);

public readonly record struct RenderFrameBatch(
    DrawCommandBatch Commands,
    IReadOnlyList<HitTestTarget> HitTargets,
    IFrameResourceResolver Resources) : IDisposable
{
    public RenderFrameBatch(DrawCommandBatch Commands, IReadOnlyList<HitTestTarget> HitTargets)
        : this(Commands, HitTargets, FrameDrawingResources.Empty)
    {
    }

    public void Dispose()
    {
        Commands.Dispose();
        if (Resources is FrameDrawingResources resources)
        {
            FrameDrawingResources.Return(resources);
        }
    }
}