using Irix.Drawing;
using System.Threading.Channels;

namespace Irix.Rendering;

public sealed class CompositorLoop : IVirtualNodePatchSink, IAsyncDisposable
{
    private readonly ICompositor _compositor;
    private readonly IPatchBatchTranslator _translator;
    private readonly Channel<PatchBatch> _channel;
    private readonly Task _processingTask;
    private int _renderRequestQueued;
    private int _renderRequestDirty;

    public CompositorLoop(IPatchBatchTranslator translator, ICompositor compositor)
    {
        _translator = translator;
        _compositor = compositor;
        _channel = Channel.CreateUnbounded<PatchBatch>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _processingTask = Task.Run(ProcessAsync);
    }

    public ValueTask PublishAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(patchBatch, cancellationToken);
    }

    public ValueTask RequestRenderAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        Interlocked.Exchange(ref _renderRequestDirty, 1);
        ScheduleRenderRequest();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _processingTask;
    }

    private async Task ProcessAsync()
    {
        await foreach (var patchBatch in _channel.Reader.ReadAllAsync())
        {
            var isRenderRequest = patchBatch.Count == 0;
            if (isRenderRequest)
            {
                Interlocked.Exchange(ref _renderRequestQueued, 0);
                Interlocked.Exchange(ref _renderRequestDirty, 0);
            }

            using (patchBatch)
            {
                using var renderFrameBatch = _translator.Translate(patchBatch);
                await _compositor.RenderAsync(renderFrameBatch);
            }

            if (isRenderRequest && Interlocked.Exchange(ref _renderRequestDirty, 0) == 1)
            {
                ScheduleRenderRequest();
            }
        }
    }

    private void ScheduleRenderRequest()
    {
        if (Interlocked.CompareExchange(ref _renderRequestQueued, 1, 0) != 0)
        {
            return;
        }

        var patchBatch = new PatchBatch(new ArrayMemoryOwner<VirtualNodePatch>([]), 0);
        if (!_channel.Writer.TryWrite(patchBatch))
        {
            patchBatch.Dispose();
            Interlocked.Exchange(ref _renderRequestQueued, 0);
        }
    }
}
