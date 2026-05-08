using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

public readonly record struct HitTestTarget(PixelRectangle Bounds, string ActionId, PixelRectangle ClipBounds = default);

public readonly record struct RenderFrameBatch(
    DrawCommandBatch Commands,
    IReadOnlyList<HitTestTarget> HitTargets,
    IFrameResourceResolver Resources,
    IReadOnlyList<(int Start, int Count)> DirtyCommandRanges) : IDisposable
{
    public RenderFrameBatch(DrawCommandBatch Commands, IReadOnlyList<HitTestTarget> HitTargets)
        : this(Commands, HitTargets, FrameDrawingResources.Empty, [])
    {
    }

    public RenderFrameBatch(DrawCommandBatch Commands, IReadOnlyList<HitTestTarget> HitTargets, IFrameResourceResolver Resources)
        : this(Commands, HitTargets, Resources, [])
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