namespace Irix;

public interface IVirtualNodePatchSink
{
    ValueTask PublishAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default);

    ValueTask PublishAndWaitRenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default);
}

internal interface IRetainedFramePatchSink : IVirtualNodePatchSink
{
    ValueTask PublishAndWaitRetainedFrameAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default);
}
