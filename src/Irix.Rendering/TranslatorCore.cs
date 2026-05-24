using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed class TranslatorCore
{
    private readonly RenderPipeline _renderPipeline;
    private readonly SegmentedRetainedFrameProductionOwnerFeed? _ownerFeed;
    private readonly RetainedTree _retainedTree = new(default);

    public TranslatorCore(RenderPipeline renderPipeline, RenderPipelineProductionOwnerOptions ownerOptions)
    {
        _renderPipeline = renderPipeline;
        _ownerFeed = ownerOptions.EnableSegmentedRetainedFrameRuntimeOwner
            ? new SegmentedRetainedFrameProductionOwnerFeed(_renderPipeline, ownerOptions)
            : null;
    }

    public RetainedRenderFrameSegmentOwnership? SegmentOwnership => _ownerFeed?.SegmentOwnership;

    public TranslatorRetainedState Apply(in PatchBatch patchBatch)
    {
        if (patchBatch.Kind == PatchBatchKind.RenderRequest)
        {
            return default;
        }

        if (patchBatch.Count > 0)
        {
            var result = _retainedTree.Apply(patchBatch);
            return result.Dirty.Count == 0
                ? new TranslatorRetainedState(result.Dirty)
                : new TranslatorRetainedState(result.Dirty, result.PreviousRoot, result.PreviousTextSnapshot);
        }

        _retainedTree.Apply(patchBatch);
        return default;
    }

    public TranslatorOutput BuildOutput(
        in TranslatorInput input,
        in TranslatorRetainedState retained,
        bool measureAllocation,
        out RenderPipelineBuildAllocationAttribution pipelineAttribution)
    {
        var textSnapshot = _retainedTree.Tree.TextSnapshot;
        RenderFrameBatch batch;
        pipelineAttribution = default;
        if (_ownerFeed is not null)
        {
            batch = _ownerFeed.Build(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, retained.DirtyNodes, retained.PreviousTextSnapshot, retained.PreviousRoot);
        }
        else if (measureAllocation)
        {
            batch = _renderPipeline.Build(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, retained.DirtyNodes, retained.PreviousTextSnapshot, retained.PreviousRoot, out pipelineAttribution);
        }
        else
        {
            batch = _renderPipeline.Build(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, retained.DirtyNodes, retained.PreviousTextSnapshot, retained.PreviousRoot);
        }

        return new TranslatorOutput(
            batch,
            input.PhysicalViewport,
            _renderPipeline.LastViewport,
            _renderPipeline.LayoutRebuildCount,
            _renderPipeline.LastLayoutRebuildReason,
            _renderPipeline.LastDirtyClassifications,
            _renderPipeline.LastLayoutResult,
            _renderPipeline.LastMaxScrollY);
    }
}

internal readonly struct TranslatorRetainedState(
    IReadOnlyList<int>? DirtyNodes,
    VirtualNode PreviousRoot = default,
    TextBufferSnapshot? PreviousTextSnapshot = null)
{
    public IReadOnlyList<int>? DirtyNodes { get; } = DirtyNodes;
    public VirtualNode PreviousRoot { get; } = PreviousRoot;
    public TextBufferSnapshot? PreviousTextSnapshot { get; } = PreviousTextSnapshot;
}

internal readonly struct TranslatorInput(
    PatchBatch PatchBatch,
    PixelRectangle PhysicalViewport,
    PixelRectangle LayoutViewport,
    DisplayScale DisplayScale)
{
    public PatchBatch PatchBatch { get; } = PatchBatch;
    public PixelRectangle PhysicalViewport { get; } = PhysicalViewport;
    public PixelRectangle LayoutViewport { get; } = LayoutViewport;
    public DisplayScale DisplayScale { get; } = DisplayScale;
}

internal readonly struct TranslatorOutput(
    RenderFrameBatch Batch,
    PixelRectangle PhysicalViewport,
    PixelRectangle LayoutViewport,
    long LayoutRebuildCount,
    LayoutRebuildReason LastLayoutRebuildReason,
    IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications,
    LayoutTreeResult? LayoutResult,
    double MaxScrollY)
{
    public RenderFrameBatch Batch { get; } = Batch;
    public PixelRectangle PhysicalViewport { get; } = PhysicalViewport;
    public PixelRectangle LayoutViewport { get; } = LayoutViewport;
    public long LayoutRebuildCount { get; } = LayoutRebuildCount;
    public LayoutRebuildReason LastLayoutRebuildReason { get; } = LastLayoutRebuildReason;
    public IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications { get; } = LastDirtyClassifications;
    public LayoutTreeResult? LayoutResult { get; } = LayoutResult;
    public double MaxScrollY { get; } = MaxScrollY;
}
