#if IRIX_DIAGNOSTICS
namespace Irix.Rendering;

internal sealed partial class DrawCommandRecorder
{
    private long _allocationPhaseStart;

    internal DrawCommandRecordAllocationAttribution LastAllocationAttribution { get; private set; }

    partial void OnRecordAllocationStarted()
    {
        LastAllocationAttribution = default;
    }

    partial void OnRecordAllocationPhaseStarted()
    {
        _allocationPhaseStart = GC.GetAllocatedBytesForCurrentThread();
    }

    partial void OnRecordResourcesAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithResources(Delta());
    }

    partial void OnRecordStylesAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithStyles(Delta());
    }

    partial void OnRecordCommandBuildAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithCommandBuild(Delta());
    }

    partial void OnRecordDirtyRangesAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithDirtyRanges(Delta());
    }

    private long Delta() => GC.GetAllocatedBytesForCurrentThread() - _allocationPhaseStart;
}

internal readonly struct DrawCommandRecordAllocationAttribution(
    long ResourcesBytes,
    long StylesBytes,
    long CommandBuildBytes,
    long DirtyRangesBytes) : IEquatable<DrawCommandRecordAllocationAttribution>
{
    public long ResourcesBytes { get; } = ResourcesBytes;
    public long StylesBytes { get; } = StylesBytes;
    public long CommandBuildBytes { get; } = CommandBuildBytes;
    public long DirtyRangesBytes { get; } = DirtyRangesBytes;
    public long TotalBytes => ResourcesBytes + StylesBytes + CommandBuildBytes + DirtyRangesBytes;

    public DrawCommandRecordAllocationAttribution Add(DrawCommandRecordAllocationAttribution other) =>
        new(
            ResourcesBytes + other.ResourcesBytes,
            StylesBytes + other.StylesBytes,
            CommandBuildBytes + other.CommandBuildBytes,
            DirtyRangesBytes + other.DirtyRangesBytes);

    public DrawCommandRecordAllocationAttribution WithResources(long bytes) => new(ResourcesBytes + bytes, StylesBytes, CommandBuildBytes, DirtyRangesBytes);

    public DrawCommandRecordAllocationAttribution WithStyles(long bytes) => new(ResourcesBytes, StylesBytes + bytes, CommandBuildBytes, DirtyRangesBytes);

    public DrawCommandRecordAllocationAttribution WithCommandBuild(long bytes) => new(ResourcesBytes, StylesBytes, CommandBuildBytes + bytes, DirtyRangesBytes);

    public DrawCommandRecordAllocationAttribution WithDirtyRanges(long bytes) => new(ResourcesBytes, StylesBytes, CommandBuildBytes, DirtyRangesBytes + bytes);

    public bool Equals(DrawCommandRecordAllocationAttribution other)
    {
        return ResourcesBytes == other.ResourcesBytes
            && StylesBytes == other.StylesBytes
            && CommandBuildBytes == other.CommandBuildBytes
            && DirtyRangesBytes == other.DirtyRangesBytes;
    }

    public override bool Equals(object? obj) => obj is DrawCommandRecordAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ResourcesBytes, StylesBytes, CommandBuildBytes, DirtyRangesBytes);
}
#endif
