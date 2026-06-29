#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed partial class RenderPipeline
{
    private bool _captureAllocationAttribution;
    private long _allocationPhaseStart;
    private long _snapshotAllocationStart;
    private long _snapshotPhaseStart;
    private RenderPipelineSnapshotAllocationAttribution _snapshotAttribution;

    internal RenderPipelineBuildAllocationAttribution LastAllocationAttribution { get; private set; }

    internal RenderFrameBatch BuildWithAllocationAttribution(
        VirtualNode root,
        PixelRectangle viewportBounds,
        TextBufferSnapshot textSnapshot,
        IReadOnlyList<int>? dirtyNodes,
        TextBufferSnapshot? prevTextSnapshot,
        VirtualNode previousRoot,
        out RenderPipelineBuildAllocationAttribution attribution)
    {
        _captureAllocationAttribution = true;
        try
        {
            var batch = Build(root, viewportBounds, textSnapshot, dirtyNodes, prevTextSnapshot, previousRoot);
            attribution = LastAllocationAttribution;
            return batch;
        }
        finally
        {
            _captureAllocationAttribution = false;
        }
    }

    partial void OnPipelineAllocationStarted()
    {
        if (!_captureAllocationAttribution)
        {
            return;
        }

        LastAllocationAttribution = default;
        _snapshotAttribution = default;
    }

    partial void OnPipelineAllocationPhaseStarted()
    {
        if (_captureAllocationAttribution)
        {
            _allocationPhaseStart = GC.GetAllocatedBytesForCurrentThread();
        }
    }

    partial void OnPipelineClassificationAllocated()
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution.WithClassification(AllocationPhaseDelta());
        }
    }

    partial void OnPipelineStyleOnlyPatchAllocated()
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution.WithStyleOnlyPatch(AllocationPhaseDelta());
        }
    }

    partial void OnPipelineLayoutAllocated(LayoutTreeBuilder layoutTreeBuilder)
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution
                .WithLayout(AllocationPhaseDelta())
                .WithLayoutAttribution(layoutTreeBuilder.LastAllocationAttribution);
        }
    }

    partial void OnPipelineRecordAllocated(DrawCommandRecorder drawCommandRecorder)
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution
                .WithRecord(AllocationPhaseDelta())
                .WithRecordAttribution(drawCommandRecorder.LastAllocationAttribution);
        }
    }

    partial void OnPipelineHitTargetsAllocated()
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution.WithHitTargets(AllocationPhaseDelta());
        }
    }

    partial void OnPipelineSnapshotAllocationStarted()
    {
        if (!_captureAllocationAttribution)
        {
            return;
        }

        _snapshotAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        _snapshotAttribution = default;
    }

    partial void OnPipelineSnapshotPhaseStarted()
    {
        if (_captureAllocationAttribution)
        {
            _snapshotPhaseStart = GC.GetAllocatedBytesForCurrentThread();
        }
    }

    partial void OnPipelineFrameBatchAllocated()
    {
        if (_captureAllocationAttribution)
        {
            _snapshotAttribution = _snapshotAttribution.WithFrameBatch(SnapshotPhaseDelta());
        }
    }

    partial void OnPipelineRetainedInputAllocated()
    {
        if (_captureAllocationAttribution)
        {
            _snapshotAttribution = _snapshotAttribution.WithRetainedInput(SnapshotPhaseDelta());
        }
    }

    partial void OnPipelineSnapshotAllocated()
    {
        if (_captureAllocationAttribution)
        {
            var measured = GC.GetAllocatedBytesForCurrentThread() - _snapshotAllocationStart;
            LastAllocationAttribution = LastAllocationAttribution.WithSnapshot(measured, _snapshotAttribution);
        }
    }

    partial void OnPipelineRetainedFrameAllocated()
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution.WithRetainedFrame(AllocationPhaseDelta());
        }
    }

    private long AllocationPhaseDelta() => GC.GetAllocatedBytesForCurrentThread() - _allocationPhaseStart;

    private long SnapshotPhaseDelta() => GC.GetAllocatedBytesForCurrentThread() - _snapshotPhaseStart;
}

internal readonly struct RenderPipelineBuildAllocationAttribution(
    long ClassificationBytes,
    long LayoutBytes,
    long RecordBytes,
    long HitTargetsBytes,
    long SnapshotBytes,
    long RetainedFrameBytes,
    DrawCommandRecordAllocationAttribution RecordAttribution = default,
    LayoutBuildAllocationAttribution LayoutAttribution = default,
    RenderPipelineSnapshotAllocationAttribution SnapshotAttribution = default,
    long StyleOnlyPatchBytes = 0) : IEquatable<RenderPipelineBuildAllocationAttribution>
{
    public long ClassificationBytes { get; } = ClassificationBytes;
    public long LayoutBytes { get; } = LayoutBytes;
    public long RecordBytes { get; } = RecordBytes;
    public long HitTargetsBytes { get; } = HitTargetsBytes;
    public long SnapshotBytes { get; } = SnapshotBytes;
    public long RetainedFrameBytes { get; } = RetainedFrameBytes;
    public DrawCommandRecordAllocationAttribution RecordAttribution { get; } = RecordAttribution;
    public LayoutBuildAllocationAttribution LayoutAttribution { get; } = LayoutAttribution;
    public RenderPipelineSnapshotAllocationAttribution SnapshotAttribution { get; } = SnapshotAttribution;
    public long StyleOnlyPatchBytes { get; } = StyleOnlyPatchBytes;
    public long TotalBytes => ClassificationBytes + LayoutBytes + StyleOnlyPatchBytes + RecordBytes + HitTargetsBytes + SnapshotBytes + RetainedFrameBytes;

    public RenderPipelineBuildAllocationAttribution Add(RenderPipelineBuildAllocationAttribution other) =>
        new(
            ClassificationBytes + other.ClassificationBytes,
            LayoutBytes + other.LayoutBytes,
            RecordBytes + other.RecordBytes,
            HitTargetsBytes + other.HitTargetsBytes,
            SnapshotBytes + other.SnapshotBytes,
            RetainedFrameBytes + other.RetainedFrameBytes,
            RecordAttribution.Add(other.RecordAttribution),
            LayoutAttribution.Add(other.LayoutAttribution),
            SnapshotAttribution.Add(other.SnapshotAttribution),
            StyleOnlyPatchBytes + other.StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithClassification(long bytes) => new(ClassificationBytes + bytes, LayoutBytes, RecordBytes, HitTargetsBytes, SnapshotBytes, RetainedFrameBytes, RecordAttribution, LayoutAttribution, SnapshotAttribution, StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithLayout(long bytes) => new(ClassificationBytes, LayoutBytes + bytes, RecordBytes, HitTargetsBytes, SnapshotBytes, RetainedFrameBytes, RecordAttribution, LayoutAttribution, SnapshotAttribution, StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithStyleOnlyPatch(long bytes) => new(ClassificationBytes, LayoutBytes, RecordBytes, HitTargetsBytes, SnapshotBytes, RetainedFrameBytes, RecordAttribution, LayoutAttribution, SnapshotAttribution, StyleOnlyPatchBytes + bytes);

    public RenderPipelineBuildAllocationAttribution WithRecord(long bytes) => new(ClassificationBytes, LayoutBytes, RecordBytes + bytes, HitTargetsBytes, SnapshotBytes, RetainedFrameBytes, RecordAttribution, LayoutAttribution, SnapshotAttribution, StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithHitTargets(long bytes) => new(ClassificationBytes, LayoutBytes, RecordBytes, HitTargetsBytes + bytes, SnapshotBytes, RetainedFrameBytes, RecordAttribution, LayoutAttribution, SnapshotAttribution, StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithSnapshot(long bytes) => new(ClassificationBytes, LayoutBytes, RecordBytes, HitTargetsBytes, SnapshotBytes + bytes, RetainedFrameBytes, RecordAttribution, LayoutAttribution, SnapshotAttribution, StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithSnapshot(long bytes, RenderPipelineSnapshotAllocationAttribution attribution) =>
        new(ClassificationBytes, LayoutBytes, RecordBytes, HitTargetsBytes, SnapshotBytes + bytes, RetainedFrameBytes, RecordAttribution, LayoutAttribution, SnapshotAttribution.Add(attribution.WithMeasured(bytes)), StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithRetainedFrame(long bytes) => new(ClassificationBytes, LayoutBytes, RecordBytes, HitTargetsBytes, SnapshotBytes, RetainedFrameBytes + bytes, RecordAttribution, LayoutAttribution, SnapshotAttribution, StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithRecordAttribution(DrawCommandRecordAllocationAttribution attribution) => new(ClassificationBytes, LayoutBytes, RecordBytes, HitTargetsBytes, SnapshotBytes, RetainedFrameBytes, RecordAttribution.Add(attribution), LayoutAttribution, SnapshotAttribution, StyleOnlyPatchBytes);

    public RenderPipelineBuildAllocationAttribution WithLayoutAttribution(LayoutBuildAllocationAttribution attribution) => new(ClassificationBytes, LayoutBytes, RecordBytes, HitTargetsBytes, SnapshotBytes, RetainedFrameBytes, RecordAttribution, LayoutAttribution.Add(attribution), SnapshotAttribution, StyleOnlyPatchBytes);

    public bool Equals(RenderPipelineBuildAllocationAttribution other)
    {
        return ClassificationBytes == other.ClassificationBytes
            && LayoutBytes == other.LayoutBytes
            && StyleOnlyPatchBytes == other.StyleOnlyPatchBytes
            && RecordBytes == other.RecordBytes
            && HitTargetsBytes == other.HitTargetsBytes
            && SnapshotBytes == other.SnapshotBytes
            && RetainedFrameBytes == other.RetainedFrameBytes
            && RecordAttribution.Equals(other.RecordAttribution)
            && LayoutAttribution.Equals(other.LayoutAttribution)
            && SnapshotAttribution.Equals(other.SnapshotAttribution);
    }

    public override bool Equals(object? obj) => obj is RenderPipelineBuildAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ClassificationBytes, LayoutBytes, StyleOnlyPatchBytes, RecordBytes, HitTargetsBytes, SnapshotBytes, RetainedFrameBytes, HashCode.Combine(RecordAttribution, LayoutAttribution, SnapshotAttribution));
}

internal readonly struct RenderPipelineSnapshotAllocationAttribution(
    long FrameBatchBytes,
    long RetainedInputBytes,
    long MeasuredBytes) : IEquatable<RenderPipelineSnapshotAllocationAttribution>
{
    public long FrameBatchBytes { get; } = FrameBatchBytes;
    public long RetainedInputBytes { get; } = RetainedInputBytes;
    public long MeasuredBytes { get; } = MeasuredBytes;
    public long DetailBytes => FrameBatchBytes + RetainedInputBytes;
    public long DetailGapBytes => MeasuredBytes - DetailBytes;

    public RenderPipelineSnapshotAllocationAttribution Add(RenderPipelineSnapshotAllocationAttribution other) =>
        new(
            FrameBatchBytes + other.FrameBatchBytes,
            RetainedInputBytes + other.RetainedInputBytes,
            MeasuredBytes + other.MeasuredBytes);

    public RenderPipelineSnapshotAllocationAttribution WithFrameBatch(long bytes) =>
        new(FrameBatchBytes + bytes, RetainedInputBytes, MeasuredBytes);

    public RenderPipelineSnapshotAllocationAttribution WithRetainedInput(long bytes) =>
        new(FrameBatchBytes, RetainedInputBytes + bytes, MeasuredBytes);

    public RenderPipelineSnapshotAllocationAttribution WithMeasured(long bytes) =>
        new(FrameBatchBytes, RetainedInputBytes, MeasuredBytes + bytes);

    public bool Equals(RenderPipelineSnapshotAllocationAttribution other)
    {
        return FrameBatchBytes == other.FrameBatchBytes
            && RetainedInputBytes == other.RetainedInputBytes
            && MeasuredBytes == other.MeasuredBytes;
    }

    public override bool Equals(object? obj) => obj is RenderPipelineSnapshotAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FrameBatchBytes, RetainedInputBytes, MeasuredBytes);
}
#endif
