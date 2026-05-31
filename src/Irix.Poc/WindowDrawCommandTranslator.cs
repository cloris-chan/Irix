using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed partial class WindowDrawCommandTranslator : IPatchBatchTranslator, ICompositionInvalidationProvider
{
    private readonly TranslatorViewportProvider _translatorViewportProvider;
    private readonly TranslatorFeedbackSink _feedbackSink;
    private readonly TranslatorCore _translatorCore;
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
        _translatorCore = new TranslatorCore((renderPipelineFactory ?? TranslatorRenderPipelineFactory.CounterDefault).Create(), ownerOptions);
        _displayScale = displayScale.Normalize();
    }

    /// <summary>MaxScrollY from the last layout pass. 0 if no scroll needed.</summary>
    public double LastMaxScrollY => _feedbackSink.LastMaxScrollY;

    public ScrollFeedback LastScrollFeedback => _feedbackSink.LastScrollFeedback;

    public PixelRectangle LastViewport { get; private set; }

    public PixelRectangle LastLayoutViewport => _lastLayoutViewport;

    public long LayoutRebuildCount => _layoutRebuildCount;

    public LayoutRebuildReason LastLayoutRebuildReason => _lastLayoutRebuildReason;

    public IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications => _lastDirtyClassifications;

    public CompositionRenderInvalidation LastCompositionInvalidation { get; private set; }

    internal RetainedRenderFrameSegmentOwnership? SegmentOwnership => _translatorCore.SegmentOwnership;

    internal RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot => _translatorCore.LastRetainedInputSnapshot;

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
        return TranslateCore(patchBatch);
    }

    private RenderFrameBatch TranslateCore(PatchBatch patchBatch)
    {
        OnTranslateAllocationStarted();
        OnTranslateAllocationPhaseStarted();
        var retained = _translatorCore.Apply(in patchBatch);
        OnTranslateRetainedApplyAllocated();

        OnTranslateAllocationPhaseStarted();
        var input = CreateInput(in patchBatch);
        OnTranslateViewportAllocated();

        OnTranslateAllocationPhaseStarted();
        var output = _translatorCore.BuildOutput(in input, in retained);
        OnTranslatePipelineBuildAllocated(_translatorCore);
        ApplyOutput(in output);

        OnTranslateAllocationPhaseStarted();
        _feedbackSink.Deliver(output.LayoutResult, output.MaxScrollY);
        OnTranslateFeedbackAllocated();

        return output.Batch;
    }

    private TranslatorInput CreateInput(in PatchBatch patchBatch)
    {
        var viewport = _translatorViewportProvider.Resolve(_displayScale);
        return new TranslatorInput(patchBatch, viewport.PhysicalViewport, viewport.LayoutViewport, viewport.DisplayScale);
    }

    private void ApplyOutput(in TranslatorOutput output)
    {
        LastViewport = output.PhysicalViewport;
        _lastLayoutViewport = output.LayoutViewport;
        _layoutRebuildCount = output.LayoutRebuildCount;
        _lastLayoutRebuildReason = output.LastLayoutRebuildReason;
        _lastDirtyClassifications = output.LastDirtyClassifications;
        LastCompositionInvalidation = ResolveCompositionInvalidation(output.LastLayoutRebuildReason, output.MaxScrollY);
    }

    private CompositionRenderInvalidation ResolveCompositionInvalidation(LayoutRebuildReason reason, double maxScrollY)
    {
        var invalidation = CompositionRenderInvalidation.FromLayoutRebuildReason(reason);
        if (invalidation.CancelsScrollPresentation || Math.Abs(maxScrollY - _feedbackSink.LastMaxScrollY) <= 0.5)
        {
            return invalidation;
        }

        return CompositionRenderInvalidation.MaxScrollChanged;
    }

    partial void OnTranslateAllocationStarted();
    partial void OnTranslateAllocationPhaseStarted();
    partial void OnTranslateRetainedApplyAllocated();
    partial void OnTranslateViewportAllocated();
    partial void OnTranslatePipelineBuildAllocated(TranslatorCore translatorCore);
    partial void OnTranslateFeedbackAllocated();
}

internal sealed class TranslatorRenderPipelineFactory(Func<RenderPipeline> create)
{
    public static TranslatorRenderPipelineFactory CounterDefault { get; } = FromStyle(CounterStylePreset.Default);

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
