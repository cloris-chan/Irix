using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed partial class TranslatorCore : IRetainedInputSnapshotProvider
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

    public RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot =>
        _renderPipeline.HasLastRetainedInputSnapshot ? _renderPipeline.LastRetainedInputSnapshot : null;

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
        in TranslatorRetainedState retained)
    {
        var textSnapshot = _retainedTree.Tree.TextSnapshot;
        var batch = _ownerFeed is not null
            ? _ownerFeed.Build(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, retained.DirtyNodes, retained.PreviousTextSnapshot, retained.PreviousRoot)
            : _renderPipeline.Build(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, retained.DirtyNodes, retained.PreviousTextSnapshot, retained.PreviousRoot);

        return CreateOutput(in input, batch);
    }

    private TranslatorOutput CreateOutput(in TranslatorInput input, RenderFrameBatch batch)
    {
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
    LayoutDirtyClassificationList LastDirtyClassifications,
    LayoutTreeResult? LayoutResult,
    double MaxScrollY)
{
    public RenderFrameBatch Batch { get; } = Batch;
    public PixelRectangle PhysicalViewport { get; } = PhysicalViewport;
    public PixelRectangle LayoutViewport { get; } = LayoutViewport;
    public long LayoutRebuildCount { get; } = LayoutRebuildCount;
    public LayoutRebuildReason LastLayoutRebuildReason { get; } = LastLayoutRebuildReason;
    public LayoutDirtyClassificationList LastDirtyClassifications { get; } = LastDirtyClassifications;
    public LayoutTreeResult? LayoutResult { get; } = LayoutResult;
    public double MaxScrollY { get; } = MaxScrollY;
}
