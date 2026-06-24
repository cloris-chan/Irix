#if IRIX_DIAGNOSTICS
namespace Irix;

internal sealed partial class VirtualTextArena
{
    internal TextBufferSnapshot GetOrCreateSnapshotWithAllocationAttribution(out TextBufferSnapshotAllocationAttribution attribution)
    {
        attribution = default;
        if (_cachedSnapshot is { } cached)
        {
            return cached;
        }

        var beforeTotal = GC.GetTotalAllocatedBytes(false);
        var beforeCharBuffer = GC.GetTotalAllocatedBytes(false);
        char[] buffer = [.. _buffer];
        attribution = attribution.WithCharBuffer(GC.GetTotalAllocatedBytes(false) - beforeCharBuffer);

        var beforeSnapshotShell = GC.GetTotalAllocatedBytes(false);
        var snapshot = new TextBufferSnapshot(CurrentBufferId, buffer);
        _cachedSnapshot = snapshot;
        attribution = attribution.WithSnapshotShell(GC.GetTotalAllocatedBytes(false) - beforeSnapshotShell);
        attribution = attribution.WithMeasured(GC.GetTotalAllocatedBytes(false) - beforeTotal);
        return snapshot;
    }
}

internal readonly struct TextBufferSnapshotAllocationAttribution(
    long CharBufferBytes,
    long SnapshotShellBytes,
    long MeasuredBytes) : IEquatable<TextBufferSnapshotAllocationAttribution>
{
    public long CharBufferBytes { get; } = CharBufferBytes;
    public long SnapshotShellBytes { get; } = SnapshotShellBytes;
    public long MeasuredBytes { get; } = MeasuredBytes;
    public long DetailBytes => CharBufferBytes + SnapshotShellBytes;
    public long DetailGapBytes => MeasuredBytes - DetailBytes;

    public TextBufferSnapshotAllocationAttribution Add(TextBufferSnapshotAllocationAttribution other) =>
        new(
            CharBufferBytes + other.CharBufferBytes,
            SnapshotShellBytes + other.SnapshotShellBytes,
            MeasuredBytes + other.MeasuredBytes);

    public TextBufferSnapshotAllocationAttribution WithCharBuffer(long bytes) =>
        new(CharBufferBytes + bytes, SnapshotShellBytes, MeasuredBytes);

    public TextBufferSnapshotAllocationAttribution WithSnapshotShell(long bytes) =>
        new(CharBufferBytes, SnapshotShellBytes + bytes, MeasuredBytes);

    public TextBufferSnapshotAllocationAttribution WithMeasured(long bytes) =>
        new(CharBufferBytes, SnapshotShellBytes, MeasuredBytes + bytes);

    public bool Equals(TextBufferSnapshotAllocationAttribution other)
    {
        return CharBufferBytes == other.CharBufferBytes
            && SnapshotShellBytes == other.SnapshotShellBytes
            && MeasuredBytes == other.MeasuredBytes;
    }

    public override bool Equals(object? obj) => obj is TextBufferSnapshotAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CharBufferBytes, SnapshotShellBytes, MeasuredBytes);
}
#endif
