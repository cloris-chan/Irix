#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed partial class SegmentedRetainedFrameProductionOwnerFeed
{
    internal RenderFrameBatch BuildWithAllocationAttribution(
        VirtualNode root,
        PixelRectangle viewportBounds,
        TextBufferSnapshot textSnapshot,
        IReadOnlyList<int>? dirtyNodes,
        TextBufferSnapshot? prevTextSnapshot,
        VirtualNode previousRoot,
        out RenderPipelineBuildAllocationAttribution attribution)
    {
        var batch = _pipeline.BuildWithAllocationAttribution(root, viewportBounds, textSnapshot, dirtyNodes, prevTextSnapshot, previousRoot, out attribution);
        LastResult = UpdateRuntimeOwner(root, viewportBounds, batch);
        return batch;
    }
}
#endif
