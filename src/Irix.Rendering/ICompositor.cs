using Irix.Drawing;

namespace Irix.Rendering;

public interface ICompositor
{
    ValueTask RenderAsync(DrawCommandBatch drawCommandBatch, CancellationToken cancellationToken = default);
}
