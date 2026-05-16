using System.Threading.Channels;

namespace Irix;

public sealed class Runtime<TModel, TMessage> : IMessageDispatcher<TMessage>, IAsyncDisposable
    where TModel : notnull
    where TMessage : notnull
{
    private readonly IApplication<TModel, TMessage> _application;
    private readonly IVirtualNodePatchSink _patchSink;
    private readonly Channel<QueuedMessage> _messageChannel;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _processingTask;

    private bool _isStarted;
    private TModel _currentModel;
    private VirtualNodeTree _currentTree;

    public Runtime(IApplication<TModel, TMessage> application, IVirtualNodePatchSink patchSink)
    {
        _application = application;
        _patchSink = patchSink;
        _messageChannel = Channel.CreateUnbounded<QueuedMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cancellationTokenSource = new CancellationTokenSource();
        _currentModel = _application.Initialize();
        _processingTask = Task.Run(ProcessMessagesAsync);
    }

    public TModel CurrentModel => _currentModel;

    public VirtualNodeTree CurrentTree => _currentTree;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isStarted)
        {
            return;
        }

        _currentTree = _application.BuildView(_currentModel);
        _isStarted = true;

        await _patchSink.PublishAsync(VirtualNodeDiffer.CreatePatchBatch(default, _currentTree), cancellationToken);
    }

    public void Dispatch(TMessage message)
    {
        ObjectDisposedException.ThrowIf(_cancellationTokenSource.IsCancellationRequested, this);

        if (!_messageChannel.Writer.TryWrite(new QueuedMessage(message, null)))
        {
            throw new InvalidOperationException("Unable to enqueue message.");
        }
    }

    internal Task DispatchAndWaitAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_cancellationTokenSource.IsCancellationRequested, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_messageChannel.Writer.TryWrite(new QueuedMessage(message, processed)))
        {
            throw new InvalidOperationException("Unable to enqueue message.");
        }

        return processed.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _messageChannel.Writer.TryComplete();
        _cancellationTokenSource.Cancel();

        try
        {
            await _processingTask;
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task ProcessMessagesAsync()
    {
        await foreach (var queuedMessage in _messageChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
        {
            try
            {
                var updateResult = _application.Update(_currentModel, queuedMessage.Message);
                _currentModel = updateResult.NextModel;

                var nextTree = _application.BuildView(_currentModel);
                var patchBatch = VirtualNodeDiffer.CreatePatchBatch(_currentTree, nextTree);
                _currentTree = nextTree;

                if (queuedMessage.Processed is null)
                {
                    await _patchSink.PublishAsync(patchBatch, _cancellationTokenSource.Token);
                }
                else
                {
                    await _patchSink.PublishAndWaitRenderAsync(patchBatch, _cancellationTokenSource.Token);
                }

                await ExecuteCommandAsync(updateResult.Command, _cancellationTokenSource.Token);
                queuedMessage.Processed?.TrySetResult();
            }
            catch (Exception ex)
            {
                queuedMessage.Processed?.TrySetException(ex);
                throw;
            }
        }
    }

    private readonly struct QueuedMessage(TMessage Message, TaskCompletionSource? Processed) : IEquatable<QueuedMessage>
    {
        public TMessage Message { get; } = Message;
        public TaskCompletionSource? Processed { get; } = Processed;

        public bool Equals(QueuedMessage other)
        {
            return EqualityComparer<TMessage>.Default.Equals(Message, other.Message)
                && EqualityComparer<TaskCompletionSource?>.Default.Equals(Processed, other.Processed);
        }

        public override bool Equals(object? obj) => obj is QueuedMessage other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Message, Processed);

        public static bool operator ==(QueuedMessage left, QueuedMessage right) => left.Equals(right);

        public static bool operator !=(QueuedMessage left, QueuedMessage right) => !left.Equals(right);
    }

    private async ValueTask ExecuteCommandAsync(Command<TMessage>? command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case null:
            case Command<TMessage>.None:
                return;
            case Command<TMessage>.Async asyncCommand:
                Dispatch(await asyncCommand.Callback(cancellationToken));
                return;
            case Command<TMessage>.Batch batchCommand:
                foreach (var childCommand in batchCommand.Commands)
                {
                    await ExecuteCommandAsync(childCommand, cancellationToken);
                }
                return;
            default:
                throw new NotSupportedException($"Unsupported command type: {command.GetType().Name}");
        }
    }
}
