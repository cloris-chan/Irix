namespace Irix.Rendering;

public interface ICompositor
{
    ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default);
}

internal interface IRetainedFrameStagingCompositor
{
    ValueTask StageRetainedFrameAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        CancellationToken cancellationToken = default);
}
