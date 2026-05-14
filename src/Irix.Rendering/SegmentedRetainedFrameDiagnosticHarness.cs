using Irix.Platform;

namespace Irix.Rendering;

internal readonly record struct RenderPipelineShadowOptions
{
    public bool EnableSegmentedRetainedFrame { get; init; }

    public static RenderPipelineShadowOptions Disabled => default;

    public static RenderPipelineShadowOptions SegmentedRetainedFrameEnabled => new() { EnableSegmentedRetainedFrame = true };
}

internal sealed class SegmentedRetainedFrameDiagnosticHarness(RenderPipeline pipeline, RenderPipelineShadowOptions options = default) : IDisposable
{
    private SegmentedRetainedFrameShadowHarness? _segmentedRetainedFrame;

    public SegmentedRetainedFrameShadowResult LastShadowResult { get; private set; } = SegmentedRetainedFrameShadowResult.Disabled;

    public bool HasSegmentedRetainedFrameOwner => _segmentedRetainedFrame is not null;

    public SegmentedRetainedFrameOwner? SegmentedRetainedFrameOwner => _segmentedRetainedFrame?.Owner;

    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null, TextBufferSnapshot? textSnapshot = null, TextBufferSnapshot? prevTextSnapshot = null, VirtualNode previousRoot = default)
    {
        var batch = pipeline.Build(root, viewportBounds, dirtyNodes, textSnapshot, prevTextSnapshot, previousRoot);
        LastShadowResult = UpdateSegmentedRetainedFrame(root, viewportBounds, batch);
        return batch;
    }

    private SegmentedRetainedFrameShadowResult UpdateSegmentedRetainedFrame(VirtualNode root, PixelRectangle viewportBounds, RenderFrameBatch batch)
    {
        if (!options.EnableSegmentedRetainedFrame)
        {
            return SegmentedRetainedFrameShadowResult.Disabled;
        }

        var shadow = _segmentedRetainedFrame ??= new SegmentedRetainedFrameShadowHarness();
        if (shadow.Owner.CommandCount == 0 || batch.DirtyCommandRanges.Count == 0 || pipeline.LastRetainedInputSnapshot is null)
        {
            return shadow.ApplyFull(batch, root);
        }

        var result = shadow.TryAcceptPartial(pipeline.LastRetainedInputSnapshot, viewportBounds, batch, root);
        if (result.Kind == SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial || result.Kind == SegmentedRetainedFrameShadowResultKind.ShadowRejected)
        {
            return result;
        }

        return shadow.ApplyFull(batch, root, result.Reason, result.PlanKind);
    }

    public void Dispose()
    {
        _segmentedRetainedFrame?.Dispose();
    }
}