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

internal readonly struct RetainedResourceSegment(int CommandStart, int CommandCount, RetainedResourceSnapshot Snapshot) : IEquatable<RetainedResourceSegment>
{
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public RetainedResourceSnapshot Snapshot { get; } = Snapshot;

    public int CommandEnd => CommandStart + CommandCount;

    public bool Equals(RetainedResourceSegment other)
    {
        return CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && EqualityComparer<RetainedResourceSnapshot>.Default.Equals(Snapshot, other.Snapshot);
    }

    public override bool Equals(object? obj) => obj is RetainedResourceSegment other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CommandStart, CommandCount, Snapshot);

    public static bool operator ==(RetainedResourceSegment left, RetainedResourceSegment right) => left.Equals(right);

    public static bool operator !=(RetainedResourceSegment left, RetainedResourceSegment right) => !left.Equals(right);
}

internal readonly struct SegmentedFrameRead(int CommandStart, DrawCommand[] Commands, IFrameResourceResolver Resolver) : IEquatable<SegmentedFrameRead>
{
    public int CommandStart { get; } = CommandStart;
    public DrawCommand[] Commands { get; } = Commands;
    public IFrameResourceResolver Resolver { get; } = Resolver;

    public bool Equals(SegmentedFrameRead other)
    {
        return CommandStart == other.CommandStart
            && EqualityComparer<DrawCommand[]>.Default.Equals(Commands, other.Commands)
            && EqualityComparer<IFrameResourceResolver>.Default.Equals(Resolver, other.Resolver);
    }

    public override bool Equals(object? obj) => obj is SegmentedFrameRead other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CommandStart, Commands, Resolver);

    public static bool operator ==(SegmentedFrameRead left, SegmentedFrameRead right) => left.Equals(right);

    public static bool operator !=(SegmentedFrameRead left, SegmentedFrameRead right) => !left.Equals(right);
}

internal sealed class SegmentedRetainedFrameReader(RetainedCommandBuffer commandBuffer, RetainedResourceSegmentTable segmentTable)
{
    public IReadOnlyList<SegmentedFrameRead> ReadSegments()
    {
        var commands = commandBuffer.Commands;
        if (commands.Length == 0)
        {
            return [];
        }

        if (segmentTable.Segments.Count == 0)
        {
            throw new InvalidOperationException("Resource segment table is empty for a non-empty retained command buffer.");
        }

        var reads = new List<SegmentedFrameRead>(segmentTable.Segments.Count);
        var cursor = 0;
        foreach (var segment in segmentTable.Segments)
        {
            if (segment.CommandStart < 0 || segment.CommandCount <= 0 || segment.CommandStart > commands.Length - segment.CommandCount)
            {
                throw new InvalidOperationException("Resource segment is outside the retained command buffer.");
            }

            if (segment.CommandStart != cursor)
            {
                throw new InvalidOperationException("Resource segments must cover the retained command buffer contiguously without overlap.");
            }

            reads.Add(new SegmentedFrameRead(
                segment.CommandStart,
                commands.Slice(segment.CommandStart, segment.CommandCount).ToArray(),
                segment.Snapshot.Resolver));
            cursor = segment.CommandEnd;
        }

        if (cursor != commands.Length)
        {
            throw new InvalidOperationException("Resource segment coverage does not match the retained command buffer command count.");
        }

        return reads;
    }
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

    internal void ApplyUncheckedForPreflight(IReadOnlyList<RetainedResourceSegment> segments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReplaceSegments([.. segments], retainBeforeRelease: false);
    }

    public bool TryAcceptPartial(IReadOnlyList<(int Start, int Count)> dirtyCommandRanges, RetainedResourceSnapshot replacementSnapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var commandCount = _segments.Length == 0 ? 0 : _segments[^1].CommandEnd;
        if (_segments.Length == 0 || !RangeUtils.TryNormalizeStrict(dirtyCommandRanges, commandCount, out var ranges))
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
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
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
                merged[^1] = new RetainedResourceSegment(last.CommandStart, last.CommandCount + current.CommandCount, last.Snapshot);
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
