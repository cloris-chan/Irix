using Irix.Drawing;

namespace Irix.Rendering;

internal sealed class RetainedResourceSnapshot(IFrameResourceResolver resolver, ulong frameId, Action? retain = null, Action? release = null) : IDisposable
{
    private readonly Action? _retain = retain;
    private readonly Action? _release = release;
    private bool _retained;
    private bool _disposed;

    public IFrameResourceResolver Resolver { get; } = resolver;

    public ulong FrameId { get; } = frameId;

    public static RetainedResourceSnapshot Capture(IFrameResourceResolver resolver, Action? retain = null, Action? release = null)
    {
        var frameId = resolver is FrameDrawingResources frameResources ? frameResources.FrameId : 0ul;
        return new RetainedResourceSnapshot(resolver, frameId, retain, release);
    }

    public void Retain()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RetainedResourceSnapshot));
        }

        if (_retained)
        {
            return;
        }

        if (Resolver is FrameDrawingResources frameResources)
        {
            frameResources.Retain();
        }

        _retain?.Invoke();
        _retained = true;
    }

    public void Release()
    {
        if (!_retained)
        {
            return;
        }

        _retained = false;
        if (Resolver is FrameDrawingResources frameResources)
        {
            frameResources.Release();
        }

        _release?.Invoke();
    }

    public bool MatchesResolverScope(IFrameResourceResolver candidate)
    {
        if (Resolver is FrameDrawingResources retainedFrameResources && candidate is FrameDrawingResources candidateFrameResources)
        {
            return ReferenceEquals(retainedFrameResources, candidateFrameResources)
                && FrameId == candidateFrameResources.FrameId;
        }

        return ReferenceEquals(Resolver, candidate);
    }

    public void Dispose()
    {
        Release();
        _disposed = true;
    }
}

internal readonly record struct RetainedResourceSegment(int CommandStart, int CommandCount, RetainedResourceSnapshot Snapshot)
{
    public int CommandEnd => CommandStart + CommandCount;
}

internal sealed class RetainedResourceSegmentTable : IDisposable
{
    private RetainedResourceSegment[] _segments = [];
    private bool _disposed;

    public IReadOnlyList<RetainedResourceSegment> Segments => _segments;

    public void ApplyFull(int commandCount, RetainedResourceSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (commandCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandCount));
        }

        ReplaceSegments([new RetainedResourceSegment(0, commandCount, snapshot)], retainBeforeRelease: false);
    }

    public bool TryAcceptPartial(IReadOnlyList<(int Start, int Count)> dirtyCommandRanges, RetainedResourceSnapshot replacementSnapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_segments.Length == 0 || !TryMergeValidRanges(dirtyCommandRanges, out var ranges) || !RangesFitCurrentFrame(ranges))
        {
            return false;
        }

        var nextSegments = new List<RetainedResourceSegment>(_segments.Length + ranges.Count);
        foreach (var segment in _segments)
        {
            AddSurvivingPieces(segment, ranges, nextSegments);
        }

        foreach (var (start, count) in ranges)
        {
            nextSegments.Add(new RetainedResourceSegment(start, count, replacementSnapshot));
        }

        nextSegments.Sort((left, right) => left.CommandStart.CompareTo(right.CommandStart));
        ReplaceSegments(MergeAdjacentSegments(nextSegments), retainBeforeRelease: true);
        return true;
    }

    public bool TryGetSnapshotForCommand(int commandIndex, out RetainedResourceSnapshot snapshot)
    {
        foreach (var segment in _segments)
        {
            if (commandIndex >= segment.CommandStart && commandIndex < segment.CommandEnd)
            {
                snapshot = segment.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    public void Invalidate()
    {
        ReplaceSegments([], retainBeforeRelease: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Invalidate();
        _disposed = true;
    }

    private static bool TryMergeValidRanges(IReadOnlyList<(int Start, int Count)> dirtyCommandRanges, out IReadOnlyList<(int Start, int Count)> ranges)
    {
        ranges = [];
        if (dirtyCommandRanges.Count == 0)
        {
            return false;
        }

        foreach (var (start, count) in dirtyCommandRanges)
        {
            if (start < 0 || count <= 0)
            {
                return false;
            }
        }

        ranges = RangeUtils.Merge(dirtyCommandRanges);
        return ranges.Count > 0;
    }

    private bool RangesFitCurrentFrame(IReadOnlyList<(int Start, int Count)> ranges)
    {
        var commandCount = _segments.Length == 0 ? 0 : _segments[^1].CommandEnd;
        foreach (var (start, count) in ranges)
        {
            if (start > commandCount - count)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddSurvivingPieces(RetainedResourceSegment segment, IReadOnlyList<(int Start, int Count)> ranges, List<RetainedResourceSegment> output)
    {
        var cursor = segment.CommandStart;
        foreach (var (rangeStart, rangeCount) in ranges)
        {
            var rangeEnd = rangeStart + rangeCount;
            if (rangeEnd <= cursor)
            {
                continue;
            }

            if (rangeStart >= segment.CommandEnd)
            {
                break;
            }

            var overlapStart = Math.Max(cursor, rangeStart);
            if (cursor < overlapStart)
            {
                output.Add(new RetainedResourceSegment(cursor, overlapStart - cursor, segment.Snapshot));
            }

            cursor = Math.Min(segment.CommandEnd, rangeEnd);
        }

        if (cursor < segment.CommandEnd)
        {
            output.Add(new RetainedResourceSegment(cursor, segment.CommandEnd - cursor, segment.Snapshot));
        }
    }

    private static RetainedResourceSegment[] MergeAdjacentSegments(List<RetainedResourceSegment> segments)
    {
        if (segments.Count <= 1)
        {
            return [.. segments];
        }

        var merged = new List<RetainedResourceSegment> { segments[0] };
        for (var i = 1; i < segments.Count; i++)
        {
            var last = merged[^1];
            var current = segments[i];
            if (last.CommandEnd == current.CommandStart && ReferenceEquals(last.Snapshot, current.Snapshot))
            {
                merged[^1] = last with { CommandCount = last.CommandCount + current.CommandCount };
            }
            else
            {
                merged.Add(current);
            }
        }

        return [.. merged];
    }

    private void ReplaceSegments(RetainedResourceSegment[] nextSegments, bool retainBeforeRelease)
    {
        var currentSnapshots = DistinctSnapshots(_segments);
        var nextSnapshots = DistinctSnapshots(nextSegments);
        if (retainBeforeRelease)
        {
            RetainNewSnapshots(currentSnapshots, nextSnapshots);
            ReleaseRemovedSnapshots(currentSnapshots, nextSnapshots);
        }
        else
        {
            ReleaseRemovedSnapshots(currentSnapshots, nextSnapshots);
            RetainNewSnapshots(currentSnapshots, nextSnapshots);
        }

        _segments = nextSegments;
    }

    private static RetainedResourceSnapshot[] DistinctSnapshots(RetainedResourceSegment[] segments)
    {
        if (segments.Length == 0)
        {
            return [];
        }

        var snapshots = new List<RetainedResourceSnapshot>();
        foreach (var segment in segments)
        {
            if (!ContainsSnapshot(snapshots, segment.Snapshot))
            {
                snapshots.Add(segment.Snapshot);
            }
        }

        return [.. snapshots];
    }

    private static void RetainNewSnapshots(RetainedResourceSnapshot[] currentSnapshots, RetainedResourceSnapshot[] nextSnapshots)
    {
        foreach (var snapshot in nextSnapshots)
        {
            if (!ContainsSnapshot(currentSnapshots, snapshot))
            {
                snapshot.Retain();
            }
        }
    }

    private static void ReleaseRemovedSnapshots(RetainedResourceSnapshot[] currentSnapshots, RetainedResourceSnapshot[] nextSnapshots)
    {
        foreach (var snapshot in currentSnapshots)
        {
            if (!ContainsSnapshot(nextSnapshots, snapshot))
            {
                snapshot.Release();
            }
        }
    }

    private static bool ContainsSnapshot(IReadOnlyList<RetainedResourceSnapshot> snapshots, RetainedResourceSnapshot snapshot)
    {
        foreach (var candidate in snapshots)
        {
            if (ReferenceEquals(candidate, snapshot))
            {
                return true;
            }
        }

        return false;
    }
}