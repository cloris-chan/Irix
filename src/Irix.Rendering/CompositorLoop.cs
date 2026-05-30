using System.Diagnostics;
using System.Threading.Channels;

namespace Irix.Rendering;

public sealed class CompositorLoop : IVirtualNodePatchSink, IRetainedFramePatchSink, IAsyncDisposable
{
    private const int CompositionTargetFrameRate = 240;
    private static readonly CompositionDuration CompositionTargetFrameInterval = CompositionDuration.FromStopwatchTicks(Math.Max(1, Stopwatch.Frequency / CompositionTargetFrameRate));
    private readonly ICompositor _compositor;
    private readonly IPatchBatchTranslator _translator;
    private readonly Func<RetainedRenderFrameSegmentOwnership?>? _ownershipProvider;
    private readonly Channel<CompositorWorkItem> _channel;
    private readonly Lock _renderRequestGate = new();
    private readonly Lock _compositionScheduleGate = new();
    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();
    private readonly Task _processingTask;
    private bool _renderRequestQueued;
    private RenderCompletionWaitGroup? _queuedRenderRequestWaitGroup;
    private CompositionScrollPresentationSchedule _scrollPresentationSchedule;
    private bool _scrollPresentationTickQueued;
    private int _scrollPresentationGeneration;
    private long _scrollPresentationTickCount;
    private RenderCompletionWaitGroup? _scrollPresentationIdleWaitGroup;

    public CompositorLoop(IPatchBatchTranslator translator, ICompositor compositor)
        : this(translator, compositor, ownershipProvider: null)
    {
    }

    internal CompositorLoop(IPatchBatchTranslator translator, ICompositor compositor, Func<RetainedRenderFrameSegmentOwnership?>? ownershipProvider)
    {
        _translator = translator;
        _compositor = compositor;
        _ownershipProvider = ownershipProvider;
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

    public ValueTask RequestRenderAsync(CancellationToken cancellationToken = default)
    {
        return RequestRenderCoreAsync(waitForCompletion: false, cancellationToken);
    }

    public ValueTask RequestRenderAndWaitAsync(CancellationToken cancellationToken = default)
    {
        return RequestRenderCoreAsync(waitForCompletion: true, cancellationToken);
    }

    internal long ScrollPresentationTickCount => Volatile.Read(ref _scrollPresentationTickCount);

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

        Task? waitTask = null;
        lock (_compositionScheduleGate)
        {
            if (_scrollPresentationSchedule.IsActive || _scrollPresentationTickQueued)
            {
                _scrollPresentationIdleWaitGroup ??= new RenderCompletionWaitGroup();
                waitTask = _scrollPresentationIdleWaitGroup.AddWaiter();
            }
        }

        return waitTask is null
            ? ValueTask.CompletedTask
            : new ValueTask(waitTask.WaitAsync(cancellationToken));
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
                await InstallScrollPresentationAsync(workItem);
                continue;
            }

            if (workItem.Kind == CompositorWorkKind.TickScrollPresentation)
            {
                await RenderScrollPresentationTickAsync(workItem);
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

    private async Task InstallScrollPresentationAsync(CompositorWorkItem workItem)
    {
        Exception? error = null;
        try
        {
            if (_compositor is not ICompositionScrollPresentationCompositor scrollPresentationCompositor)
            {
                throw new InvalidOperationException("The current compositor does not support composition scroll presentation scheduling.");
            }

            var declaration = workItem.ScrollPresentationDeclaration;
            scrollPresentationCompositor.SetCompositionScrollPresentationDeclaration(declaration, workItem.RetainedInputSnapshot!);
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

        var timestamp = CompositionTimestamp.Now();
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

    private int ActivateScrollPresentationSchedule(
        in CompositionScrollPresentationDeclaration declaration)
    {
        RenderCompletionWaitGroup? supersededWaitGroup;
        int generation;
        lock (_compositionScheduleGate)
        {
            supersededWaitGroup = _scrollPresentationIdleWaitGroup;
            _scrollPresentationIdleWaitGroup = null;
            generation = unchecked(++_scrollPresentationGeneration);
            if (generation == 0)
            {
                generation = ++_scrollPresentationGeneration;
            }

            _scrollPresentationSchedule = new CompositionScrollPresentationSchedule(
                generation,
                declaration.TargetKey,
                declaration.Timeline.StartTimestamp,
                declaration.Timeline.StartTimestamp + declaration.Timeline.Duration);
            _scrollPresentationTickQueued = false;
        }

        supersededWaitGroup?.Complete(null);
        return generation;
    }

    private bool TryGetActiveScrollPresentationSchedule(int generation, out CompositionScrollPresentationSchedule schedule)
    {
        lock (_compositionScheduleGate)
        {
            if (_scrollPresentationSchedule.IsActive && _scrollPresentationSchedule.Generation == generation)
            {
                _scrollPresentationTickQueued = false;
                schedule = _scrollPresentationSchedule;
                return true;
            }

            schedule = default;
            return false;
        }
    }

    private void CompleteScrollPresentationSchedule(int generation)
    {
        RenderCompletionWaitGroup? idleWaitGroup = null;
        lock (_compositionScheduleGate)
        {
            if (_scrollPresentationSchedule.IsActive && _scrollPresentationSchedule.Generation == generation)
            {
                _scrollPresentationSchedule = default;
                _scrollPresentationTickQueued = false;
                idleWaitGroup = _scrollPresentationIdleWaitGroup;
                _scrollPresentationIdleWaitGroup = null;
            }
        }

        idleWaitGroup?.Complete(null);
    }

    private void CompleteScrollPresentationSchedulerOnDispose()
    {
        RenderCompletionWaitGroup? idleWaitGroup;
        lock (_compositionScheduleGate)
        {
            _scrollPresentationSchedule = default;
            _scrollPresentationTickQueued = false;
            idleWaitGroup = _scrollPresentationIdleWaitGroup;
            _scrollPresentationIdleWaitGroup = null;
        }

        idleWaitGroup?.Complete(null);
    }

    private void ScheduleNextScrollPresentationTick(CompositionTimestamp lastTickTimestamp, int generation)
    {
        CompositionDuration delay;
        lock (_compositionScheduleGate)
        {
            if (!_scrollPresentationSchedule.IsActive
                || _scrollPresentationSchedule.Generation != generation
                || _scrollPresentationTickQueued)
            {
                return;
            }

            delay = ComputeNextTickDelay(lastTickTimestamp, CompositionTimestamp.Now(), CompositionTargetFrameInterval);
            _scrollPresentationTickQueued = true;
        }

        _ = ScheduleDelayedScrollPresentationTickAsync(delay, generation);
    }

    private async Task ScheduleDelayedScrollPresentationTickAsync(CompositionDuration delay, int generation)
    {
        try
        {
            if (delay.StopwatchTicks > 0)
            {
                var delayMilliseconds = ToDelayMilliseconds(delay);
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, _disposeCancellationTokenSource.Token);
                }
                else
                {
                    await Task.Yield();
                }
            }
            else
            {
                await Task.Yield();
            }

            if (!_disposeCancellationTokenSource.IsCancellationRequested)
            {
                _channel.Writer.TryWrite(CompositorWorkItem.TickScrollPresentation(generation));
            }
        }
        catch (OperationCanceledException) when (_disposeCancellationTokenSource.IsCancellationRequested)
        {
        }
    }

    internal static int ComputeNextTickDelayMilliseconds(
        CompositionTimestamp tickTimestamp,
        CompositionTimestamp afterRenderTimestamp,
        CompositionDuration targetFrameInterval)
    {
        return ToDelayMilliseconds(ComputeNextTickDelay(tickTimestamp, afterRenderTimestamp, targetFrameInterval));
    }

    private static CompositionDuration ComputeNextTickDelay(
        CompositionTimestamp tickTimestamp,
        CompositionTimestamp afterRenderTimestamp,
        CompositionDuration targetFrameInterval)
    {
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

        var milliseconds = delay.StopwatchTicks * 1000 / Stopwatch.Frequency;
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

    private enum CompositorWorkMode : byte
    {
        Render,
        RetainedFrameStage
    }

    private enum CompositorWorkKind : byte
    {
        Render,
        InstallScrollPresentation,
        TickScrollPresentation
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
            int compositionGeneration)
        {
            Kind = kind;
            PatchBatch = patchBatch;
            RenderCompletionWaitGroup = renderCompletionWaitGroup;
            Mode = mode;
            ScrollPresentationDeclaration = scrollPresentationDeclaration;
            RetainedInputSnapshot = retainedInputSnapshot;
            CompositionGeneration = compositionGeneration;
        }

        public CompositorWorkKind Kind { get; }
        public PatchBatch? PatchBatch { get; }
        public RenderCompletionWaitGroup? RenderCompletionWaitGroup { get; }
        public CompositorWorkMode Mode { get; }
        public CompositionScrollPresentationDeclaration ScrollPresentationDeclaration { get; }
        public RenderPipelineRetainedInputSnapshot? RetainedInputSnapshot { get; }
        public int CompositionGeneration { get; }

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
