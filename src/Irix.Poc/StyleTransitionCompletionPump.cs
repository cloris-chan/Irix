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
            _pumpTask = RunAsync(_disposeCancellationTokenSource.Token);
            return true;
        }
    }

    internal async ValueTask<StyleTransitionCompletionPumpResult> TickAndDrainAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default)
    {
        if (!_tracker.HasActiveTransition)
        {
            return StyleTransitionCompletionPumpResult.NoActiveTransition();
        }

        try
        {
            _ = await _compositor.RenderCompositionAnimationTickAtAsync(timestamp, cancellationToken);
            TickCount++;
        }
        catch (InvalidOperationException)
        {
            return StyleTransitionCompletionPumpResult.TickUnavailable();
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
            LastError = ex;
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
                DrainedEventCount += drainedEvents;
                return StyleTransitionCompletionPumpResult.TickRendered(drainedEvents);
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
                CommitCount++;
                DrainedEventCount += drainedEvents;
                return StyleTransitionCompletionPumpResult.CompletionCommitted(drainedEvents, commitResult);
            }
        }
    }
}
