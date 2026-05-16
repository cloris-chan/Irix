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
        _displayScale = displayScale;
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
        _displayScale = scale;
    }

    public RenderFrameBatch Translate(PatchBatch patchBatch)
    {
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
        var batch = _ownerFeed is not null
            ? _ownerFeed.Build(_retainedTree.Tree.Root, viewport, textSnapshot, dirty, prevTextSnapshot, previousRoot)
            : _renderPipeline.Build(_retainedTree.Tree.Root, viewport, textSnapshot, dirty, prevTextSnapshot, previousRoot);
        LastScrollFeedback = BuildScrollFeedback(_renderPipeline.LastLayoutResult);
        _postFrameCallback?.Invoke(_renderPipeline.LastMaxScrollY);

        return batch;
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
                ContainerId: $"dfs:{diagnostics.DfsIndex}",
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
