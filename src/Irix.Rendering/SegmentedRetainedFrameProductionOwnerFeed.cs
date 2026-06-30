using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly struct RenderPipelineProductionOwnerOptions(bool EnableSegmentedRetainedFrameRuntimeOwner) : IEquatable<RenderPipelineProductionOwnerOptions>
{

    public bool EnableSegmentedRetainedFrameRuntimeOwner { get; } = EnableSegmentedRetainedFrameRuntimeOwner;

    public static RenderPipelineProductionOwnerOptions Disabled => default;

    public static RenderPipelineProductionOwnerOptions SegmentedRetainedFrameRuntimeOwnerEnabled => new(true);

    public bool Equals(RenderPipelineProductionOwnerOptions other)
    {
        return EnableSegmentedRetainedFrameRuntimeOwner == other.EnableSegmentedRetainedFrameRuntimeOwner;
    }

    public override bool Equals(object? obj) => obj is RenderPipelineProductionOwnerOptions other && Equals(other);

    public override int GetHashCode() => EnableSegmentedRetainedFrameRuntimeOwner.GetHashCode();

    public static bool operator ==(RenderPipelineProductionOwnerOptions left, RenderPipelineProductionOwnerOptions right) => left.Equals(right);

    public static bool operator !=(RenderPipelineProductionOwnerOptions left, RenderPipelineProductionOwnerOptions right) => !left.Equals(right);
}

internal readonly struct SegmentedRetainedFrameProductionOwnerFeedResult(
    SegmentedRetainedFrameShadowResult ShadowResult,
    bool RuntimeOwnerEnabled,
    bool FallbackApplied,
    bool OwnerStatePreservedBeforeFallback,
    ulong BatchFrameId = 0,
    int BatchCommandCount = 0,
    IFrameResourceResolver? BatchResources = null,
    object? BatchCommandOwner = null,
    ulong BatchCommandGeneration = 0) : IEquatable<SegmentedRetainedFrameProductionOwnerFeedResult>
{

    public SegmentedRetainedFrameShadowResult ShadowResult { get; } = ShadowResult;
    public bool RuntimeOwnerEnabled { get; } = RuntimeOwnerEnabled;
    public bool FallbackApplied { get; } = FallbackApplied;
    public bool OwnerStatePreservedBeforeFallback { get; } = OwnerStatePreservedBeforeFallback;
    public ulong BatchFrameId { get; } = BatchFrameId;
    public int BatchCommandCount { get; } = BatchCommandCount;
    public IFrameResourceResolver? BatchResources { get; } = BatchResources;
    public object? BatchCommandOwner { get; } = BatchCommandOwner;
    public ulong BatchCommandGeneration { get; } = BatchCommandGeneration;

    public static SegmentedRetainedFrameProductionOwnerFeedResult Disabled { get; } = new(
        SegmentedRetainedFrameShadowResult.Disabled,
        false,
        false,
        true);

    public SegmentedRetainedFrameShadowResultKind Kind => ShadowResult.Kind;

    public bool Equals(SegmentedRetainedFrameProductionOwnerFeedResult other)
    {
        return ShadowResult == other.ShadowResult
            && RuntimeOwnerEnabled == other.RuntimeOwnerEnabled
            && FallbackApplied == other.FallbackApplied
            && OwnerStatePreservedBeforeFallback == other.OwnerStatePreservedBeforeFallback
            && BatchFrameId == other.BatchFrameId
            && BatchCommandCount == other.BatchCommandCount
            && EqualityComparer<IFrameResourceResolver?>.Default.Equals(BatchResources, other.BatchResources)
            && EqualityComparer<object?>.Default.Equals(BatchCommandOwner, other.BatchCommandOwner)
            && BatchCommandGeneration == other.BatchCommandGeneration;
    }

    public override bool Equals(object? obj) => obj is SegmentedRetainedFrameProductionOwnerFeedResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ShadowResult);
        hash.Add(RuntimeOwnerEnabled);
        hash.Add(FallbackApplied);
        hash.Add(OwnerStatePreservedBeforeFallback);
        hash.Add(BatchFrameId);
        hash.Add(BatchCommandCount);
        hash.Add(BatchResources);
        hash.Add(BatchCommandOwner);
        hash.Add(BatchCommandGeneration);
        return hash.ToHashCode();
    }

    public static bool operator ==(SegmentedRetainedFrameProductionOwnerFeedResult left, SegmentedRetainedFrameProductionOwnerFeedResult right) => left.Equals(right);

    public static bool operator !=(SegmentedRetainedFrameProductionOwnerFeedResult left, SegmentedRetainedFrameProductionOwnerFeedResult right) => !left.Equals(right);
}

internal sealed partial class SegmentedRetainedFrameProductionOwnerFeed(RenderPipeline pipeline, RenderPipelineProductionOwnerOptions options = default) : IDisposable
{
    private readonly RenderPipeline _pipeline = pipeline;
    private readonly RetainedRenderFrameSegmentOwnership? _segmentOwnership = options.EnableSegmentedRetainedFrameRuntimeOwner
        ? new RetainedRenderFrameSegmentOwnership(pipeline.RetainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled)
        : null;

    public SegmentedRetainedFrameProductionOwnerFeedResult LastResult { get; private set; } = SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;

    public bool HasRuntimeOwner => _segmentOwnership?.HasSegmentedOwner == true;

    public SegmentedRetainedFrameRuntimeOwner? RuntimeOwner => _segmentOwnership?.RuntimeOwner;

    public RetainedRenderFrameSegmentOwnership? SegmentOwnership => _segmentOwnership;

    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds, TextBufferSnapshot textSnapshot, IReadOnlyList<int>? dirtyNodes = null, TextBufferSnapshot? prevTextSnapshot = null, VirtualNode previousRoot = default)
    {
        var tree = new VirtualNodeTree(root, textSnapshot);
        var previousTree = previousRoot.Kind == VirtualNodeKind.None ? default : new VirtualNodeTree(previousRoot, prevTextSnapshot ?? default);
        var batch = Build(tree, viewportBounds, dirtyNodes, previousTree);
        return batch;
    }

    public RenderFrameBatch Build(VirtualNodeTree tree, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null, VirtualNodeTree previousTree = default)
    {
        var batch = _pipeline.Build(tree, viewportBounds, dirtyNodes, previousTree);
        LastResult = UpdateRuntimeOwner(tree.Root, viewportBounds, batch);
        return batch;
    }

    private SegmentedRetainedFrameProductionOwnerFeedResult UpdateRuntimeOwner(VirtualNode root, PixelRectangle viewportBounds, RenderFrameBatch batch)
    {
        var snapshot = _pipeline.HasLastRetainedInputSnapshot ? _pipeline.LastRetainedInputSnapshot : (RenderPipelineRetainedInputSnapshot?)null;
        return _segmentOwnership?.Update(snapshot, root, viewportBounds, batch)
            ?? SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;
    }

    public void Dispose()
    {
        _segmentOwnership?.Dispose();
    }
}
