namespace Irix;

public interface IVirtualNodePatchSink
{
    ValueTask PublishAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default);
}
