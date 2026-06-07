using Irix.Rendering;

namespace Irix.Poc;

internal enum StyleTransitionCompletionPumpResultKind : byte
{
    None,
    NoActiveTransition,
    TickRendered,
    CompletionCommitted,
    TickUnavailable,
}

internal readonly struct StyleTransitionCompletionPumpResult(
    StyleTransitionCompletionPumpResultKind Kind,
    int DrainedEvents = 0,
    StyleTransitionRuntimeResult CommitResult = default) : IEquatable<StyleTransitionCompletionPumpResult>
{
    public StyleTransitionCompletionPumpResultKind Kind { get; } = Kind;
    public int DrainedEvents { get; } = DrainedEvents;
    public StyleTransitionRuntimeResult CommitResult { get; } = CommitResult;
    public bool HasCommit => CommitResult.Kind == StyleTransitionRuntimeResultKind.Committed;

    public static StyleTransitionCompletionPumpResult NoActiveTransition() =>
        new(StyleTransitionCompletionPumpResultKind.NoActiveTransition);

    public static StyleTransitionCompletionPumpResult TickRendered(int drainedEvents) =>
        new(StyleTransitionCompletionPumpResultKind.TickRendered, drainedEvents);

    public static StyleTransitionCompletionPumpResult CompletionCommitted(
        int drainedEvents,
        StyleTransitionRuntimeResult commitResult) =>
        new(StyleTransitionCompletionPumpResultKind.CompletionCommitted, drainedEvents, commitResult);

    public static StyleTransitionCompletionPumpResult TickUnavailable() =>
        new(StyleTransitionCompletionPumpResultKind.TickUnavailable);

    public bool Equals(StyleTransitionCompletionPumpResult other)
    {
        return Kind == other.Kind
            && DrainedEvents == other.DrainedEvents
            && CommitResult == other.CommitResult;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionCompletionPumpResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, DrainedEvents, CommitResult);

    public static bool operator ==(StyleTransitionCompletionPumpResult left, StyleTransitionCompletionPumpResult right) => left.Equals(right);

    public static bool operator !=(StyleTransitionCompletionPumpResult left, StyleTransitionCompletionPumpResult right) => !left.Equals(right);
}

internal readonly struct StyleTransitionCompletionPumpDiagnosticSnapshot(
    bool IsRunning,
    bool HasActiveTransition,
    NodeKey ActiveTargetKey,
    CompositionAnimationInstanceId ActiveInstanceId,
    int ActiveOwnerCount,
    StyleTransitionOwnerKind ActiveOwnerKind,
    StyleTransitionCompletionPumpResult LastResult,
    StyleTransitionCompletionResult TrackerResult,
    long TickCount,
    long CommitCount,
    long DrainedEventCount,
    string? LastErrorKind) : IEquatable<StyleTransitionCompletionPumpDiagnosticSnapshot>
{
    public bool IsRunning { get; } = IsRunning;
    public bool HasActiveTransition { get; } = HasActiveTransition;
    public NodeKey ActiveTargetKey { get; } = ActiveTargetKey;
    public CompositionAnimationInstanceId ActiveInstanceId { get; } = ActiveInstanceId;
    public int ActiveOwnerCount { get; } = ActiveOwnerCount;
    public StyleTransitionOwnerKind ActiveOwnerKind { get; } = ActiveOwnerKind;
    public StyleTransitionCompletionPumpResult LastResult { get; } = LastResult;
    public StyleTransitionCompletionResult TrackerResult { get; } = TrackerResult;
    public long TickCount { get; } = TickCount;
    public long CommitCount { get; } = CommitCount;
    public long DrainedEventCount { get; } = DrainedEventCount;
    public string? LastErrorKind { get; } = LastErrorKind;
    public bool HasError => LastErrorKind is not null;

    public bool Equals(StyleTransitionCompletionPumpDiagnosticSnapshot other)
    {
        return IsRunning == other.IsRunning
            && HasActiveTransition == other.HasActiveTransition
            && ActiveTargetKey == other.ActiveTargetKey
            && ActiveInstanceId == other.ActiveInstanceId
            && ActiveOwnerCount == other.ActiveOwnerCount
            && ActiveOwnerKind == other.ActiveOwnerKind
            && LastResult == other.LastResult
            && TrackerResult == other.TrackerResult
            && TickCount == other.TickCount
            && CommitCount == other.CommitCount
            && DrainedEventCount == other.DrainedEventCount
            && LastErrorKind == other.LastErrorKind;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionCompletionPumpDiagnosticSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsRunning);
        hash.Add(HasActiveTransition);
        hash.Add(ActiveTargetKey);
        hash.Add(ActiveInstanceId);
        hash.Add(ActiveOwnerCount);
        hash.Add(ActiveOwnerKind);
        hash.Add(LastResult);
        hash.Add(TrackerResult);
        hash.Add(TickCount);
        hash.Add(CommitCount);
        hash.Add(DrainedEventCount);
        hash.Add(LastErrorKind);
        return hash.ToHashCode();
    }

    public static bool operator ==(StyleTransitionCompletionPumpDiagnosticSnapshot left, StyleTransitionCompletionPumpDiagnosticSnapshot right) => left.Equals(right);

    public static bool operator !=(StyleTransitionCompletionPumpDiagnosticSnapshot left, StyleTransitionCompletionPumpDiagnosticSnapshot right) => !left.Equals(right);
}

internal sealed class StyleTransitionCompletionPump : IAsyncDisposable
{
    private const int StackEventCapacity = 16;
    private static readonly TimeSpan SoftwareTimerDelay = TimeSpan.FromMilliseconds(4);
    private readonly DrawingBackendCompositor _compositor;
    private readonly StyleTransitionCompletionTracker _tracker;
    private readonly IStyleTransitionCompositorAdapter _styleCompositor;
    private readonly IStyleTransitionRetainedSnapshotProvider _snapshotProvider;
    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();
    private readonly Lock _gate = new();
    private Task? _pumpTask;
    private StyleTransitionCompletionPumpResult _lastResult;

    internal StyleTransitionCompletionPump(
        DrawingBackendCompositor compositor,
        StyleTransitionCompletionTracker tracker,
        IStyleTransitionCompositorAdapter styleCompositor,
        IStyleTransitionRetainedSnapshotProvider snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(styleCompositor);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        _compositor = compositor;
        _tracker = tracker;
        _styleCompositor = styleCompositor;
        _snapshotProvider = snapshotProvider;
    }

    internal long TickCount { get; private set; }

    internal long CommitCount { get; private set; }

    internal long DrainedEventCount { get; private set; }

    internal Exception? LastError { get; private set; }

    internal StyleTransitionCompletionPumpResult LastResult
    {
        get
        {
            lock (_gate)
            {
                return _lastResult;
            }
        }
    }

    internal bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _pumpTask is { IsCompleted: false };
            }
        }
    }

    internal bool EnsureRunning()
    {
        lock (_gate)
        {
            if (_pumpTask is { IsCompleted: false } || !_tracker.HasActiveTransition)
            {
                return false;
            }

            LastError = null;
            _pumpTask = Task.Run(
                () => RunAsync(_disposeCancellationTokenSource.Token),
                _disposeCancellationTokenSource.Token);
            return true;
        }
    }

    internal StyleTransitionCompletionPumpDiagnosticSnapshot CaptureDiagnosticSnapshot()
    {
        var tracker = _tracker.CaptureDiagnosticState();
        lock (_gate)
        {
            return new StyleTransitionCompletionPumpDiagnosticSnapshot(
                _pumpTask is { IsCompleted: false },
                tracker.HasActiveTransition,
                tracker.ActiveTargetKey,
                tracker.ActiveInstanceId,
                tracker.ActiveOwnerCount,
                tracker.ActiveOwnerKind,
                _lastResult,
                tracker.LastResult,
                TickCount,
                CommitCount,
                DrainedEventCount,
                LastError?.GetType().Name);
        }
    }

    internal async ValueTask<StyleTransitionCompletionPumpResult> TickAndDrainAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default)
    {
        if (!_tracker.HasActiveTransition)
        {
            return SetLastResult(StyleTransitionCompletionPumpResult.NoActiveTransition());
        }

        try
        {
            _ = await _compositor.RenderCompositionAnimationTickAtAsync(timestamp, cancellationToken);
            IncrementTickCount();
        }
        catch (InvalidOperationException)
        {
            return SetLastResult(StyleTransitionCompletionPumpResult.TickUnavailable());
        }

        return await DrainAndApplyCompletionAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCancellationTokenSource.Cancel();
        Task? pumpTask;
        lock (_gate)
        {
            pumpTask = _pumpTask;
        }

        if (pumpTask is not null)
        {
            try
            {
                await pumpTask;
            }
            catch (OperationCanceledException) when (_disposeCancellationTokenSource.IsCancellationRequested)
            {
            }
        }

        _disposeCancellationTokenSource.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _tracker.HasActiveTransition)
            {
                var result = await TickAndDrainAtAsync(CompositionTimestamp.Now(), cancellationToken);
                if (result.Kind is StyleTransitionCompletionPumpResultKind.NoActiveTransition
                    or StyleTransitionCompletionPumpResultKind.CompletionCommitted
                    or StyleTransitionCompletionPumpResultKind.TickUnavailable)
                {
                    return;
                }

                if (_compositor.FramePacing == CompositionFramePacing.BackendPresentation)
                {
                    await Task.Yield();
                }
                else
                {
                    await Task.Delay(SoftwareTimerDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetLastError(ex);
        }
    }

    private async ValueTask<StyleTransitionCompletionPumpResult> DrainAndApplyCompletionAsync(CancellationToken cancellationToken)
    {
        var events = new CompositionAnimationMarkerEvent[StackEventCapacity];
        var drainedEvents = 0;
        while (true)
        {
            var count = _compositor.DrainCompositionMarkerEvents(events);
            if (count == 0)
            {
                AddDrainedEventCount(drainedEvents);
                return SetLastResult(StyleTransitionCompletionPumpResult.TickRendered(drainedEvents));
            }

            drainedEvents += count;
            for (var i = 0; i < count; i++)
            {
                if (!_tracker.TryCreateCompletionDecision(events[i], out var commitDecision))
                {
                    continue;
                }

                var commitResult = await StyleTransitionRuntimeCoordinator.ApplyDecisionAsync(
                    commitDecision,
                    _styleCompositor,
                    _snapshotProvider,
                    cancellationToken);
                IncrementCommitCount();
                AddDrainedEventCount(drainedEvents);
                return SetLastResult(StyleTransitionCompletionPumpResult.CompletionCommitted(drainedEvents, commitResult));
            }
        }
    }

    private void IncrementTickCount()
    {
        lock (_gate)
        {
            TickCount++;
        }
    }

    private void IncrementCommitCount()
    {
        lock (_gate)
        {
            CommitCount++;
        }
    }

    private void AddDrainedEventCount(int drainedEvents)
    {
        lock (_gate)
        {
            DrainedEventCount += drainedEvents;
        }
    }

    private StyleTransitionCompletionPumpResult SetLastResult(StyleTransitionCompletionPumpResult result)
    {
        lock (_gate)
        {
            _lastResult = result;
            return _lastResult;
        }
    }

    private void SetLastError(Exception error)
    {
        lock (_gate)
        {
            LastError = error;
        }
    }
}
