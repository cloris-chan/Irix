namespace Irix.Rendering;

public sealed class CompositeCompositor(params ICompositor[] compositors) : ICompositor
{
    public async ValueTask RenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        foreach (var compositor in compositors)
        {
            await compositor.RenderAsync(patchBatch, cancellationToken);
        }
    }
}
