using System.Threading.Channels;

namespace Irix.Rendering;

public sealed class CompositorLoop : IVirtualNodePatchSink, IAsyncDisposable
{
    private readonly ICompositor _compositor;
    private readonly Channel<PatchBatch> _channel;
    private readonly Task _processingTask;

    public CompositorLoop(ICompositor compositor)
    {
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

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _processingTask;
    }

    private async Task ProcessAsync()
    {
        await foreach (var patchBatch in _channel.Reader.ReadAllAsync())
        {
            using (patchBatch)
            {
                await _compositor.RenderAsync(patchBatch);
            }
        }
    }
}
