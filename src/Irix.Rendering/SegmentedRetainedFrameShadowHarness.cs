using Irix.Platform;

namespace Irix.Rendering;

internal readonly record struct SegmentedRetainedFrameShadowApplyResult(
    bool Accepted,
    RetainedPartialApplyFallbackReason Reason,
    RetainedPartialApplyResultKind PlanKind,
    IReadOnlyList<SegmentedFrameRead> Reads);

internal sealed class SegmentedRetainedFrameShadowHarness : IDisposable
{
    private readonly SegmentedRetainedFrameOwner _owner = new();

    public SegmentedRetainedFrameOwner Owner => _owner;

    public void ApplyFull(RenderFrameBatch batch, VirtualNode retainedRoot)
    {
        _owner.ApplyFull(batch, RetainedResourceSnapshot.Capture(batch.Resources), retainedRoot);
    }

    public SegmentedRetainedFrameShadowApplyResult TryAcceptPartial(
        RenderPipelineRetainedInputSnapshot snapshot,
        PixelRectangle viewport,
        RenderFrameBatch batch,
        VirtualNode nextRoot)
    {
        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, batch.Resources, batch.Resources);
        if (plan.Kind != RetainedPartialApplyResultKind.AppliedPartial)
        {
            return new SegmentedRetainedFrameShadowApplyResult(false, plan.Reason, plan.Kind, []);
        }

        var dirtyDfsIndices = new int[snapshot.DirtyClassifications.Count];
        for (var i = 0; i < snapshot.DirtyClassifications.Count; i++)
        {
            dirtyDfsIndices[i] = snapshot.DirtyClassifications[i].DfsIndex;
        }

        var hitTargetProjection = HitTargetMetadataProjector.ProjectActionIds(_owner.RetainedRoot, nextRoot, dirtyDfsIndices, snapshot.HitTargets);
        if (!hitTargetProjection.Succeeded)
        {
            return new SegmentedRetainedFrameShadowApplyResult(false, hitTargetProjection.FallbackReason, plan.Kind, []);
        }

        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(_owner.RetainedRoot, nextRoot, snapshot.DirtyClassifications);
        if (!rootPatch.Succeeded)
        {
            return new SegmentedRetainedFrameShadowApplyResult(false, rootPatch.FallbackReason, plan.Kind, []);
        }

        if (!_owner.TryAcceptPartial(batch, RetainedResourceSnapshot.Capture(batch.Resources), rootPatch))
        {
            return new SegmentedRetainedFrameShadowApplyResult(false, RetainedPartialApplyFallbackReason.UnstableCommandRange, plan.Kind, []);
        }

        return new SegmentedRetainedFrameShadowApplyResult(true, RetainedPartialApplyFallbackReason.None, plan.Kind, _owner.ReadSegments());
    }

    public void Dispose()
    {
        _owner.Dispose();
    }
}