namespace Irix.Rendering;

public sealed class CompositeCompositor(params ICompositor[] compositors) : ICompositor, IRetainedFrameStagingCompositor, ICompositionScrollPresentationCompositor
{
    public async ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
    {
        foreach (var compositor in compositors)
        {
            await compositor.RenderAsync(renderFrameBatch, cancellationToken);
        }
    }

    async ValueTask IRetainedFrameStagingCompositor.StageRetainedFrameAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        CancellationToken cancellationToken)
    {
        var staged = false;
        foreach (var compositor in compositors)
        {
            if (compositor is IRetainedFrameStagingCompositor stagingCompositor)
            {
                await stagingCompositor.StageRetainedFrameAsync(renderFrameBatch, ownership, cancellationToken);
                staged = true;
            }
        }

        if (!staged)
        {
            throw new InvalidOperationException("Composite compositor does not contain a retained-frame staging compositor.");
        }
    }

    void ICompositionScrollPresentationCompositor.SetCompositionScrollPresentationDeclaration(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        var installed = false;
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionScrollPresentationCompositor scrollPresentationCompositor)
            {
                scrollPresentationCompositor.SetCompositionScrollPresentationDeclaration(declaration, snapshot);
                installed = true;
            }
        }

        if (!installed)
        {
            throw new InvalidOperationException("Composite compositor does not contain a composition scroll presentation compositor.");
        }
    }

    async ValueTask<CompositionBackendExecutionResult> ICompositionScrollPresentationCompositor.RenderCompositionScrollPresentationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken)
    {
        var rendered = false;
        var result = default(CompositionBackendExecutionResult);
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionScrollPresentationCompositor scrollPresentationCompositor)
            {
                result = await scrollPresentationCompositor.RenderCompositionScrollPresentationTickAtAsync(timestamp, cancellationToken);
                rendered = true;
            }
        }

        if (!rendered)
        {
            throw new InvalidOperationException("Composite compositor does not contain a composition scroll presentation compositor.");
        }

        return result;
    }

    bool ICompositionScrollPresentationCompositor.TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
    {
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionScrollPresentationCompositor scrollPresentationCompositor
                && scrollPresentationCompositor.TryGetPresentedScrollY(targetKey, out presentedScrollY))
            {
                return true;
            }
        }

        presentedScrollY = 0;
        return false;
    }
}
