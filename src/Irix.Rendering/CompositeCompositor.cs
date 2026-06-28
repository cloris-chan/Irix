namespace Irix.Rendering;

public sealed class CompositeCompositor(params ICompositor[] compositors) : ICompositor, IRetainedFrameStagingCompositor, ICompositionScrollPresentationCompositor, ICompositionAnimationCompositor, ICompositionFramePacingProvider
{
    CompositionFramePacing ICompositionFramePacingProvider.FramePacing
    {
        get
        {
            foreach (var compositor in compositors)
            {
                if (compositor is ICompositionFramePacingProvider provider
                    && provider.FramePacing == CompositionFramePacing.BackendPresentation)
                {
                    return CompositionFramePacing.BackendPresentation;
                }
            }

            return CompositionFramePacing.SoftwareTimer;
        }
    }

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
        CancellationToken cancellationToken,
        RetainedFrameStageCompositionMode compositionMode)
    {
        var staged = false;
        foreach (var compositor in compositors)
        {
            if (compositor is IRetainedFrameStagingCompositor stagingCompositor)
            {
                await stagingCompositor.StageRetainedFrameAsync(renderFrameBatch, ownership, cancellationToken, compositionMode);
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

    bool ICompositionScrollPresentationCompositor.TryPrepareCompositionScrollPresentationRetainedFrameUpdate(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        var prepared = false;
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionScrollPresentationCompositor scrollPresentationCompositor
                && !scrollPresentationCompositor.TryPrepareCompositionScrollPresentationRetainedFrameUpdate(declaration, snapshot))
            {
                return false;
            }

            prepared |= compositor is ICompositionScrollPresentationCompositor;
        }

        return prepared;
    }

    void ICompositionScrollPresentationCompositor.ClearCompositionScrollPresentation()
    {
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionScrollPresentationCompositor scrollPresentationCompositor)
            {
                scrollPresentationCompositor.ClearCompositionScrollPresentation();
            }
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

    void ICompositionAnimationCompositor.SetCompositionAnimationDeclaration(
        in CompositionAnimationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        var installed = false;
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionAnimationCompositor animationCompositor)
            {
                animationCompositor.SetCompositionAnimationDeclaration(declaration, snapshot);
                installed = true;
            }
        }

        if (!installed)
        {
            throw new InvalidOperationException("Composite compositor does not contain a composition animation compositor.");
        }
    }

    void ICompositionAnimationCompositor.ClearCompositionAnimation()
    {
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionAnimationCompositor animationCompositor)
            {
                animationCompositor.ClearCompositionAnimation();
            }
        }
    }

    void ICompositionAnimationCompositor.CommitCompositionAnimation()
    {
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionAnimationCompositor animationCompositor)
            {
                animationCompositor.CommitCompositionAnimation();
            }
        }
    }

    async ValueTask<CompositionBackendExecutionResult> ICompositionAnimationCompositor.RenderCompositionAnimationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken)
    {
        var rendered = false;
        var result = default(CompositionBackendExecutionResult);
        foreach (var compositor in compositors)
        {
            if (compositor is ICompositionAnimationCompositor animationCompositor)
            {
                result = await animationCompositor.RenderCompositionAnimationTickAtAsync(timestamp, cancellationToken);
                rendered = true;
            }
        }

        if (!rendered)
        {
            throw new InvalidOperationException("Composite compositor does not contain a composition animation compositor.");
        }

        return result;
    }
}
