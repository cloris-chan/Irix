namespace Irix.Rendering;

public interface ICompositor
{
    ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default);
}
