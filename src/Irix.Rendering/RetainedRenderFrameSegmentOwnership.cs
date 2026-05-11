using Irix.Platform;

namespace Irix.Rendering;

internal readonly record struct RetainedRenderFrameSegmentOwnershipOptions
{
    public bool EnableSegmentedOwner { get; init; }

    public static RetainedRenderFrameSegmentOwnershipOptions Disabled => default;

    public static RetainedRenderFrameSegmentOwnershipOptions Enabled => new() { EnableSegmentedOwner = true };
}

internal sealed class RetainedRenderFrameSegmentOwnership(RetainedRenderFrame retainedFrame, RetainedRenderFrameSegmentOwnershipOptions options = default) : IDisposable
{
    private SegmentedRetainedFrameRuntimeOwner? _runtimeOwner;

    public RetainedRenderFrame RetainedFrame => retainedFrame;

    public SegmentedRetainedFrameProductionOwnerFeedResult LastResult { get; private set; } = SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;

    public bool HasSegmentedOwner => _runtimeOwner is not null;

    public SegmentedRetainedFrameRuntimeOwner? RuntimeOwner => _runtimeOwner;

    public SegmentedRetainedFrameProductionOwnerFeedResult Update(
        RenderPipelineRetainedInputSnapshot? snapshot,
        VirtualNode root,
        PixelRectangle viewportBounds,
        RenderFrameBatch batch)
    {
        LastResult = UpdateSegmentedOwner(snapshot, root, viewportBounds, batch);
        return LastResult;
    }

    private SegmentedRetainedFrameProductionOwnerFeedResult UpdateSegmentedOwner(
        RenderPipelineRetainedInputSnapshot? snapshot,
        VirtualNode root,
        PixelRectangle viewportBounds,
        RenderFrameBatch batch)
    {
        if (!options.EnableSegmentedOwner)
        {
            return SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;
        }

        var owner = _runtimeOwner ??= new SegmentedRetainedFrameRuntimeOwner();
        if (owner.CommandCount == 0)
        {
            return new SegmentedRetainedFrameProductionOwnerFeedResult(owner.ApplyFull(batch, root), true, false, true);
        }

        if (batch.DirtyCommandRanges.Count == 0 || snapshot is null)
        {
            return new SegmentedRetainedFrameProductionOwnerFeedResult(owner.Rebuild(batch, root), true, false, true);
        }

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewportBounds, batch.Resources, batch.Resources);
        if (plan.Kind != RetainedPartialApplyResultKind.AppliedPartial)
        {
            return ApplyFallback(owner, batch, root, plan.Reason, plan.Kind, ownerStatePreserved: true);
        }

        var dirtyDfsIndices = new int[snapshot.DirtyClassifications.Count];
        for (var i = 0; i < snapshot.DirtyClassifications.Count; i++)
        {
            dirtyDfsIndices[i] = snapshot.DirtyClassifications[i].DfsIndex;
        }

        var hitTargetProjection = HitTargetMetadataProjector.ProjectActionIds(owner.RetainedRoot, root, dirtyDfsIndices, owner.HitTargets);
        if (!hitTargetProjection.Succeeded)
        {
            return ApplyFallback(owner, batch, root, hitTargetProjection.FallbackReason, plan.Kind, ownerStatePreserved: true);
        }

        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(owner.RetainedRoot, root, snapshot.DirtyClassifications);
        if (!rootPatch.Succeeded)
        {
            return ApplyFallback(owner, batch, root, rootPatch.FallbackReason, plan.Kind, ownerStatePreserved: true);
        }

        var beforeRoot = owner.RetainedRoot;
        var beforeSegments = owner.ResourceSegments.ToArray();
        var beforeHitTargets = owner.HitTargets.ToArray();
        var beforeReads = owner.ReadSegments();
        if (owner.TryAcceptPartial(batch, rootPatch, hitTargetProjection.HitTargets))
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

        var statePreserved = beforeRoot.Equals(owner.RetainedRoot)
            && SegmentsEqual(beforeSegments, owner.ResourceSegments)
            && HitTargetsEqual(beforeHitTargets, owner.HitTargets)
            && ReadsEqual(beforeReads, owner.ReadSegments());
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

    private static bool HitTargetsEqual(IReadOnlyList<HitTestTarget> left, IReadOnlyList<HitTestTarget> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReadsEqual(IReadOnlyList<SegmentedFrameRead> left, IReadOnlyList<SegmentedFrameRead> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].CommandStart != right[i].CommandStart
                || !ReferenceEquals(left[i].Resolver, right[i].Resolver)
                || left[i].Commands.Length != right[i].Commands.Length)
            {
                return false;
            }

            for (var commandIndex = 0; commandIndex < left[i].Commands.Length; commandIndex++)
            {
                if (left[i].Commands[commandIndex] != right[i].Commands[commandIndex])
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void Dispose()
    {
        _runtimeOwner?.Dispose();
    }
}