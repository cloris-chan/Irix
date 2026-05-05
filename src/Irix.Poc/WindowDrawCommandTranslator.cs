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

    public RenderFrameBatch Translate(PatchBatch patchBatch)
    {
        if (patchBatch.Count == 0)
        {
            return new RenderFrameBatch(new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0), []);
        }

        return _renderPipeline.Build(patchBatch.Root, window.Region.PhysicalBounds);
    }
}
