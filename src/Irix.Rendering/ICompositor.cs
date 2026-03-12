namespace Irix.Rendering;

public interface ICompositor
{
    ValueTask RenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default);
}
