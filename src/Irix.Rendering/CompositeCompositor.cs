using Irix.Drawing;

namespace Irix.Rendering;

public sealed class CompositeCompositor(params ICompositor[] compositors) : ICompositor
{
    public async ValueTask RenderAsync(DrawCommandBatch drawCommandBatch, CancellationToken cancellationToken = default)
    {
        foreach (var compositor in compositors)
        {
            await compositor.RenderAsync(drawCommandBatch, cancellationToken);
        }
    }
}
