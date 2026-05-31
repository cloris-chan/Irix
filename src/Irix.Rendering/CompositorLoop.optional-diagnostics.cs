#if IRIX_DIAGNOSTICS
namespace Irix.Rendering;

internal enum ScrollPresentationCancellationReason : byte
{
    None,
    Explicit,
    RenderInvalidation,
    Dispose
}

internal readonly struct ScrollPresentationCancellationSnapshot(
    ScrollPresentationCancellationReason LastReason,
    CompositionRenderInvalidationKind LastInvalidationKind,
    long ExplicitCount,
    long RenderInvalidationCount,
    long DisposeCount) : IEquatable<ScrollPresentationCancellationSnapshot>
{
    public ScrollPresentationCancellationReason LastReason { get; } = LastReason;
    public CompositionRenderInvalidationKind LastInvalidationKind { get; } = LastInvalidationKind;
    public long ExplicitCount { get; } = ExplicitCount;
    public long RenderInvalidationCount { get; } = RenderInvalidationCount;
    public long DisposeCount { get; } = DisposeCount;

    public bool Equals(ScrollPresentationCancellationSnapshot other)
    {
        return LastReason == other.LastReason
            && LastInvalidationKind == other.LastInvalidationKind
            && ExplicitCount == other.ExplicitCount
            && RenderInvalidationCount == other.RenderInvalidationCount
            && DisposeCount == other.DisposeCount;
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationCancellationSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LastReason, LastInvalidationKind, ExplicitCount, RenderInvalidationCount, DisposeCount);

    public static bool operator ==(ScrollPresentationCancellationSnapshot left, ScrollPresentationCancellationSnapshot right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationCancellationSnapshot left, ScrollPresentationCancellationSnapshot right) => !left.Equals(right);
}

public sealed partial class CompositorLoop
{
    private ScrollPresentationCancellationReason _lastScrollPresentationCancellationReason;
    private CompositionRenderInvalidationKind _lastScrollPresentationCancellationInvalidationKind;
    private long _explicitScrollPresentationCancellationCount;
    private long _renderInvalidationScrollPresentationCancellationCount;
    private long _disposeScrollPresentationCancellationCount;

    internal ScrollPresentationCancellationSnapshot ScrollPresentationCancellationDiagnostics => new(
        _lastScrollPresentationCancellationReason,
        _lastScrollPresentationCancellationInvalidationKind,
        Volatile.Read(ref _explicitScrollPresentationCancellationCount),
        Volatile.Read(ref _renderInvalidationScrollPresentationCancellationCount),
        Volatile.Read(ref _disposeScrollPresentationCancellationCount));

    partial void RecordScrollPresentationCancellation(
        byte reason,
        CompositionRenderInvalidationKind invalidationKind,
        bool canceled)
    {
        if (!canceled)
        {
            return;
        }

        var typedReason = ToScrollPresentationCancellationReason(reason);
        _lastScrollPresentationCancellationReason = typedReason;
        _lastScrollPresentationCancellationInvalidationKind = invalidationKind;
        switch (typedReason)
        {
            case ScrollPresentationCancellationReason.Explicit:
                Interlocked.Increment(ref _explicitScrollPresentationCancellationCount);
                break;
            case ScrollPresentationCancellationReason.RenderInvalidation:
                Interlocked.Increment(ref _renderInvalidationScrollPresentationCancellationCount);
                break;
            case ScrollPresentationCancellationReason.Dispose:
                Interlocked.Increment(ref _disposeScrollPresentationCancellationCount);
                break;
        }
    }

    private static ScrollPresentationCancellationReason ToScrollPresentationCancellationReason(byte reason)
    {
        return reason switch
        {
            1 => ScrollPresentationCancellationReason.Explicit,
            2 => ScrollPresentationCancellationReason.RenderInvalidation,
            3 => ScrollPresentationCancellationReason.Dispose,
            _ => ScrollPresentationCancellationReason.None
        };
    }
}
#endif
