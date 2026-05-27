using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

public readonly struct HitTestTarget(
    PixelRectangle Bounds,
    ActionId ActionId,
    PixelRectangle ClipBounds = default,
    int CommandStart = -1,
    int CommandCount = 0) : IEquatable<HitTestTarget>
{

    public PixelRectangle Bounds { get; } = Bounds;
    public ActionId ActionId { get; } = ActionId;
    public PixelRectangle ClipBounds { get; } = ClipBounds;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;

    internal bool HasCommandRange => CommandStart >= 0 && CommandCount > 0;

    public HitTestTarget Scale(DisplayScale scale)
    {
        scale = scale.Normalize();
        if (scale.IsIdentity) return this;
        return new HitTestTarget(
            new PixelRectangle(
                (int)(Bounds.X * scale.ScaleX),
                (int)(Bounds.Y * scale.ScaleY),
                (int)(Bounds.Width * scale.ScaleX),
                (int)(Bounds.Height * scale.ScaleY)),
            ActionId,
            new PixelRectangle(
                (int)(ClipBounds.X * scale.ScaleX),
                (int)(ClipBounds.Y * scale.ScaleY),
                (int)(ClipBounds.Width * scale.ScaleX),
                (int)(ClipBounds.Height * scale.ScaleY)),
            CommandStart,
            CommandCount);
    }

    public bool Equals(HitTestTarget other)
    {
        return Bounds == other.Bounds
            && ActionId.Equals(other.ActionId)
            && ClipBounds == other.ClipBounds
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount;
    }

    public override bool Equals(object? obj) => obj is HitTestTarget other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Bounds, ActionId, ClipBounds, CommandStart, CommandCount);

    public static bool operator ==(HitTestTarget left, HitTestTarget right) => left.Equals(right);

    public static bool operator !=(HitTestTarget left, HitTestTarget right) => !left.Equals(right);
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
