using System.Threading.Channels;

namespace Irix.Rendering;

public sealed partial class CompositorLoop : IVirtualNodePatchSink, IRetainedFramePatchSink, IAsyncDisposable
{
    private const int CompositionTargetFrameRate = 240;
    private static readonly CompositionDuration CompositionTargetFrameInterval = CompositionDuration.FromStopwatchTicks(Math.Max(1, CompositionClock.Frequency / CompositionTargetFrameRate));
    private readonly ICompositor _compositor;
    private readonly IPatchBatchTranslator _translator;
    private readonly Func<RetainedRenderFrameSegmentOwnership?>? _ownershipProvider;
    private readonly ICompositionClockSource _clockSource;
    private readonly Channel<CompositorWorkItem> _channel;
    private readonly Lock _renderRequestGate = new();
    private readonly ScrollPresentationLifecycle _scrollPresentationLifecycle = new();
    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();
    private readonly Task _processingTask;
    private bool _renderRequestQueued;
    private RenderCompletionWaitGroup? _queuedRenderRequestWaitGroup;
    private long _scrollPresentationTickCount;
    private long _scrollPresentationCancelCount;
    private long _scrollPresentationStaleDelayedTickSkipCount;

    public CompositorLoop(IPatchBatchTranslator translator, ICompositor compositor)
        : this(translator, compositor, ownershipProvider: null)
    {
    }

    internal CompositorLoop(IPatchBatchTranslator translator, ICompositor compositor, Func<RetainedRenderFrameSegmentOwnership?>? ownershipProvider)
        : this(translator, compositor, ownershipProvider, new SystemCompositionClockSource())
    {
    }

    internal CompositorLoop(
        IPatchBatchTranslator translator,
        ICompositor compositor,
        Func<RetainedRenderFrameSegmentOwnership?>? ownershipProvider,
        ICompositionClockSource clockSource)
    {
        _translator = translator;
        _compositor = compositor;
        _ownershipProvider = ownershipProvider;
        _clockSource = clockSource;
        _channel = Channel.CreateUnbounded<CompositorWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _processingTask = Task.Run(ProcessAsync);
    }

    public ValueTask PublishAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(CompositorWorkItem.Render(patchBatch, null, CompositorWorkMode.Render), cancellationToken);
    }

    public ValueTask PublishAndWaitRenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        var waitGroup = new RenderCompletionWaitGroup();
        var waitTask = waitGroup.AddWaiter();
        if (!_channel.Writer.TryWrite(CompositorWorkItem.Render(patchBatch, waitGroup, CompositorWorkMode.Render)))
        {
            patchBatch.Dispose();
            waitGroup.Complete(new InvalidOperationException("Unable to enqueue patch batch."));
        }

        return new ValueTask(waitTask.WaitAsync(cancellationToken));
    }

    public ValueTask PublishAndWaitRetainedFrameAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        var waitGroup = new RenderCompletionWaitGroup();
        var waitTask = waitGroup.AddWaiter();
        if (!_channel.Writer.TryWrite(CompositorWorkItem.Render(patchBatch, waitGroup, CompositorWorkMode.RetainedFrameStage)))
        {
            patchBatch.Dispose();
            waitGroup.Complete(new InvalidOperationException("Unable to enqueue retained-frame patch batch."));
        }

        return new ValueTask(waitTask.WaitAsync(cancellationToken));
    }

    internal ValueTask PublishRetainedFrameAndStartCompositionScrollPresentationAsync(
        PatchBatch patchBatch,
        in CompositionScrollPresentationDeclaration declaration,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            patchBatch.Dispose();
            return ValueTask.FromCanceled(cancellationToken);
        }

        var waitGroup = new RenderCompletionWaitGroup();
        var waitTask = waitGroup.AddWaiter();
        if (!_channel.Writer.TryWrite(CompositorWorkItem.StageAndInstallScrollPresentation(patchBatch, declaration, waitGroup)))
        {
            patchBatch.Dispose();
            waitGroup.Complete(new InvalidOperationException("Unable to enqueue retained-frame scroll presentation."));
        }

        return new ValueTask(waitTask.WaitAsync(cancellationToken));
    }

    public ValueTask RequestRenderAsync(CancellationToken cancellationToken = default)
    {
        return RequestRenderCoreAsync(waitForCompletion: false, cancellationToken);
    }

    public ValueTask RequestRenderAndWaitAsync(CancellationToken cancellationToken = default)
    {
        return RequestRenderCoreAsync(waitForCompletion: true, cancellationToken);
    }

    internal long ScrollPresentationTickCount => Volatile.Read(ref _scrollPresentationTickCount);

    internal long ScrollPresentationCancelCount => Volatile.Read(ref _scrollPresentationCancelCount);

    internal long ScrollPresentationStaleDelayedTickSkipCount => Volatile.Read(ref _scrollPresentationStaleDelayedTickSkipCount);

    partial void RecordScrollPresentationCancellation(
        byte reason,
        CompositionRenderInvalidationKind invalidationKind,
        bool canceled);

    internal bool HasActiveScrollPresentation(NodeKey targetKey)
    {
        if (targetKey == NodeKey.None)
        {
            return false;
        }

        return _scrollPresentationLifecycle.HasActive(targetKey);
    }

    internal bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
    {
        if (_compositor is ICompositionScrollPresentationCompositor scrollPresentationCompositor)
        {
            return scrollPresentationCompositor.TryGetPresentedScrollY(targetKey, out presentedScrollY);
        }

        presentedScrollY = 0;
        return false;
    }

    internal ValueTask StartCompositionScrollPresentationAsync(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        var waitGroup = new RenderCompletionWaitGroup();
        var waitTask = waitGroup.AddWaiter();
        if (!_channel.Writer.TryWrite(CompositorWorkItem.InstallScrollPresentation(declaration, snapshot, waitGroup)))
        {
            waitGroup.Complete(new InvalidOperationException("Unable to enqueue composition scroll presentation."));
        }

        return new ValueTask(waitTask.WaitAsync(cancellationToken));
    }

    internal ValueTask WaitForScrollPresentationIdleAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        var waitTask = _scrollPresentationLifecycle.AddIdleWaiterIfBusy();

        return waitTask is null
            ? ValueTask.CompletedTask
            : new ValueTask(waitTask.WaitAsync(cancellationToken));
    }

    internal ValueTask CancelCompositionScrollPresentationAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        var waitGroup = new RenderCompletionWaitGroup();
        var waitTask = waitGroup.AddWaiter();
        if (!_channel.Writer.TryWrite(CompositorWorkItem.CancelScrollPresentation(waitGroup)))
        {
            waitGroup.Complete(new InvalidOperationException("Unable to enqueue composition scroll presentation cancellation."));
        }

        return new ValueTask(waitTask.WaitAsync(cancellationToken));
    }

    internal ValueTask<CompositionScrollPresentationSample> SampleAndHoldCompositionScrollPresentationAsync(
        NodeKey targetKey,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<CompositionScrollPresentationSample>(cancellationToken);
        }

        var completion = new TaskCompletionSource<CompositionScrollPresentationSample>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_channel.Writer.TryWrite(CompositorWorkItem.SampleAndHoldScrollPresentation(targetKey, completion)))
        {
            completion.TrySetException(new InvalidOperationException("Unable to enqueue composition scroll presentation sample-and-hold."));
        }

        return new ValueTask<CompositionScrollPresentationSample>(completion.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCancellationTokenSource.Cancel();
        CompleteScrollPresentationSchedulerOnDispose();
        _channel.Writer.TryComplete();
        await _processingTask;
        _disposeCancellationTokenSource.Dispose();
    }

    private ValueTask RequestRenderCoreAsync(bool waitForCompletion, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        RenderCompletionWaitGroup waitGroup;
        RenderCompletionWaitGroup? waitGroupToSchedule = null;
        Task? waitTask = null;

        lock (_renderRequestGate)
        {
            if (!_renderRequestQueued)
            {
                _renderRequestQueued = true;
                _queuedRenderRequestWaitGroup = new RenderCompletionWaitGroup();
                waitGroupToSchedule = _queuedRenderRequestWaitGroup;
            }

            waitGroup = _queuedRenderRequestWaitGroup
                ?? throw new InvalidOperationException("No render request wait group is queued.");

            if (waitForCompletion)
            {
                waitTask = waitGroup.AddWaiter();
            }
        }

        if (waitGroupToSchedule is not null)
        {
            ScheduleRenderRequest(waitGroupToSchedule);
        }

        return waitTask is null
            ? ValueTask.CompletedTask
            : new ValueTask(waitTask.WaitAsync(cancellationToken));
    }

    private async Task ProcessAsync()
    {
        await foreach (var workItem in _channel.Reader.ReadAllAsync())
        {
            if (workItem.Kind == CompositorWorkKind.InstallScrollPresentation)
            {
                await InstallScrollPresentationWorkItemAsync(workItem);
                continue;
            }

            if (workItem.Kind == CompositorWorkKind.StageAndInstallScrollPresentation)
            {
                await StageAndInstallScrollPresentationWorkItemAsync(workItem);
                continue;
            }

            if (workItem.Kind == CompositorWorkKind.TickScrollPresentation)
            {
                await RenderScrollPresentationTickAsync(workItem);
                continue;
            }

            if (workItem.Kind == CompositorWorkKind.CancelScrollPresentation)
            {
                CancelScrollPresentationWorkItem(workItem);
                continue;
            }

            if (workItem.Kind == CompositorWorkKind.SampleAndHoldScrollPresentation)
            {
                SampleAndHoldScrollPresentationWorkItem(workItem);
                continue;
            }

            await ProcessRenderWorkItemAsync(workItem);
        }
    }

    private async Task ProcessRenderWorkItemAsync(CompositorWorkItem workItem)
    {
        var patchBatch = workItem.PatchBatch ?? throw new InvalidOperationException("Render work item must carry a patch batch.");
        var isRenderRequest = patchBatch.Kind == PatchBatchKind.RenderRequest;
        if (isRenderRequest)
        {
            MarkRenderRequestStarted(workItem.RenderCompletionWaitGroup);
        }
        else if (patchBatch.Count == 0)
        {
            patchBatch.Dispose();
            workItem.RenderCompletionWaitGroup?.Complete(null);
            return;
        }

        Exception? renderError = null;
        try
        {
            using (patchBatch)
            {
                using var renderFrameBatch = _translator.Translate(patchBatch);
                var invalidation = ResolveCompositionInvalidation();
                var retainedSnapshot = ResolveRetainedInputSnapshot();
                CancelScrollPresentationForInvalidation(invalidation, retainedSnapshot);
                var ownership = _ownershipProvider?.Invoke();
                if (workItem.Mode == CompositorWorkMode.RetainedFrameStage)
                {
                    if (_compositor is not IRetainedFrameStagingCompositor stagingCompositor)
                    {
                        throw new InvalidOperationException("The current compositor does not support retained-frame staging.");
                    }

                    await stagingCompositor.StageRetainedFrameAsync(renderFrameBatch, ownership);
                }
                else if (ownership is not null
                    && _compositor is DrawingBackendCompositor drawingBackendCompositor)
                {
                    await drawingBackendCompositor.RenderAsync(renderFrameBatch, ownership);
                }
                else
                {
                    await _compositor.RenderAsync(renderFrameBatch);
                }
            }
        }
        catch (Exception ex)
        {
            renderError = ex;
            throw;
        }
        finally
        {
            workItem.RenderCompletionWaitGroup?.Complete(renderError);
        }
    }

    private async Task StageAndInstallScrollPresentationWorkItemAsync(CompositorWorkItem workItem)
    {
        var patchBatch = workItem.PatchBatch ?? throw new InvalidOperationException("Retained-frame scroll presentation work item must carry a patch batch.");
        Exception? error = null;
        try
        {
            using (patchBatch)
            {
                using var renderFrameBatch = _translator.Translate(patchBatch);
                if (_compositor is not IRetainedFrameStagingCompositor stagingCompositor)
                {
                    throw new InvalidOperationException("The current compositor does not support retained-frame staging.");
                }

                await stagingCompositor.StageRetainedFrameAsync(
                    renderFrameBatch,
                    _ownershipProvider?.Invoke(),
                    compositionMode: RetainedFrameStageCompositionMode.DeferActiveCompositionAfterStage);
                if (_translator is not IRetainedInputSnapshotProvider snapshotProvider
                    || snapshotProvider.LastRetainedInputSnapshot is not { } snapshot)
                {
                    throw new InvalidOperationException("The current translator did not provide a retained input snapshot for scroll presentation.");
                }

                await InstallScrollPresentationCoreAsync(workItem.ScrollPresentationDeclaration, snapshot);
            }
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            workItem.RenderCompletionWaitGroup?.Complete(error);
        }
    }

    private async Task InstallScrollPresentationWorkItemAsync(CompositorWorkItem workItem)
    {
        Exception? error = null;
        try
        {
            await InstallScrollPresentationCoreAsync(
                workItem.ScrollPresentationDeclaration,
                workItem.RetainedInputSnapshot!);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            workItem.RenderCompletionWaitGroup?.Complete(error);
        }
    }

    private async Task InstallScrollPresentationCoreAsync(
        CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        if (_compositor is not ICompositionScrollPresentationCompositor scrollPresentationCompositor)
        {
            throw new InvalidOperationException("The current compositor does not support composition scroll presentation scheduling.");
        }

        scrollPresentationCompositor.SetCompositionScrollPresentationDeclaration(declaration, snapshot);
        var timestamp = declaration.Timeline.StartTimestamp;
        _ = await scrollPresentationCompositor.RenderCompositionScrollPresentationTickAtAsync(timestamp);
        Interlocked.Increment(ref _scrollPresentationTickCount);
        var generation = ActivateScrollPresentationSchedule(declaration);
        if (timestamp >= declaration.Timeline.StartTimestamp + declaration.Timeline.Duration)
        {
            CompleteScrollPresentationSchedule(generation);
        }
        else
        {
            ScheduleNextScrollPresentationTick(timestamp, generation);
        }
    }

    private async Task RenderScrollPresentationTickAsync(CompositorWorkItem workItem)
    {
        if (!TryGetActiveScrollPresentationSchedule(workItem.CompositionGeneration, out var schedule))
        {
            return;
        }

        if (_compositor is not ICompositionScrollPresentationCompositor scrollPresentationCompositor)
        {
            throw new InvalidOperationException("The current compositor does not support composition scroll presentation scheduling.");
        }

        if (!scrollPresentationCompositor.TryGetPresentedScrollY(schedule.TargetKey, out _))
        {
            Interlocked.Increment(ref _scrollPresentationStaleDelayedTickSkipCount);
            CompleteScrollPresentationSchedule(workItem.CompositionGeneration);
            return;
        }

        var timestamp = _clockSource.TimestampNow();
        if (timestamp > schedule.EndTimestamp)
        {
            timestamp = schedule.EndTimestamp;
        }

        _ = await scrollPresentationCompositor.RenderCompositionScrollPresentationTickAtAsync(timestamp);
        Interlocked.Increment(ref _scrollPresentationTickCount);

        if (timestamp >= schedule.EndTimestamp)
        {
            CompleteScrollPresentationSchedule(workItem.CompositionGeneration);
            return;
        }

        ScheduleNextScrollPresentationTick(timestamp, workItem.CompositionGeneration);
    }

    private void CancelScrollPresentationWorkItem(CompositorWorkItem workItem)
    {
        Exception? error = null;
        try
        {
            CancelScrollPresentationCore(
                countCancellation: true,
                1,
                CompositionRenderInvalidationKind.None);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            workItem.RenderCompletionWaitGroup?.Complete(error);
        }
    }

    private void SampleAndHoldScrollPresentationWorkItem(CompositorWorkItem workItem)
    {
        var completion = workItem.ScrollPresentationSampleCompletion
            ?? throw new InvalidOperationException("Scroll presentation sample work item must carry a completion source.");
        try
        {
            var sample = default(CompositionScrollPresentationSample);
            if (_compositor is ICompositionScrollPresentationCompositor scrollPresentationCompositor
                && scrollPresentationCompositor.TryGetPresentedScrollY(workItem.ScrollPresentationTargetKey, out var presentedScrollY))
            {
                sample = new CompositionScrollPresentationSample(true, presentedScrollY);
            }

            HoldScrollPresentationScheduleForRetarget(workItem.ScrollPresentationTargetKey, sample);
            completion.TrySetResult(sample);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            throw;
        }
    }

    private CompositionRenderInvalidation ResolveCompositionInvalidation()
    {
        return _translator is ICompositionInvalidationProvider invalidationProvider
            ? invalidationProvider.LastCompositionInvalidation
            : CompositionRenderInvalidation.None;
    }

    private RenderPipelineRetainedInputSnapshot? ResolveRetainedInputSnapshot()
    {
        return _translator is IRetainedInputSnapshotProvider snapshotProvider
            ? snapshotProvider.LastRetainedInputSnapshot
            : null;
    }

    private void CancelScrollPresentationForInvalidation(
        in CompositionRenderInvalidation invalidation,
        RenderPipelineRetainedInputSnapshot? retainedSnapshot)
    {
        if (ShouldCancelScrollPresentationForInvalidation(invalidation, retainedSnapshot))
        {
            CancelScrollPresentationCore(
                countCancellation: true,
                2,
                invalidation.Kind);
        }
    }

    private bool ShouldCancelScrollPresentationForInvalidation(
        in CompositionRenderInvalidation invalidation,
        RenderPipelineRetainedInputSnapshot? retainedSnapshot)
    {
        if (!invalidation.CancelsScrollPresentation)
        {
            return false;
        }

        return !TryPrepareActiveScrollPresentationRetainedFrameUpdate(invalidation, retainedSnapshot);
    }

    private bool TryPrepareActiveScrollPresentationRetainedFrameUpdate(
        in CompositionRenderInvalidation invalidation,
        RenderPipelineRetainedInputSnapshot? retainedSnapshot)
    {
        if (!CanPreserveScrollPresentationAcrossInvalidation(invalidation.Kind)
            || retainedSnapshot is null
            || _compositor is not ICompositionScrollPresentationCompositor scrollPresentationCompositor
            || !TryGetPreservableScrollPresentationDeclaration(out var declaration))
        {
            return false;
        }

        return scrollPresentationCompositor.TryPrepareCompositionScrollPresentationRetainedFrameUpdate(
            declaration,
            retainedSnapshot);
    }

    private static bool CanPreserveScrollPresentationAcrossInvalidation(CompositionRenderInvalidationKind kind)
    {
        return kind == CompositionRenderInvalidationKind.TextSizeAffecting;
    }

    private void CancelScrollPresentationCore(
        bool countCancellation,
        byte reason,
        CompositionRenderInvalidationKind invalidationKind)
    {
        if (_compositor is ICompositionScrollPresentationCompositor scrollPresentationCompositor)
        {
            scrollPresentationCompositor.ClearCompositionScrollPresentation();
        }

        var canceled = CancelScrollPresentationSchedule();
        RecordScrollPresentationCancellation(reason, invalidationKind, canceled);
        if (countCancellation && canceled)
        {
            Interlocked.Increment(ref _scrollPresentationCancelCount);
        }
    }

    private void HoldScrollPresentationScheduleForRetarget(
        NodeKey targetKey,
        in CompositionScrollPresentationSample sample)
    {
        _scrollPresentationLifecycle.HoldForRetarget(targetKey, sample);
    }

    private int ActivateScrollPresentationSchedule(
        in CompositionScrollPresentationDeclaration declaration)
    {
        return _scrollPresentationLifecycle.Activate(declaration);
    }

    private bool TryGetActiveScrollPresentationSchedule(int generation, out CompositionScrollPresentationSchedule schedule)
    {
        return _scrollPresentationLifecycle.TryBeginTick(generation, out schedule);
    }

    private void CompleteScrollPresentationSchedule(int generation)
    {
        _scrollPresentationLifecycle.Complete(generation);
    }

    private bool CancelScrollPresentationSchedule()
    {
        return _scrollPresentationLifecycle.Cancel();
    }

    private void CompleteScrollPresentationSchedulerOnDispose()
    {
        var canceled = _scrollPresentationLifecycle.CompleteForDispose();
        RecordScrollPresentationCancellation(3, CompositionRenderInvalidationKind.None, canceled);
    }

    private void ScheduleNextScrollPresentationTick(CompositionTimestamp lastTickTimestamp, int generation)
    {
        if (!_scrollPresentationLifecycle.TryQueueNextTick(
            generation,
            lastTickTimestamp,
            _clockSource.TimestampNow(),
            CompositionTargetFrameInterval,
            ResolveScrollPresentationFramePacing(),
            out var delay))
        {
            return;
        }

        if (delay.StopwatchTicks <= 0)
        {
            _ = TryQueueScrollPresentationTick(generation);
            return;
        }

        _ = ScheduleDelayedScrollPresentationTickAsync(delay, generation);
    }

    private bool TryGetPreservableScrollPresentationDeclaration(out CompositionScrollPresentationDeclaration declaration)
    {
        return _scrollPresentationLifecycle.TryGetPreservableDeclaration(out declaration);
    }

    private async Task ScheduleDelayedScrollPresentationTickAsync(CompositionDuration delay, int generation)
    {
        try
        {
            var delayMilliseconds = ToDelayMilliseconds(delay);
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, _disposeCancellationTokenSource.Token);
            }

            if (!_disposeCancellationTokenSource.IsCancellationRequested
                && !TryQueueScrollPresentationTick(generation))
            {
                Interlocked.Increment(ref _scrollPresentationStaleDelayedTickSkipCount);
            }
        }
        catch (OperationCanceledException) when (_disposeCancellationTokenSource.IsCancellationRequested)
        {
        }
    }

    private bool TryQueueScrollPresentationTick(int generation)
    {
        if (!_scrollPresentationLifecycle.TryTakeQueuedTickForDispatch(generation))
        {
            return false;
        }

        if (_channel.Writer.TryWrite(CompositorWorkItem.TickScrollPresentation(generation)))
        {
            return true;
        }

        _scrollPresentationLifecycle.CompleteAfterDispatchFailure(generation);
        return false;
    }

    internal static int ComputeNextTickDelayMilliseconds(
        CompositionTimestamp tickTimestamp,
        CompositionTimestamp afterRenderTimestamp,
        CompositionDuration targetFrameInterval,
        CompositionFramePacing framePacing = CompositionFramePacing.SoftwareTimer)
    {
        return ToDelayMilliseconds(ComputeNextTickDelay(tickTimestamp, afterRenderTimestamp, targetFrameInterval, framePacing));
    }

    private static CompositionDuration ComputeNextTickDelay(
        CompositionTimestamp tickTimestamp,
        CompositionTimestamp afterRenderTimestamp,
        CompositionDuration targetFrameInterval,
        CompositionFramePacing framePacing)
    {
        if (framePacing == CompositionFramePacing.BackendPresentation)
        {
            return CompositionDuration.Zero;
        }

        var nextTick = tickTimestamp + targetFrameInterval;
        var remainingTicks = (nextTick - afterRenderTimestamp).StopwatchTicks;
        return remainingTicks <= 0
            ? CompositionDuration.Zero
            : CompositionDuration.FromStopwatchTicks(remainingTicks);
    }

    private static int ToDelayMilliseconds(CompositionDuration delay)
    {
        if (delay.StopwatchTicks <= 0)
        {
            return 0;
        }

        var milliseconds = delay.StopwatchTicks * 1000 / CompositionClock.Frequency;
        return milliseconds > int.MaxValue ? int.MaxValue : Math.Max(1, (int)milliseconds);
    }

    private void ScheduleRenderRequest(RenderCompletionWaitGroup waitGroup)
    {
        var patchBatch = PatchBatch.CreateRenderRequest();
        if (!_channel.Writer.TryWrite(CompositorWorkItem.Render(patchBatch, waitGroup, CompositorWorkMode.Render)))
        {
            patchBatch.Dispose();
            MarkRenderRequestScheduleFailed(waitGroup);
            waitGroup.Complete(new InvalidOperationException("Unable to enqueue render request."));
        }
    }

    private void MarkRenderRequestStarted(RenderCompletionWaitGroup? waitGroup)
    {
        lock (_renderRequestGate)
        {
            if (ReferenceEquals(_queuedRenderRequestWaitGroup, waitGroup))
            {
                _queuedRenderRequestWaitGroup = null;
            }

            _renderRequestQueued = false;
        }
    }

    private void MarkRenderRequestScheduleFailed(RenderCompletionWaitGroup waitGroup)
    {
        lock (_renderRequestGate)
        {
            if (ReferenceEquals(_queuedRenderRequestWaitGroup, waitGroup))
            {
                _queuedRenderRequestWaitGroup = null;
                _renderRequestQueued = false;
            }
        }
    }

    private CompositionFramePacing ResolveScrollPresentationFramePacing()
    {
        return _compositor is ICompositionFramePacingProvider framePacingProvider
            ? framePacingProvider.FramePacing
            : CompositionFramePacing.SoftwareTimer;
    }

    private enum CompositorWorkMode : byte
    {
        Render,
        RetainedFrameStage
    }

    private enum CompositorWorkKind : byte
    {
        Render,
        InstallScrollPresentation,
        StageAndInstallScrollPresentation,
        TickScrollPresentation,
        CancelScrollPresentation,
        SampleAndHoldScrollPresentation
    }

    internal readonly struct CompositionScrollPresentationSample(
        bool HasValue,
        double PresentedScrollY) : IEquatable<CompositionScrollPresentationSample>
    {
        public bool HasValue { get; } = HasValue;
        public double PresentedScrollY { get; } = PresentedScrollY;

        public bool Equals(CompositionScrollPresentationSample other)
        {
            return HasValue == other.HasValue
                && PresentedScrollY.Equals(other.PresentedScrollY);
        }

        public override bool Equals(object? obj) => obj is CompositionScrollPresentationSample other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(HasValue, PresentedScrollY);

        public static bool operator ==(CompositionScrollPresentationSample left, CompositionScrollPresentationSample right) => left.Equals(right);

        public static bool operator !=(CompositionScrollPresentationSample left, CompositionScrollPresentationSample right) => !left.Equals(right);
    }

    private readonly struct CompositionScrollPresentationSchedule(
        int generation,
        NodeKey targetKey,
        CompositionTimestamp startTimestamp,
        CompositionTimestamp endTimestamp)
    {
        public int Generation { get; } = generation;
        public NodeKey TargetKey { get; } = targetKey;
        public CompositionTimestamp StartTimestamp { get; } = startTimestamp;
        public CompositionTimestamp EndTimestamp { get; } = endTimestamp;
        public bool IsActive => Generation != 0 && TargetKey != NodeKey.None && EndTimestamp >= StartTimestamp;
    }

    private readonly struct CompositorWorkItem
    {
        private CompositorWorkItem(
            CompositorWorkKind kind,
            PatchBatch? patchBatch,
            RenderCompletionWaitGroup? renderCompletionWaitGroup,
            CompositorWorkMode mode,
            CompositionScrollPresentationDeclaration scrollPresentationDeclaration,
            RenderPipelineRetainedInputSnapshot? retainedInputSnapshot,
            int compositionGeneration,
            NodeKey scrollPresentationTargetKey = default,
            TaskCompletionSource<CompositionScrollPresentationSample>? scrollPresentationSampleCompletion = null)
        {
            Kind = kind;
            PatchBatch = patchBatch;
            RenderCompletionWaitGroup = renderCompletionWaitGroup;
            Mode = mode;
            ScrollPresentationDeclaration = scrollPresentationDeclaration;
            RetainedInputSnapshot = retainedInputSnapshot;
            CompositionGeneration = compositionGeneration;
            ScrollPresentationTargetKey = scrollPresentationTargetKey;
            ScrollPresentationSampleCompletion = scrollPresentationSampleCompletion;
        }

        public CompositorWorkKind Kind { get; }
        public PatchBatch? PatchBatch { get; }
        public RenderCompletionWaitGroup? RenderCompletionWaitGroup { get; }
        public CompositorWorkMode Mode { get; }
        public CompositionScrollPresentationDeclaration ScrollPresentationDeclaration { get; }
        public RenderPipelineRetainedInputSnapshot? RetainedInputSnapshot { get; }
        public int CompositionGeneration { get; }
        public NodeKey ScrollPresentationTargetKey { get; }
        public TaskCompletionSource<CompositionScrollPresentationSample>? ScrollPresentationSampleCompletion { get; }

        public static CompositorWorkItem Render(
            PatchBatch patchBatch,
            RenderCompletionWaitGroup? renderCompletionWaitGroup,
            CompositorWorkMode mode)
        {
            return new CompositorWorkItem(
                CompositorWorkKind.Render,
                patchBatch,
                renderCompletionWaitGroup,
                mode,
                default,
                null,
                0);
        }

        public static CompositorWorkItem InstallScrollPresentation(
            in CompositionScrollPresentationDeclaration declaration,
            RenderPipelineRetainedInputSnapshot snapshot,
            RenderCompletionWaitGroup renderCompletionWaitGroup)
        {
            return new CompositorWorkItem(
                CompositorWorkKind.InstallScrollPresentation,
                null,
                renderCompletionWaitGroup,
                CompositorWorkMode.Render,
                declaration,
                snapshot,
                0);
        }

        public static CompositorWorkItem StageAndInstallScrollPresentation(
            PatchBatch patchBatch,
            in CompositionScrollPresentationDeclaration declaration,
            RenderCompletionWaitGroup renderCompletionWaitGroup)
        {
            return new CompositorWorkItem(
                CompositorWorkKind.StageAndInstallScrollPresentation,
                patchBatch,
                renderCompletionWaitGroup,
                CompositorWorkMode.RetainedFrameStage,
                declaration,
                null,
                0);
        }

        public static CompositorWorkItem TickScrollPresentation(int generation)
        {
            return new CompositorWorkItem(
                CompositorWorkKind.TickScrollPresentation,
                null,
                null,
                CompositorWorkMode.Render,
                default,
                null,
                generation);
        }

        public static CompositorWorkItem CancelScrollPresentation(RenderCompletionWaitGroup renderCompletionWaitGroup)
        {
            return new CompositorWorkItem(
                CompositorWorkKind.CancelScrollPresentation,
                null,
                renderCompletionWaitGroup,
                CompositorWorkMode.Render,
                default,
                null,
                0);
        }

        public static CompositorWorkItem SampleAndHoldScrollPresentation(
            NodeKey targetKey,
            TaskCompletionSource<CompositionScrollPresentationSample> completion)
        {
            return new CompositorWorkItem(
                CompositorWorkKind.SampleAndHoldScrollPresentation,
                null,
                null,
                CompositorWorkMode.Render,
                default,
                null,
                0,
                targetKey,
                completion);
        }
    }

    private sealed class RenderCompletionWaitGroup
    {
        private readonly Queue<TaskCompletionSource> _waiters = new();
        private bool _isCompleted;
        private Exception? _error;

        public Task AddWaiter()
        {
            lock (_waiters)
            {
                if (_isCompleted)
                {
                    return _error is null
                        ? Task.CompletedTask
                        : Task.FromException(_error);
                }

                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Enqueue(waiter);
                return waiter.Task;
            }
        }

        public void Complete(Exception? error)
        {
            TaskCompletionSource[] waiters;
            lock (_waiters)
            {
                if (_isCompleted)
                {
                    return;
                }

                _isCompleted = true;
                _error = error;
                waiters = _waiters.ToArray();
                _waiters.Clear();
            }

            foreach (var waiter in waiters)
            {
                if (error is null)
                {
                    waiter.TrySetResult();
                }
                else
                {
                    waiter.TrySetException(error);
                }
            }
        }
    }
}
