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
        var tree = new VirtualNodeTree(root, textSnapshot);
        var previousTree = previousRoot.Kind == VirtualNodeKind.None ? default : new VirtualNodeTree(previousRoot, prevTextSnapshot ?? default);
        return BuildWithAllocationAttribution(tree, viewportBounds, dirtyNodes, previousTree, out attribution);
    }

    internal RenderFrameBatch BuildWithAllocationAttribution(
        VirtualNodeTree tree,
        PixelRectangle viewportBounds,
        IReadOnlyList<int>? dirtyNodes,
        VirtualNodeTree previousTree,
        out RenderPipelineBuildAllocationAttribution attribution)
    {
        var batch = _pipeline.BuildWithAllocationAttribution(tree, viewportBounds, dirtyNodes, previousTree, out attribution);
        LastResult = UpdateRuntimeOwner(tree.Root, viewportBounds, batch);
        return batch;
    }
}
#endif
