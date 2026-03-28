using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowDrawCommandTranslator(INativeWindow window) : IPatchBatchTranslator
{
    private readonly WindowLayoutTreeBuilder _layoutTreeBuilder = new();
    private readonly WindowDrawCommandRecorder _drawCommandRecorder = new();

    public DrawCommandBatch Translate(PatchBatch patchBatch)
    {
        if (patchBatch.Count == 0)
        {
            return new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0);
        }

        var root = patchBatch.Memory.Span[patchBatch.Count - 1].Node;
        var layoutElements = _layoutTreeBuilder.Build(root, window.Region.PhysicalBounds);
        return _drawCommandRecorder.Record(layoutElements);
    }
}
