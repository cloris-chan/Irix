namespace Irix.Rendering;

internal enum RetainedRenderFrameHandoffCounterId : byte
{
    RenderCount,
    FullApplyCount,
    PartialApplyCount,
    EmptyFrameCount,
    LastDirtyCommandRanges,
    LastPartialApplySucceeded
}

internal readonly struct RetainedRenderFrameHandoffCounterSemantic(
    RetainedRenderFrameHandoffCounterId CounterId,
    bool ExistingCounterBehaviorUnchanged) : IEquatable<RetainedRenderFrameHandoffCounterSemantic>
{
    public RetainedRenderFrameHandoffCounterId CounterId { get; } = CounterId;
    public bool ExistingCounterBehaviorUnchanged { get; } = ExistingCounterBehaviorUnchanged;

    public bool Equals(RetainedRenderFrameHandoffCounterSemantic other)
    {
        return CounterId == other.CounterId
            && ExistingCounterBehaviorUnchanged == other.ExistingCounterBehaviorUnchanged;
    }

    public override bool Equals(object? obj) => obj is RetainedRenderFrameHandoffCounterSemantic other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CounterId, ExistingCounterBehaviorUnchanged);

    public static bool operator ==(RetainedRenderFrameHandoffCounterSemantic left, RetainedRenderFrameHandoffCounterSemantic right) => left.Equals(right);

    public static bool operator !=(RetainedRenderFrameHandoffCounterSemantic left, RetainedRenderFrameHandoffCounterSemantic right) => !left.Equals(right);
}

internal static class RetainedRenderFrameHandoffCounterSemantics
{
    public static IReadOnlyList<RetainedRenderFrameHandoffCounterSemantic> All { get; } =
    [
        new(RetainedRenderFrameHandoffCounterId.RenderCount, true),
        new(RetainedRenderFrameHandoffCounterId.FullApplyCount, true),
        new(RetainedRenderFrameHandoffCounterId.PartialApplyCount, true),
        new(RetainedRenderFrameHandoffCounterId.EmptyFrameCount, true),
        new(RetainedRenderFrameHandoffCounterId.LastDirtyCommandRanges, true),
        new(RetainedRenderFrameHandoffCounterId.LastPartialApplySucceeded, true)
    ];
}

internal readonly struct SegmentedBackendDirtyRangeHandoffSegment(
    int CommandStart,
    int CommandCount,
    IReadOnlyList<(int Start, int Count)> SegmentDirtyRanges) : IEquatable<SegmentedBackendDirtyRangeHandoffSegment>
{
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public IReadOnlyList<(int Start, int Count)> SegmentDirtyRanges { get; } = SegmentDirtyRanges;

    public bool Equals(SegmentedBackendDirtyRangeHandoffSegment other)
    {
        return CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && EqualityComparer<IReadOnlyList<(int Start, int Count)>>.Default.Equals(SegmentDirtyRanges, other.SegmentDirtyRanges);
    }

    public override bool Equals(object? obj) => obj is SegmentedBackendDirtyRangeHandoffSegment other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CommandStart, CommandCount, SegmentDirtyRanges);

    public static bool operator ==(SegmentedBackendDirtyRangeHandoffSegment left, SegmentedBackendDirtyRangeHandoffSegment right) => left.Equals(right);

    public static bool operator !=(SegmentedBackendDirtyRangeHandoffSegment left, SegmentedBackendDirtyRangeHandoffSegment right) => !left.Equals(right);
}

internal static class SegmentedBackendDirtyRangeHandoffPlanner
{
    public static IReadOnlyList<SegmentedBackendDirtyRangeHandoffSegment> Plan(
        IReadOnlyList<SegmentedFrameRead> reads,
        IReadOnlyList<(int Start, int Count)> retainedFrameDirtyRanges)
    {
        if (reads.Count == 0)
        {
            return [];
        }

        var segments = new SegmentedBackendDirtyRangeHandoffSegment[reads.Count];
        for (var segmentIndex = 0; segmentIndex < reads.Count; segmentIndex++)
        {
            var read = reads[segmentIndex];
            segments[segmentIndex] = new SegmentedBackendDirtyRangeHandoffSegment(
                read.CommandStart,
                read.Commands.Length,
                IntersectDirtyRanges(read.CommandStart, read.Commands.Length, retainedFrameDirtyRanges));
        }

        return segments;
    }

    private static IReadOnlyList<(int Start, int Count)> IntersectDirtyRanges(
        int segmentStart,
        int segmentCommandCount,
        IReadOnlyList<(int Start, int Count)> retainedFrameDirtyRanges)
    {
        if (segmentCommandCount <= 0 || retainedFrameDirtyRanges.Count == 0)
        {
            return [];
        }

        var segmentEnd = segmentStart + segmentCommandCount;
        var segmentDirtyRanges = new List<(int Start, int Count)>();
        foreach (var (dirtyStart, dirtyCount) in retainedFrameDirtyRanges)
        {
            if (dirtyCount <= 0)
            {
                continue;
            }

            var dirtyEnd = dirtyStart + dirtyCount;
            var intersectionStart = Math.Max(segmentStart, dirtyStart);
            var intersectionEnd = Math.Min(segmentEnd, dirtyEnd);
            if (intersectionStart >= intersectionEnd)
            {
                continue;
            }

            segmentDirtyRanges.Add((intersectionStart - segmentStart, intersectionEnd - intersectionStart));
        }

        return RangeUtils.Merge(segmentDirtyRanges);
    }
}
