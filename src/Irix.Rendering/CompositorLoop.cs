using System.Threading.Channels;

namespace Irix.Rendering;

public sealed class CompositorLoop : IVirtualNodePatchSink, IAsyncDisposable
{
    private readonly ICompositor _compositor;
    private readonly IPatchBatchTranslator _translator;
    private readonly Func<RetainedRenderFrameSegmentOwnership?>? _ownershipProvider;
    private readonly Channel<CompositorWorkItem> _channel;
    private readonly Lock _renderRequestGate = new();
    private readonly Task _processingTask;
    private bool _renderRequestQueued;
    private RenderCompletionWaitGroup? _queuedRenderRequestWaitGroup;

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
        return _channel.Writer.WriteAsync(new CompositorWorkItem(patchBatch, null), cancellationToken);
    }

    public ValueTask PublishAndWaitRenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        var waitGroup = new RenderCompletionWaitGroup();
        var waitTask = waitGroup.AddWaiter();
        if (!_channel.Writer.TryWrite(new CompositorWorkItem(patchBatch, waitGroup)))
        {
            patchBatch.Dispose();
            waitGroup.Complete(new InvalidOperationException("Unable to enqueue patch batch."));
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

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _processingTask;
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
            var patchBatch = workItem.PatchBatch;
            var isRenderRequest = patchBatch.Kind == PatchBatchKind.RenderRequest;
            if (isRenderRequest)
            {
                MarkRenderRequestStarted(workItem.RenderCompletionWaitGroup);
            }
            else if (patchBatch.Count == 0)
            {
                // Regular empty diff (no-op): dispose and skip.
                patchBatch.Dispose();
                workItem.RenderCompletionWaitGroup?.Complete(null);
                continue;
            }

            Exception? renderError = null;
            try
            {
                using (patchBatch)
                {
                    using var renderFrameBatch = _translator.Translate(patchBatch);
                    if (_ownershipProvider?.Invoke() is { } ownership
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
    }

    private void ScheduleRenderRequest(RenderCompletionWaitGroup waitGroup)
    {
        var patchBatch = PatchBatch.CreateRenderRequest();
        if (!_channel.Writer.TryWrite(new CompositorWorkItem(patchBatch, waitGroup)))
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

    private readonly record struct CompositorWorkItem(
        PatchBatch PatchBatch,
        RenderCompletionWaitGroup? RenderCompletionWaitGroup);

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
