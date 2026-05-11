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
    bool OwnerStatePreservedBeforeFallback)
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
    private SegmentedRetainedFrameRuntimeOwner? _runtimeOwner;

    public SegmentedRetainedFrameProductionOwnerFeedResult LastResult { get; private set; } = SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;

    public bool HasRuntimeOwner => _runtimeOwner is not null;

    public SegmentedRetainedFrameRuntimeOwner? RuntimeOwner => _runtimeOwner;

    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null)
    {
        var batch = pipeline.Build(root, viewportBounds, dirtyNodes);
        LastResult = UpdateRuntimeOwner(root, viewportBounds, batch);
        return batch;
    }

    private SegmentedRetainedFrameProductionOwnerFeedResult UpdateRuntimeOwner(VirtualNode root, PixelRectangle viewportBounds, RenderFrameBatch batch)
    {
        if (!options.EnableSegmentedRetainedFrameRuntimeOwner)
        {
            return SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;
        }

        var owner = _runtimeOwner ??= new SegmentedRetainedFrameRuntimeOwner();
        if (owner.CommandCount == 0)
        {
            return new SegmentedRetainedFrameProductionOwnerFeedResult(owner.ApplyFull(batch, root), true, false, true);
        }

        if (batch.DirtyCommandRanges.Count == 0 || pipeline.LastRetainedInputSnapshot is null)
        {
            return new SegmentedRetainedFrameProductionOwnerFeedResult(owner.Rebuild(batch, root), true, false, true);
        }

        var snapshot = pipeline.LastRetainedInputSnapshot;
        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewportBounds, batch.Resources, batch.Resources);
        if (plan.Kind != RetainedPartialApplyResultKind.AppliedPartial)
        {
            return ApplyFallback(owner, batch, root, plan.Reason, plan.Kind, ownerStatePreserved: true);
        }

        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(owner.RetainedRoot, root, snapshot.DirtyClassifications);
        if (!rootPatch.Succeeded)
        {
            return ApplyFallback(owner, batch, root, rootPatch.FallbackReason, plan.Kind, ownerStatePreserved: true);
        }

        var beforeRoot = owner.RetainedRoot;
        var beforeSegments = owner.ResourceSegments.ToArray();
        if (owner.TryAcceptPartial(batch, rootPatch))
        {
            return new SegmentedRetainedFrameProductionOwnerFeedResult(
                new SegmentedRetainedFrameShadowResult(
                    SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial,
                    RetainedPartialApplyFallbackReason.None,
                    plan.Kind,
                    owner.ReadSegments()),
                true,
                false,
                true);
        }

        var statePreserved = beforeRoot.Equals(owner.RetainedRoot) && SegmentsEqual(beforeSegments, owner.ResourceSegments);
        return ApplyFallback(owner, batch, root, RetainedPartialApplyFallbackReason.UnstableCommandRange, plan.Kind, statePreserved);
    }

    private static SegmentedRetainedFrameProductionOwnerFeedResult ApplyFallback(
        SegmentedRetainedFrameRuntimeOwner owner,
        RenderFrameBatch batch,
        VirtualNode root,
        RetainedPartialApplyFallbackReason reason,
        RetainedPartialApplyResultKind planKind,
        bool ownerStatePreserved)
    {
        var result = owner.ApplyFallbackFull(batch, root, reason);
        return new SegmentedRetainedFrameProductionOwnerFeedResult(
            result with { PlanKind = planKind },
            true,
            true,
            ownerStatePreserved);
    }

    private static bool SegmentsEqual(IReadOnlyList<RetainedResourceSegment> left, IReadOnlyList<RetainedResourceSegment> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].CommandStart != right[i].CommandStart
                || left[i].CommandCount != right[i].CommandCount
                || !ReferenceEquals(left[i].Snapshot, right[i].Snapshot))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        _runtimeOwner?.Dispose();
    }
}