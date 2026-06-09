using System.Threading.Channels;
using Irix.Platform;

namespace Irix.Poc;

internal sealed class PocInputEventPump : IAsyncDisposable
{
    private readonly Func<RawInputEvent, ValueTask> _dispatch;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Channel<RawInputEvent> _channel = Channel.CreateUnbounded<RawInputEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Task _pumpTask;

    public PocInputEventPump(Func<RawInputEvent, ValueTask> dispatch)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _pumpTask = Task.Run(PumpAsync);
    }

    public PocInputEventPump(Action<RawInputEvent> dispatch)
        : this(CreateDispatch(dispatch))
    {
    }

    internal PocInputEventPump()
        : this(_ => { })
    {
    }

    internal long EnqueuedCount => Volatile.Read(ref _enqueuedCount);

    internal long DispatchedCount => Volatile.Read(ref _dispatchedCount);

    internal Exception? LastError => Volatile.Read(ref _lastError);

    private long _enqueuedCount;
    private long _dispatchedCount;
    private Exception? _lastError;
    private bool _isDisposed;

    public bool TryEnqueue(in RawInputEvent inputEvent)
    {
        if (Volatile.Read(ref _isDisposed))
        {
            return false;
        }

        if (!_channel.Writer.TryWrite(inputEvent))
        {
            return false;
        }

        Interlocked.Increment(ref _enqueuedCount);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _isDisposed))
        {
            return;
        }

        Volatile.Write(ref _isDisposed, true);
        _channel.Writer.TryComplete();
        _cancellationTokenSource.Cancel();
        try
        {
            await _pumpTask;
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task PumpAsync()
    {
        await foreach (var inputEvent in _channel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
        {
            try
            {
                await _dispatch(inputEvent);
                Interlocked.Increment(ref _dispatchedCount);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _lastError, ex);
            }
        }
    }

    private static Func<RawInputEvent, ValueTask> CreateDispatch(Action<RawInputEvent> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return inputEvent =>
        {
            dispatch(inputEvent);
            return ValueTask.CompletedTask;
        };
    }
}
