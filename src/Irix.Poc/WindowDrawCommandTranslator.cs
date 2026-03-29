using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowDrawCommandTranslator(INativeWindow window) : IPatchBatchTranslator
{
    private readonly Irix.Rendering.RenderPipeline _renderPipeline = new(
        LayoutStyle.Default,
        new DrawingStyle(
        TextColor: DrawColor.Opaque(32, 32, 32),
        RectangleFillColor: DrawColor.Opaque(72, 72, 72),
        ButtonFillColor: DrawColor.Opaque(52, 120, 246),
        ButtonTextColor: DrawColor.Opaque(255, 255, 255)));

    public DrawCommandBatch Translate(PatchBatch patchBatch)
    {
        if (patchBatch.Count == 0)
        {
            return new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0);
        }

        var root = patchBatch.Memory.Span[patchBatch.Count - 1].Node;
        return _renderPipeline.Build(root, window.Region.PhysicalBounds);
    }
}
