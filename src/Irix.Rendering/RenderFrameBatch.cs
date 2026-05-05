using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

public readonly record struct HitTestTarget(PixelRectangle Bounds, string ActionId);

public readonly record struct RenderFrameBatch(
    DrawCommandBatch Commands,
    IReadOnlyList<HitTestTarget> HitTargets,
    IReadOnlyList<TextRunEntry> TextRuns) : IDisposable
{
    public RenderFrameBatch(DrawCommandBatch Commands, IReadOnlyList<HitTestTarget> HitTargets)
        : this(Commands, HitTargets, [])
    {
    }

    public void Dispose()
    {
        Commands.Dispose();
    }
}