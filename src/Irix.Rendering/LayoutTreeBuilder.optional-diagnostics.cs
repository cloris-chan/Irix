#if IRIX_DIAGNOSTICS
namespace Irix.Rendering;

internal sealed partial class LayoutTreeBuilder
{
    private long _allocationPhaseStart;

    internal LayoutBuildAllocationAttribution LastAllocationAttribution { get; private set; }

    partial void OnLayoutAllocationStarted()
    {
        LastAllocationAttribution = default;
    }

    partial void OnLayoutAllocationPhaseStarted()
    {
        _allocationPhaseStart = GC.GetTotalAllocatedBytes(false);
    }

    partial void OnLayoutNodeWalkAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithNodeWalk(Delta());
    }

    partial void OnLayoutDirtyRangesAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithDirtyRanges(Delta());
    }

    partial void OnLayoutElementArrayAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithElementArray(Delta());
    }

    partial void OnLayoutTreeNodeArrayAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithTreeNodeArray(Delta());
    }

    partial void OnLayoutScrollObservationsArrayAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithScrollDiagnosticsArray(Delta());
    }

    partial void OnLayoutResultAllocated()
    {
        LastAllocationAttribution = LastAllocationAttribution.WithResult(Delta());
    }

    private long Delta() => GC.GetTotalAllocatedBytes(false) - _allocationPhaseStart;
}

internal readonly struct LayoutBuildAllocationAttribution(
    long NodeWalkBytes,
    long DirtyRangeBytes,
    long ElementArrayBytes,
    long TreeNodeArrayBytes,
    long ScrollDiagnosticsArrayBytes,
    long ResultBytes) : IEquatable<LayoutBuildAllocationAttribution>
{
    public long NodeWalkBytes { get; } = NodeWalkBytes;
    public long DirtyRangeBytes { get; } = DirtyRangeBytes;
    public long ElementArrayBytes { get; } = ElementArrayBytes;
    public long TreeNodeArrayBytes { get; } = TreeNodeArrayBytes;
    public long ScrollDiagnosticsArrayBytes { get; } = ScrollDiagnosticsArrayBytes;
    public long ResultBytes { get; } = ResultBytes;
    public long TotalBytes => NodeWalkBytes + DirtyRangeBytes + ElementArrayBytes + TreeNodeArrayBytes + ScrollDiagnosticsArrayBytes + ResultBytes;

    public LayoutBuildAllocationAttribution Add(LayoutBuildAllocationAttribution other) =>
        new(
            NodeWalkBytes + other.NodeWalkBytes,
            DirtyRangeBytes + other.DirtyRangeBytes,
            ElementArrayBytes + other.ElementArrayBytes,
            TreeNodeArrayBytes + other.TreeNodeArrayBytes,
            ScrollDiagnosticsArrayBytes + other.ScrollDiagnosticsArrayBytes,
            ResultBytes + other.ResultBytes);

    public LayoutBuildAllocationAttribution WithNodeWalk(long bytes) => new(NodeWalkBytes + bytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithDirtyRanges(long bytes) => new(NodeWalkBytes, DirtyRangeBytes + bytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithElementArray(long bytes) => new(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes + bytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithTreeNodeArray(long bytes) => new(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes + bytes, ScrollDiagnosticsArrayBytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithScrollDiagnosticsArray(long bytes) => new(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes + bytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithResult(long bytes) => new(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes + bytes);

    public bool Equals(LayoutBuildAllocationAttribution other)
    {
        return NodeWalkBytes == other.NodeWalkBytes
            && DirtyRangeBytes == other.DirtyRangeBytes
            && ElementArrayBytes == other.ElementArrayBytes
            && TreeNodeArrayBytes == other.TreeNodeArrayBytes
            && ScrollDiagnosticsArrayBytes == other.ScrollDiagnosticsArrayBytes
            && ResultBytes == other.ResultBytes;
    }

    public override bool Equals(object? obj) => obj is LayoutBuildAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes);
}
#endif
