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

internal interface ICompositionScrollPresentationCompositor
{
    void SetCompositionScrollPresentationDeclaration(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot);

    ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default);

    bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY);
}
