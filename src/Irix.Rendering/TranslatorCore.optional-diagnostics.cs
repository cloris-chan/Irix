#if IRIX_DIAGNOSTICS
namespace Irix.Rendering;

internal sealed partial class TranslatorCore
{
    internal RenderPipelineBuildAllocationAttribution LastPipelineAllocationAttribution { get; private set; }

    internal TranslatorOutput BuildOutputWithAllocationAttribution(
        in TranslatorInput input,
        in TranslatorRetainedState retained,
        out RenderPipelineBuildAllocationAttribution attribution)
    {
        var textSnapshot = _retainedTree.Tree.TextSnapshot;
        RenderFrameBatch batch;
        if (_ownerFeed is not null)
        {
            batch = _ownerFeed.BuildWithAllocationAttribution(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, retained.DirtyNodes, retained.PreviousTextSnapshot, retained.PreviousRoot, out attribution);
        }
        else
        {
            batch = _renderPipeline.BuildWithAllocationAttribution(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, retained.DirtyNodes, retained.PreviousTextSnapshot, retained.PreviousRoot, out attribution);
        }

        LastPipelineAllocationAttribution = attribution;
        return CreateOutput(in input, batch);
    }
}
#endif
