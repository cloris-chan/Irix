using Irix.Drawing;

namespace Irix.Rendering;

internal readonly struct RetainedRenderFrameHandoffHarnessOptions(bool EnableSegmentedRenderSourceCandidate) : IEquatable<RetainedRenderFrameHandoffHarnessOptions>
{
    public bool EnableSegmentedRenderSourceCandidate { get; } = EnableSegmentedRenderSourceCandidate;

    public static RetainedRenderFrameHandoffHarnessOptions Disabled => default;

    public static RetainedRenderFrameHandoffHarnessOptions Enabled => new(true);

    public bool Equals(RetainedRenderFrameHandoffHarnessOptions other)
    {
        return EnableSegmentedRenderSourceCandidate == other.EnableSegmentedRenderSourceCandidate;
    }

    public override bool Equals(object? obj) => obj is RetainedRenderFrameHandoffHarnessOptions other && Equals(other);

    public override int GetHashCode() => EnableSegmentedRenderSourceCandidate.GetHashCode();

    public static bool operator ==(RetainedRenderFrameHandoffHarnessOptions left, RetainedRenderFrameHandoffHarnessOptions right) => left.Equals(right);

    public static bool operator !=(RetainedRenderFrameHandoffHarnessOptions left, RetainedRenderFrameHandoffHarnessOptions right) => !left.Equals(right);
}

internal enum RetainedRenderFrameHandoffHarnessResultKind : byte
{
    Disabled,
    MissingSegmentedOwner,
    EmptyFrame,
    Executed
}

internal readonly struct RetainedRenderFrameHandoffHarnessCounters(
    long RenderCount,
    long FullApplyCount,
    long PartialApplyCount,
    long EmptyFrameCount,
    IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges,
    bool LastPartialApplySucceeded) : IEquatable<RetainedRenderFrameHandoffHarnessCounters>
{
    public long RenderCount { get; } = RenderCount;
    public long FullApplyCount { get; } = FullApplyCount;
    public long PartialApplyCount { get; } = PartialApplyCount;
    public long EmptyFrameCount { get; } = EmptyFrameCount;
    public IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges { get; } = LastDirtyCommandRanges;
    public bool LastPartialApplySucceeded { get; } = LastPartialApplySucceeded;

    public bool Equals(RetainedRenderFrameHandoffHarnessCounters other)
    {
        return RenderCount == other.RenderCount
            && FullApplyCount == other.FullApplyCount
            && PartialApplyCount == other.PartialApplyCount
            && EmptyFrameCount == other.EmptyFrameCount
            && EqualityComparer<IReadOnlyList<(int Start, int Count)>>.Default.Equals(LastDirtyCommandRanges, other.LastDirtyCommandRanges)
            && LastPartialApplySucceeded == other.LastPartialApplySucceeded;
    }

    public override bool Equals(object? obj) => obj is RetainedRenderFrameHandoffHarnessCounters other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RenderCount, FullApplyCount, PartialApplyCount, EmptyFrameCount, LastDirtyCommandRanges, LastPartialApplySucceeded);

    public static bool operator ==(RetainedRenderFrameHandoffHarnessCounters left, RetainedRenderFrameHandoffHarnessCounters right) => left.Equals(right);

    public static bool operator !=(RetainedRenderFrameHandoffHarnessCounters left, RetainedRenderFrameHandoffHarnessCounters right) => !left.Equals(right);
}

internal readonly struct RetainedRenderFrameHandoffHarnessResult(
    RetainedRenderFrameHandoffHarnessResultKind Kind,
    IReadOnlyList<SegmentedFrameRead> Reads,
    IReadOnlyList<SegmentedBackendDirtyRangeHandoffSegment> DirtyRangePlan,
    RetainedRenderFrameHandoffHarnessCounters Counters,
    IReadOnlyList<RetainedRenderFrameHandoffCounterSemantic> CounterSemantics) : IEquatable<RetainedRenderFrameHandoffHarnessResult>
{
    public RetainedRenderFrameHandoffHarnessResultKind Kind { get; } = Kind;
    public IReadOnlyList<SegmentedFrameRead> Reads { get; } = Reads;
    public IReadOnlyList<SegmentedBackendDirtyRangeHandoffSegment> DirtyRangePlan { get; } = DirtyRangePlan;
    public RetainedRenderFrameHandoffHarnessCounters Counters { get; } = Counters;
    public IReadOnlyList<RetainedRenderFrameHandoffCounterSemantic> CounterSemantics { get; } = CounterSemantics;

    public static RetainedRenderFrameHandoffHarnessResult Disabled(RetainedRenderFrameHandoffHarnessCounters counters)
    {
        return new RetainedRenderFrameHandoffHarnessResult(
            RetainedRenderFrameHandoffHarnessResultKind.Disabled,
            [],
            [],
            counters,
            RetainedRenderFrameHandoffCounterSemantics.All);
    }

    public bool Equals(RetainedRenderFrameHandoffHarnessResult other)
    {
        return Kind == other.Kind
            && EqualityComparer<IReadOnlyList<SegmentedFrameRead>>.Default.Equals(Reads, other.Reads)
            && EqualityComparer<IReadOnlyList<SegmentedBackendDirtyRangeHandoffSegment>>.Default.Equals(DirtyRangePlan, other.DirtyRangePlan)
            && Counters == other.Counters
            && EqualityComparer<IReadOnlyList<RetainedRenderFrameHandoffCounterSemantic>>.Default.Equals(CounterSemantics, other.CounterSemantics);
    }

    public override bool Equals(object? obj) => obj is RetainedRenderFrameHandoffHarnessResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Reads, DirtyRangePlan, Counters, CounterSemantics);

    public static bool operator ==(RetainedRenderFrameHandoffHarnessResult left, RetainedRenderFrameHandoffHarnessResult right) => left.Equals(right);

    public static bool operator !=(RetainedRenderFrameHandoffHarnessResult left, RetainedRenderFrameHandoffHarnessResult right) => !left.Equals(right);
}

internal sealed class RetainedRenderFrameHandoffHarness(IDrawingBackend backend, RetainedRenderFrameHandoffHarnessOptions options = default) : IDisposable
{
    private HitTestTarget[] _hitTargets = [];
    private IReadOnlyList<(int Start, int Count)> _lastDirtyCommandRanges = [];
    private long _renderCount;
    private long _fullApplyCount;
    private long _partialApplyCount;
    private long _emptyFrameCount;
    private bool _lastPartialApplySucceeded;
    private bool _disposed;

    public RetainedRenderFrameHandoffHarnessResult LastResult { get; private set; } = RetainedRenderFrameHandoffHarnessResult.Disabled(new RetainedRenderFrameHandoffHarnessCounters(0, 0, 0, 0, [], false));

    public RetainedRenderFrameHandoffHarnessCounters Counters => CreateCounterSnapshot();

    public RetainedRenderFrameHandoffHarnessResult ExecuteCandidateFrame(
        RetainedRenderFrameSegmentOwnership ownership,
        in FrameContext frameContext,
        IndexRangeList retainedFrameDirtyRanges)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!options.EnableSegmentedRenderSourceCandidate)
        {
            LastResult = RetainedRenderFrameHandoffHarnessResult.Disabled(CreateCounterSnapshot());
            return LastResult;
        }

        if (ownership.RuntimeOwner is null)
        {
            LastResult = new RetainedRenderFrameHandoffHarnessResult(
                RetainedRenderFrameHandoffHarnessResultKind.MissingSegmentedOwner,
                [],
                [],
                CreateCounterSnapshot(),
                RetainedRenderFrameHandoffCounterSemantics.All);
            return LastResult;
        }

        var reads = ownership.RuntimeOwner.ReadSegments();
        var dirtyRangePlan = SegmentedBackendDirtyRangeHandoffPlanner.Plan(reads, retainedFrameDirtyRanges);
        _lastDirtyCommandRanges = retainedFrameDirtyRanges.ToArray();
        _lastPartialApplySucceeded = ownership.LastResult.Kind == SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial;

        if (reads.Count == 0)
        {
            _emptyFrameCount++;
            _hitTargets = [];
            _lastPartialApplySucceeded = false;
            LastResult = new RetainedRenderFrameHandoffHarnessResult(
                RetainedRenderFrameHandoffHarnessResultKind.EmptyFrame,
                reads,
                dirtyRangePlan,
                CreateCounterSnapshot(),
                RetainedRenderFrameHandoffCounterSemantics.All);
            return LastResult;
        }

        _renderCount++;
        if (_lastPartialApplySucceeded)
        {
            _partialApplyCount++;
        }
        else
        {
            _fullApplyCount++;
        }

        _hitTargets = ownership.RuntimeOwner.HitTargets.ToArray();
        LastResult = new RetainedRenderFrameHandoffHarnessResult(
            RetainedRenderFrameHandoffHarnessResultKind.Executed,
            reads,
            dirtyRangePlan,
            CreateCounterSnapshot(),
            RetainedRenderFrameHandoffCounterSemantics.All);

        var routingBackend = new DirtyRangeRoutingBackend(backend, dirtyRangePlan);
        new SegmentedBackendExecutionAdapter(routingBackend).Execute(frameContext, reads);
        return LastResult;
    }

    /// <summary>
    /// Harness hit test uses retained logical coordinates; production input enters through the compositor physical-pixel API.
    /// </summary>
    public bool TryGetActionIdAtLogicalPixel(int x, int y, out ActionId actionId)
    {
        foreach (var hitTarget in _hitTargets)
        {
            if (x < hitTarget.Bounds.X
                || y < hitTarget.Bounds.Y
                || x >= hitTarget.Bounds.X + hitTarget.Bounds.Width
                || y >= hitTarget.Bounds.Y + hitTarget.Bounds.Height)
            {
                continue;
            }

            if (hitTarget.ClipBounds.Width > 0 && hitTarget.ClipBounds.Height > 0)
            {
                if (x < hitTarget.ClipBounds.X
                    || y < hitTarget.ClipBounds.Y
                    || x >= hitTarget.ClipBounds.X + hitTarget.ClipBounds.Width
                    || y >= hitTarget.ClipBounds.Y + hitTarget.ClipBounds.Height)
                {
                    continue;
                }
            }

            actionId = hitTarget.ActionId;
            return true;
        }

        actionId = ActionId.None;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        backend.Dispose();
        _hitTargets = [];
        _disposed = true;
    }

    private RetainedRenderFrameHandoffHarnessCounters CreateCounterSnapshot()
    {
        return new RetainedRenderFrameHandoffHarnessCounters(
            _renderCount,
            _fullApplyCount,
            _partialApplyCount,
            _emptyFrameCount,
            _lastDirtyCommandRanges.ToArray(),
            _lastPartialApplySucceeded);
    }

    private sealed class DirtyRangeRoutingBackend(
        IDrawingBackend inner,
        IReadOnlyList<SegmentedBackendDirtyRangeHandoffSegment> dirtyRangePlan) : IDrawingBackend
    {
        private int _executeIndex;

        public void BeginFrame(in FrameContext frameContext)
        {
            inner.BeginFrame(frameContext);
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            if (inner is IDirtyRangeAware dirtyRangeAware)
            {
                var ranges = _executeIndex < dirtyRangePlan.Count
                    ? dirtyRangePlan[_executeIndex].SegmentDirtyRanges
                    : [];
                dirtyRangeAware.SetDirtyCommandRanges(ranges);
            }

            _executeIndex++;
            inner.Execute(commands, resources);
        }

        public void EndFrame()
        {
            inner.EndFrame();
        }

        public void Dispose()
        {
        }
    }
}
