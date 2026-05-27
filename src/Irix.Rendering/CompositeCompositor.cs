namespace Irix.Rendering;

public sealed class CompositeCompositor(params ICompositor[] compositors) : ICompositor, IRetainedFrameStagingCompositor
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
}
