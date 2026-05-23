using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowDrawCommandTranslator : IPatchBatchTranslator
{
    private readonly TranslatorViewportProvider _translatorViewportProvider;
    private readonly TranslatorFeedbackSink _feedbackSink;
    private readonly RenderPipeline _renderPipeline;
    private readonly SegmentedRetainedFrameProductionOwnerFeed? _ownerFeed;
    private DisplayScale _displayScale;
    private PixelRectangle _lastLayoutViewport;
    private long _layoutRebuildCount;
    private LayoutRebuildReason _lastLayoutRebuildReason;
    private IReadOnlyList<LayoutDirtyClassification> _lastDirtyClassifications = [];

    public WindowDrawCommandTranslator(
        INativeWindow window,
        Action? prepareFrame,
        Func<PixelRectangle>? viewportProvider,
        Action<double>? postFrameCallback,
        TranslatorRenderPipelineFactory? renderPipelineFactory = null,
        RenderPipelineProductionOwnerOptions ownerOptions = default,
        DisplayScale displayScale = default)
    {
        _translatorViewportProvider = new TranslatorViewportProvider(window, prepareFrame, viewportProvider);
        _feedbackSink = new TranslatorFeedbackSink(postFrameCallback);
        _renderPipeline = (renderPipelineFactory ?? TranslatorRenderPipelineFactory.Default).Create();
        _ownerFeed = ownerOptions.EnableSegmentedRetainedFrameRuntimeOwner
            ? new SegmentedRetainedFrameProductionOwnerFeed(_renderPipeline, ownerOptions)
            : null;
        _displayScale = displayScale.Normalize();
    }

    private readonly RetainedTree _retainedTree = new(default);

    /// <summary>MaxScrollY from the last layout pass. 0 if no scroll needed.</summary>
    public double LastMaxScrollY => _feedbackSink.LastMaxScrollY;

    public ScrollFeedback LastScrollFeedback => _feedbackSink.LastScrollFeedback;

    public PixelRectangle LastViewport { get; private set; }

    public PixelRectangle LastLayoutViewport => _lastLayoutViewport;

    public long LayoutRebuildCount => _layoutRebuildCount;

    public LayoutRebuildReason LastLayoutRebuildReason => _lastLayoutRebuildReason;

    public IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications => _lastDirtyClassifications;

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
        var input = CreateInput(in patchBatch);
        attribution = attribution.WithViewport(AllocatedDelta(measureAllocation, beforeViewport));

        var beforePipeline = GetAllocatedBytes(measureAllocation);
        var pipelineAttribution = default(RenderPipelineBuildAllocationAttribution);
        var output = BuildOutput(in input, dirty, prevTextSnapshot, previousRoot, measureAllocation, out pipelineAttribution);
        attribution = attribution.WithPipelineBuild(AllocatedDelta(measureAllocation, beforePipeline));
        attribution = attribution.WithPipelineAttribution(pipelineAttribution);
        ApplyOutput(in output);

        var beforeFeedback = GetAllocatedBytes(measureAllocation);
        _feedbackSink.Deliver(_renderPipeline.LastLayoutResult, _renderPipeline.LastMaxScrollY);
        attribution = attribution.WithFeedback(AllocatedDelta(measureAllocation, beforeFeedback));

        return output.Batch;
    }

    private TranslatorInput CreateInput(in PatchBatch patchBatch)
    {
        var viewport = _translatorViewportProvider.Resolve(_displayScale);
        return new TranslatorInput(patchBatch, viewport.PhysicalViewport, viewport.LayoutViewport, viewport.DisplayScale);
    }

    private TranslatorOutput BuildOutput(
        in TranslatorInput input,
        IReadOnlyList<int>? dirtyNodes,
        TextBufferSnapshot? previousTextSnapshot,
        VirtualNode previousRoot,
        bool measureAllocation,
        out RenderPipelineBuildAllocationAttribution pipelineAttribution)
    {
        var textSnapshot = _retainedTree.Tree.TextSnapshot;
        RenderFrameBatch batch;
        pipelineAttribution = default;
        if (_ownerFeed is not null)
        {
            batch = _ownerFeed.Build(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, dirtyNodes, previousTextSnapshot, previousRoot);
        }
        else if (measureAllocation)
        {
            batch = _renderPipeline.Build(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, dirtyNodes, previousTextSnapshot, previousRoot, out pipelineAttribution);
        }
        else
        {
            batch = _renderPipeline.Build(_retainedTree.Tree.Root, input.LayoutViewport, textSnapshot, dirtyNodes, previousTextSnapshot, previousRoot);
        }

        return new TranslatorOutput(
            batch,
            input.PhysicalViewport,
            _renderPipeline.LastViewport,
            _renderPipeline.LayoutRebuildCount,
            _renderPipeline.LastLayoutRebuildReason,
            _renderPipeline.LastDirtyClassifications);
    }

    private void ApplyOutput(in TranslatorOutput output)
    {
        LastViewport = output.PhysicalViewport;
        _lastLayoutViewport = output.LayoutViewport;
        _layoutRebuildCount = output.LayoutRebuildCount;
        _lastLayoutRebuildReason = output.LastLayoutRebuildReason;
        _lastDirtyClassifications = output.LastDirtyClassifications;
    }

    private static long GetAllocatedBytes(bool enabled) => enabled ? GC.GetTotalAllocatedBytes(false) : 0;

    private static long AllocatedDelta(bool enabled, long before) => enabled ? GC.GetTotalAllocatedBytes(false) - before : 0;
}

internal sealed class TranslatorRenderPipelineFactory(Func<RenderPipeline> create)
{
    public static TranslatorRenderPipelineFactory Default { get; } = FromStyle(CounterStylePreset.Default);

    public static TranslatorRenderPipelineFactory FromStyle(RenderStylePreset stylePreset) => new(() => new RenderPipeline(stylePreset));

    public RenderPipeline Create() => create();
}

internal sealed class TranslatorViewportProvider(INativeWindow window, Action? prepareFrame, Func<PixelRectangle>? viewportProvider)
{
    public TranslatorViewport Resolve(DisplayScale displayScale)
    {
        prepareFrame?.Invoke();
        var physicalViewport = viewportProvider?.Invoke() ?? window.Region.PhysicalBounds;
        return new TranslatorViewport(physicalViewport, ResolveLogicalViewport(physicalViewport, displayScale), displayScale);
    }

    private static PixelRectangle ResolveLogicalViewport(PixelRectangle physicalViewport, DisplayScale displayScale)
    {
        return displayScale.IsIdentity
            ? physicalViewport
            : new PixelRectangle(
                physicalViewport.X,
                physicalViewport.Y,
                (int)(physicalViewport.Width / displayScale.ScaleX),
                (int)(physicalViewport.Height / displayScale.ScaleY));
    }
}

internal readonly struct TranslatorViewport(
    PixelRectangle PhysicalViewport,
    PixelRectangle LayoutViewport,
    DisplayScale DisplayScale)
{
    public PixelRectangle PhysicalViewport { get; } = PhysicalViewport;
    public PixelRectangle LayoutViewport { get; } = LayoutViewport;
    public DisplayScale DisplayScale { get; } = DisplayScale;
}

internal sealed class TranslatorFeedbackSink(Action<double>? postFrameCallback)
{
    public double LastMaxScrollY { get; private set; }

    public ScrollFeedback LastScrollFeedback { get; private set; } = ScrollFeedback.Empty;

    public void Deliver(LayoutTreeResult? layoutResult, double maxScrollY)
    {
        LastMaxScrollY = maxScrollY;
        LastScrollFeedback = BuildScrollFeedback(layoutResult);
        postFrameCallback?.Invoke(maxScrollY);
    }

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
    IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications)
{
    public RenderFrameBatch Batch { get; } = Batch;
    public PixelRectangle PhysicalViewport { get; } = PhysicalViewport;
    public PixelRectangle LayoutViewport { get; } = LayoutViewport;
    public long LayoutRebuildCount { get; } = LayoutRebuildCount;
    public LayoutRebuildReason LastLayoutRebuildReason { get; } = LastLayoutRebuildReason;
    public IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications { get; } = LastDirtyClassifications;
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
