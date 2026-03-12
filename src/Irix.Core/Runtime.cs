using System.Threading.Channels;

namespace Irix;

public sealed class Runtime<TModel, TMessage> : IMessageDispatcher<TMessage>, IAsyncDisposable
    where TModel : notnull
    where TMessage : notnull
{
    private readonly IApplication<TModel, TMessage> _application;
    private readonly IVirtualNodePatchSink _patchSink;
    private readonly Channel<TMessage> _messageChannel;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _processingTask;

    private bool _isStarted;
    private TModel _currentModel;
    private VirtualNodeTree _currentTree;

    public Runtime(IApplication<TModel, TMessage> application, IVirtualNodePatchSink patchSink)
    {
        _application = application;
        _patchSink = patchSink;
        _messageChannel = Channel.CreateUnbounded<TMessage>(new UnboundedChannelOptions
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

        if (!_messageChannel.Writer.TryWrite(message))
        {
            throw new InvalidOperationException("Unable to enqueue message.");
        }
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
        await foreach (var message in _messageChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
        {
            var updateResult = _application.Update(_currentModel, message);
            _currentModel = updateResult.NextModel;

            var nextTree = _application.BuildView(_currentModel);
            var patchBatch = VirtualNodeDiffer.CreatePatchBatch(_currentTree, nextTree);
            _currentTree = nextTree;

            await _patchSink.PublishAsync(patchBatch, _cancellationTokenSource.Token);
            await ExecuteCommandAsync(updateResult.Command, _cancellationTokenSource.Token);
        }
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
