using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowDrawCommandTranslator(
    INativeWindow window,
    Action? prepareFrame,
    Func<PixelRectangle>? viewportProvider,
    Action<double>? postFrameCallback) : IPatchBatchTranslator
{
    private readonly INativeWindow _window = window;
    private readonly Action? _prepareFrame = prepareFrame;
    private readonly Func<PixelRectangle>? _viewportProvider = viewportProvider;
    private readonly Action<double>? _postFrameCallback = postFrameCallback;
    private readonly Irix.Rendering.RenderPipeline _renderPipeline = new(CounterStylePreset.Default);

    private readonly RetainedTree _retainedTree = new(default);

    /// <summary>MaxScrollY from the last layout pass. 0 if no scroll needed.</summary>
    public double LastMaxScrollY => _renderPipeline.LastMaxScrollY;

    public ScrollFeedback LastScrollFeedback { get; private set; } = ScrollFeedback.Empty;

    public PixelRectangle LastViewport { get; private set; }

    public PixelRectangle LastLayoutViewport => _renderPipeline.LastViewport;

    public long LayoutRebuildCount => _renderPipeline.LayoutRebuildCount;

    public LayoutRebuildReason LastLayoutRebuildReason => _renderPipeline.LastLayoutRebuildReason;

    public IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications => _renderPipeline.LastDirtyClassifications;

    public WindowDrawCommandTranslator(INativeWindow window)
        : this(window, prepareFrame: null, viewportProvider: null, postFrameCallback: null)
    {
    }

    public RenderFrameBatch Translate(PatchBatch patchBatch)
    {
        IReadOnlyList<int>? dirty = null;
        if (patchBatch.Kind == PatchBatchKind.RenderRequest)
        {
            // Render request: reuse retained tree, no dirty nodes
        }
        else if (patchBatch.Count > 0)
        {
            // Diff batch: apply patches to retained tree, get dirty set
            dirty = _retainedTree.Apply(patchBatch);
        }
        else
        {
            // Empty diff with new root (e.g. initial frame): apply to update retained tree
            _retainedTree.Apply(patchBatch);
        }

        _prepareFrame?.Invoke();
        var viewport = _viewportProvider?.Invoke() ?? _window.Region.PhysicalBounds;
        LastViewport = viewport;
        var batch = _renderPipeline.Build(_retainedTree.Tree.Root, viewport, dirty);
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
