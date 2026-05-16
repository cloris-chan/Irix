using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

public readonly record struct HitTestTarget(PixelRectangle Bounds, ActionId ActionId, PixelRectangle ClipBounds = default)
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

public struct RenderFrameBatch : IDisposable
{
    private readonly ulong _resourceFrameId;
    private bool _disposed;

    public RenderFrameBatch(
        DrawCommandBatch commands,
        IReadOnlyList<HitTestTarget> hitTargets,
        IFrameResourceResolver resources,
        IReadOnlyList<(int Start, int Count)> dirtyCommandRanges)
    {
        Commands = commands;
        HitTargets = hitTargets;
        Resources = resources;
        DirtyCommandRanges = dirtyCommandRanges;
        _resourceFrameId = resources is FrameDrawingResources frameResources ? frameResources.FrameId : 0;

        if (resources is FrameDrawingResources retainedResources)
        {
            retainedResources.Retain();
        }
    }

    public RenderFrameBatch(DrawCommandBatch commands, IReadOnlyList<HitTestTarget> hitTargets)
        : this(commands, hitTargets, FrameDrawingResources.Empty, [])
    {
    }

    public RenderFrameBatch(DrawCommandBatch commands, IReadOnlyList<HitTestTarget> hitTargets, IFrameResourceResolver resources)
        : this(commands, hitTargets, resources, [])
    {
    }

    public DrawCommandBatch Commands { get; }
    public IReadOnlyList<HitTestTarget> HitTargets { get; }
    public IFrameResourceResolver Resources { get; }
    public IReadOnlyList<(int Start, int Count)> DirtyCommandRanges { get; }
    internal readonly ulong ResourceFrameId => _resourceFrameId;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Commands.Dispose();
        if (Resources is FrameDrawingResources resources)
        {
            resources.Release(_resourceFrameId);
        }
    }
}
