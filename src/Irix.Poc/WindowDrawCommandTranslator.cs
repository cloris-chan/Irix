using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowDrawCommandTranslator : IPatchBatchTranslator
{
    private readonly INativeWindow _window;
    private readonly Action? _prepareFrame;
    private readonly Func<PixelRectangle>? _viewportProvider;
    private readonly Action<double>? _postFrameCallback;
    private readonly RenderPipeline _renderPipeline;
    private readonly SegmentedRetainedFrameProductionOwnerFeed? _ownerFeed;
    private DisplayScale _displayScale;

    public WindowDrawCommandTranslator(
        INativeWindow window,
        Action? prepareFrame,
        Func<PixelRectangle>? viewportProvider,
        Action<double>? postFrameCallback,
        TranslatorRenderPipelineFactory? renderPipelineFactory = null,
        RenderPipelineProductionOwnerOptions ownerOptions = default,
        DisplayScale displayScale = default)
    {
        _window = window;
        _prepareFrame = prepareFrame;
        _viewportProvider = viewportProvider;
        _postFrameCallback = postFrameCallback;
        _renderPipeline = (renderPipelineFactory ?? TranslatorRenderPipelineFactory.Default).Create();
        _ownerFeed = ownerOptions.EnableSegmentedRetainedFrameRuntimeOwner
            ? new SegmentedRetainedFrameProductionOwnerFeed(_renderPipeline, ownerOptions)
            : null;
        _displayScale = displayScale.Normalize();
    }

    private readonly RetainedTree _retainedTree = new(default);

    /// <summary>MaxScrollY from the last layout pass. 0 if no scroll needed.</summary>
    public double LastMaxScrollY => _renderPipeline.LastMaxScrollY;

    public ScrollFeedback LastScrollFeedback { get; private set; } = ScrollFeedback.Empty;

    public PixelRectangle LastViewport { get; private set; }

    public PixelRectangle LastLayoutViewport => _renderPipeline.LastViewport;

    public long LayoutRebuildCount => _renderPipeline.LayoutRebuildCount;

    public LayoutRebuildReason LastLayoutRebuildReason => _renderPipeline.LastLayoutRebuildReason;

    public IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications => _renderPipeline.LastDirtyClassifications;

    internal RetainedRenderFrameSegmentOwnership? SegmentOwnership => _ownerFeed?.SegmentOwnership;

    public WindowDrawCommandTranslator(INativeWindow window)
        : this(window, prepareFrame: null, viewportProvider: null, postFrameCallback: null, renderPipelineFactory: null)
    {
    }

    public void SetDisplayScale(DisplayScale scale)
    {
        _displayScale = scale.Normalize();
    }

    public RenderFrameBatch Translate(PatchBatch patchBatch)
    {
        return TranslateCore(patchBatch, measureAllocation: false, out _);
    }

    internal RenderFrameBatch Translate(PatchBatch patchBatch, out WindowTranslateAllocationAttribution attribution)
    {
        return TranslateCore(patchBatch, measureAllocation: true, out attribution);
    }

    private RenderFrameBatch TranslateCore(
        PatchBatch patchBatch,
        bool measureAllocation,
        out WindowTranslateAllocationAttribution attribution)
    {
        attribution = default;
        var beforeApply = GetAllocatedBytes(measureAllocation);
        IReadOnlyList<int>? dirty = null;
        VirtualNode previousRoot = default;
        TextBufferSnapshot? prevTextSnapshot = null;
        if (patchBatch.Kind == PatchBatchKind.RenderRequest)
        {
            // Render request: reuse retained tree, no dirty nodes
        }
        else if (patchBatch.Count > 0)
        {
            // Diff batch: apply patches to retained tree, get dirty set
            var result = _retainedTree.Apply(patchBatch);
            dirty = result.Dirty;
            if (dirty.Count > 0)
            {
                previousRoot = result.PreviousRoot;
                prevTextSnapshot = result.PreviousTextSnapshot;
            }
        }
        else
        {
            // Empty diff with new root (e.g. initial frame): apply to update retained tree
            _retainedTree.Apply(patchBatch);
        }

        attribution = attribution.WithRetainedApply(AllocatedDelta(measureAllocation, beforeApply));

        var beforeViewport = GetAllocatedBytes(measureAllocation);
        _prepareFrame?.Invoke();
        var physicalViewport = _viewportProvider?.Invoke() ?? _window.Region.PhysicalBounds;
        LastViewport = physicalViewport;
        var viewport = _displayScale.IsIdentity
            ? physicalViewport
            : new PixelRectangle(
                physicalViewport.X,
                physicalViewport.Y,
                (int)(physicalViewport.Width / _displayScale.ScaleX),
                (int)(physicalViewport.Height / _displayScale.ScaleY));
        var textSnapshot = _retainedTree.Tree.TextSnapshot;
        attribution = attribution.WithViewport(AllocatedDelta(measureAllocation, beforeViewport));

        var beforePipeline = GetAllocatedBytes(measureAllocation);
        RenderFrameBatch batch;
        var pipelineAttribution = default(RenderPipelineBuildAllocationAttribution);
        if (_ownerFeed is not null)
        {
            batch = _ownerFeed.Build(_retainedTree.Tree.Root, viewport, textSnapshot, dirty, prevTextSnapshot, previousRoot);
        }
        else if (measureAllocation)
        {
            batch = _renderPipeline.Build(_retainedTree.Tree.Root, viewport, textSnapshot, dirty, prevTextSnapshot, previousRoot, out pipelineAttribution);
        }
        else
        {
            batch = _renderPipeline.Build(_retainedTree.Tree.Root, viewport, textSnapshot, dirty, prevTextSnapshot, previousRoot);
        }

        attribution = attribution.WithPipelineBuild(AllocatedDelta(measureAllocation, beforePipeline));
        attribution = attribution.WithPipelineAttribution(pipelineAttribution);

        var beforeFeedback = GetAllocatedBytes(measureAllocation);
        LastScrollFeedback = BuildScrollFeedback(_renderPipeline.LastLayoutResult);
        _postFrameCallback?.Invoke(_renderPipeline.LastMaxScrollY);
        attribution = attribution.WithFeedback(AllocatedDelta(measureAllocation, beforeFeedback));

        return batch;
    }

    private static long GetAllocatedBytes(bool enabled) => enabled ? GC.GetTotalAllocatedBytes(false) : 0;

    private static long AllocatedDelta(bool enabled, long before) => enabled ? GC.GetTotalAllocatedBytes(false) - before : 0;

    private static ScrollFeedback BuildScrollFeedback(LayoutTreeResult? layoutResult)
    {
        if (layoutResult is null || layoutResult.ScrollDiagnostics.Count == 0)
        {
            return ScrollFeedback.Empty;
        }

        var containers = new ScrollContainerMetrics[layoutResult.ScrollDiagnostics.Count];
        for (var index = 0; index < containers.Length; index++)
        {
            var diagnostics = layoutResult.ScrollDiagnostics[index];
            containers[index] = new ScrollContainerMetrics(
                ContainerId: new ScrollContainerId(diagnostics.DfsIndex),
                ViewportExtent: diagnostics.VisibleHeight,
                ContentExtent: diagnostics.ContentHeight,
                MaxScrollY: diagnostics.MaxScrollY);
        }

        return new ScrollFeedback(containers);
    }
}

internal sealed class TranslatorRenderPipelineFactory(Func<RenderPipeline> create)
{
    public static TranslatorRenderPipelineFactory Default { get; } = FromStyle(CounterStylePreset.Default);

    public static TranslatorRenderPipelineFactory FromStyle(RenderStylePreset stylePreset) => new(() => new RenderPipeline(stylePreset));

    public RenderPipeline Create() => create();
}

internal readonly struct WindowTranslateAllocationAttribution(
    long RetainedApplyBytes,
    long ViewportBytes,
    long PipelineBuildBytes,
    long FeedbackBytes,
    RenderPipelineBuildAllocationAttribution PipelineAttribution = default) : IEquatable<WindowTranslateAllocationAttribution>
{
    public long RetainedApplyBytes { get; } = RetainedApplyBytes;
    public long ViewportBytes { get; } = ViewportBytes;
    public long PipelineBuildBytes { get; } = PipelineBuildBytes;
    public long FeedbackBytes { get; } = FeedbackBytes;
    public RenderPipelineBuildAllocationAttribution PipelineAttribution { get; } = PipelineAttribution;
    public long TotalBytes => RetainedApplyBytes + ViewportBytes + PipelineBuildBytes + FeedbackBytes;

    public WindowTranslateAllocationAttribution Add(WindowTranslateAllocationAttribution other) =>
        new(
            RetainedApplyBytes + other.RetainedApplyBytes,
            ViewportBytes + other.ViewportBytes,
            PipelineBuildBytes + other.PipelineBuildBytes,
            FeedbackBytes + other.FeedbackBytes,
            PipelineAttribution.Add(other.PipelineAttribution));

    public WindowTranslateAllocationAttribution WithRetainedApply(long bytes) => new(RetainedApplyBytes + bytes, ViewportBytes, PipelineBuildBytes, FeedbackBytes, PipelineAttribution);

    public WindowTranslateAllocationAttribution WithViewport(long bytes) => new(RetainedApplyBytes, ViewportBytes + bytes, PipelineBuildBytes, FeedbackBytes, PipelineAttribution);

    public WindowTranslateAllocationAttribution WithPipelineBuild(long bytes) => new(RetainedApplyBytes, ViewportBytes, PipelineBuildBytes + bytes, FeedbackBytes, PipelineAttribution);

    public WindowTranslateAllocationAttribution WithFeedback(long bytes) => new(RetainedApplyBytes, ViewportBytes, PipelineBuildBytes, FeedbackBytes + bytes, PipelineAttribution);

    public WindowTranslateAllocationAttribution WithPipelineAttribution(RenderPipelineBuildAllocationAttribution attribution) => new(RetainedApplyBytes, ViewportBytes, PipelineBuildBytes, FeedbackBytes, PipelineAttribution.Add(attribution));

    public bool Equals(WindowTranslateAllocationAttribution other)
    {
        return RetainedApplyBytes == other.RetainedApplyBytes
            && ViewportBytes == other.ViewportBytes
            && PipelineBuildBytes == other.PipelineBuildBytes
            && FeedbackBytes == other.FeedbackBytes
            && PipelineAttribution.Equals(other.PipelineAttribution);
    }

    public override bool Equals(object? obj) => obj is WindowTranslateAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RetainedApplyBytes, ViewportBytes, PipelineBuildBytes, FeedbackBytes, PipelineAttribution);
}
