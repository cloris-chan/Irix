namespace Irix;

public interface IVirtualNodePatchSink
{
    ValueTask PublishAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default);

    ValueTask PublishAndWaitRenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default);
}
