using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly record struct RenderPipelineProductionOwnerOptions
{
    public bool EnableSegmentedRetainedFrameRuntimeOwner { get; init; }

    public static RenderPipelineProductionOwnerOptions Disabled => default;

    public static RenderPipelineProductionOwnerOptions SegmentedRetainedFrameRuntimeOwnerEnabled => new() { EnableSegmentedRetainedFrameRuntimeOwner = true };
}

internal readonly record struct SegmentedRetainedFrameProductionOwnerFeedResult(
    SegmentedRetainedFrameShadowResult ShadowResult,
    bool RuntimeOwnerEnabled,
    bool FallbackApplied,
    bool OwnerStatePreservedBeforeFallback,
    ulong BatchFrameId = 0,
    int BatchCommandCount = 0,
    IFrameResourceResolver? BatchResources = null,
    object? BatchCommandOwner = null)
{
    public static SegmentedRetainedFrameProductionOwnerFeedResult Disabled { get; } = new(
        SegmentedRetainedFrameShadowResult.Disabled,
        false,
        false,
        true);

    public SegmentedRetainedFrameShadowResultKind Kind => ShadowResult.Kind;
}

internal sealed class SegmentedRetainedFrameProductionOwnerFeed(RenderPipeline pipeline, RenderPipelineProductionOwnerOptions options = default) : IDisposable
{
    private readonly RetainedRenderFrameSegmentOwnership? _segmentOwnership = options.EnableSegmentedRetainedFrameRuntimeOwner
        ? new RetainedRenderFrameSegmentOwnership(pipeline.RetainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled)
        : null;

    public SegmentedRetainedFrameProductionOwnerFeedResult LastResult { get; private set; } = SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;

    public bool HasRuntimeOwner => _segmentOwnership?.HasSegmentedOwner == true;

    public SegmentedRetainedFrameRuntimeOwner? RuntimeOwner => _segmentOwnership?.RuntimeOwner;

    public RetainedRenderFrameSegmentOwnership? SegmentOwnership => _segmentOwnership;

    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null, TextBufferSnapshot? textSnapshot = null)
    {
        var batch = pipeline.Build(root, viewportBounds, dirtyNodes, textSnapshot);
        LastResult = UpdateRuntimeOwner(root, viewportBounds, batch);
        return batch;
    }

    private SegmentedRetainedFrameProductionOwnerFeedResult UpdateRuntimeOwner(VirtualNode root, PixelRectangle viewportBounds, RenderFrameBatch batch)
    {
        return _segmentOwnership?.Update(pipeline.LastRetainedInputSnapshot, root, viewportBounds, batch)
            ?? SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;
    }

    public void Dispose()
    {
        _segmentOwnership?.Dispose();
    }
}