namespace Irix.Rendering;

internal readonly record struct RetainedRenderFrameHandoffCounterSemantic(
    string CounterName,
    string CurrentMeaning,
    string HandoffRule,
    string InternalOnlyCounterRule,
    bool ExistingCounterBehaviorUnchanged);

internal static class RetainedRenderFrameHandoffCounterSemantics
{
    public static IReadOnlyList<RetainedRenderFrameHandoffCounterSemantic> All { get; } =
    [
        new(
            "RenderCount",
            "Non-empty frames rendered by DrawingBackendCompositor.",
            "Future handoff keeps this as rendered non-empty frame count, independent of how many retained segments execute.",
            "Segmented owner rehearsals do not increment RenderCount.",
            true),
        new(
            "FullApplyCount",
            "Renders where DrawingBackendCompositor full-applied its retained frame.",
            "Future handoff keeps this tied to production render-source full apply only.",
            "Secondary owner fallback rehearsal needs an internal-only fallback counter until promotion.",
            true),
        new(
            "PartialApplyCount",
            "Renders where DrawingBackendCompositor partial-applied its retained frame.",
            "Future handoff may increment this only after segmented ownership is the approved production render source.",
            "Secondary owner accepted-partial rehearsal needs an internal-only partial counter until promotion.",
            true),
        new(
            "EmptyFrameCount",
            "Empty batches received by DrawingBackendCompositor.",
            "Future handoff keeps this tied to empty input batches, not owner rebuilds.",
            "Secondary owner empty rebuilds do not increment EmptyFrameCount.",
            true),
        new(
            "LastDirtyCommandRanges",
            "Dirty ranges applied to the current production retained frame.",
            "Future handoff keeps disabled mode identical and exposes segment-local dirty ranges only behind the handoff option.",
            "Segmented owner dirty-range rehearsal uses an internal per-segment plan.",
            true),
        new(
            "LastPartialApplySucceeded",
            "Whether the last compositor render used production retained-frame partial apply.",
            "Future handoff may report segmented partial success only after the segmented owner is the approved production render source.",
            "Secondary owner accepted-partial rehearsal remains internal-only until promotion.",
            true)
    ];
}

internal readonly record struct SegmentedBackendDirtyRangeHandoffSegment(
    int CommandStart,
    int CommandCount,
    IReadOnlyList<(int Start, int Count)> SegmentDirtyRanges);

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