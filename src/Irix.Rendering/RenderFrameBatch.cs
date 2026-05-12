using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

public readonly record struct HitTestTarget(PixelRectangle Bounds, string ActionId, PixelRectangle ClipBounds = default)
{
    public HitTestTarget Scale(DisplayScale scale)
    {
        if (scale.IsIdentity) return this;
        return this with
        {
            Bounds = new PixelRectangle(
                (int)(Bounds.X * scale.ScaleX),
                (int)(Bounds.Y * scale.ScaleY),
                (int)(Bounds.Width * scale.ScaleX),
                (int)(Bounds.Height * scale.ScaleY)),
            ClipBounds = new PixelRectangle(
                (int)(ClipBounds.X * scale.ScaleX),
                (int)(ClipBounds.Y * scale.ScaleY),
                (int)(ClipBounds.Width * scale.ScaleX),
                (int)(ClipBounds.Height * scale.ScaleY))
        };
    }
}

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