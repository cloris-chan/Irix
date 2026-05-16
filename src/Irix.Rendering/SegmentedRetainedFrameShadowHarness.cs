using Irix.Platform;

namespace Irix.Rendering;

internal enum SegmentedRetainedFrameShadowResultKind : byte
{
    Disabled,
    ShadowAppliedPartial,
    ShadowFallbackFull,
    ShadowRejected
}

internal readonly struct SegmentedRetainedFrameShadowResult(
    SegmentedRetainedFrameShadowResultKind Kind,
    RetainedPartialApplyFallbackReason Reason,
    RetainedPartialApplyResultKind PlanKind,
    IReadOnlyList<SegmentedFrameRead> Reads) : IEquatable<SegmentedRetainedFrameShadowResult>
{
    public SegmentedRetainedFrameShadowResultKind Kind { get; } = Kind;
    public RetainedPartialApplyFallbackReason Reason { get; } = Reason;
    public RetainedPartialApplyResultKind PlanKind { get; } = PlanKind;
    public IReadOnlyList<SegmentedFrameRead> Reads { get; } = Reads;

    public static SegmentedRetainedFrameShadowResult Disabled { get; } = new(
        SegmentedRetainedFrameShadowResultKind.Disabled,
        RetainedPartialApplyFallbackReason.None,
        RetainedPartialApplyResultKind.FallbackFull,
        []);

    public bool Accepted => Kind == SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial;

    public bool Equals(SegmentedRetainedFrameShadowResult other)
    {
        return Kind == other.Kind
            && Reason == other.Reason
            && PlanKind == other.PlanKind
            && EqualityComparer<IReadOnlyList<SegmentedFrameRead>>.Default.Equals(Reads, other.Reads);
    }

    public override bool Equals(object? obj) => obj is SegmentedRetainedFrameShadowResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Reason, PlanKind, Reads);

    public static bool operator ==(SegmentedRetainedFrameShadowResult left, SegmentedRetainedFrameShadowResult right) => left.Equals(right);

    public static bool operator !=(SegmentedRetainedFrameShadowResult left, SegmentedRetainedFrameShadowResult right) => !left.Equals(right);
}

internal sealed class SegmentedRetainedFrameShadowHarness : IDisposable
{
    private readonly SegmentedRetainedFrameOwner _owner = new();

    public SegmentedRetainedFrameOwner Owner => _owner;

    public SegmentedRetainedFrameShadowResult ApplyFull(
        RenderFrameBatch batch,
        VirtualNode retainedRoot,
        RetainedPartialApplyFallbackReason reason = RetainedPartialApplyFallbackReason.None,
        RetainedPartialApplyResultKind planKind = RetainedPartialApplyResultKind.FallbackFull)
    {
        _owner.ApplyFull(batch, RetainedResourceSnapshot.Capture(batch.Resources), retainedRoot);
        return new SegmentedRetainedFrameShadowResult(
            SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull,
            reason,
            planKind,
            _owner.ReadSegments());
    }

    public SegmentedRetainedFrameShadowResult TryAcceptPartial(
        RenderPipelineRetainedInputSnapshot snapshot,
        PixelRectangle viewport,
        RenderFrameBatch batch,
        VirtualNode nextRoot)
    {
        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, batch.Resources, batch.Resources);
        if (plan.Kind != RetainedPartialApplyResultKind.AppliedPartial)
        {
            return new SegmentedRetainedFrameShadowResult(MapPlanKind(plan.Kind), plan.Reason, plan.Kind, []);
        }

        var dirtyDfsIndices = new int[snapshot.DirtyClassifications.Count];
        for (var i = 0; i < snapshot.DirtyClassifications.Count; i++)
        {
            dirtyDfsIndices[i] = snapshot.DirtyClassifications[i].DfsIndex;
        }

        var hitTargetProjection = HitTargetMetadataProjector.ProjectActionIds(_owner.RetainedRoot, nextRoot, dirtyDfsIndices, _owner.HitTargets);
        if (!hitTargetProjection.Succeeded)
        {
            return new SegmentedRetainedFrameShadowResult(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, hitTargetProjection.FallbackReason, plan.Kind, []);
        }

        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(_owner.RetainedRoot, nextRoot, snapshot.DirtyClassifications, snapshot.PreviousTextSnapshot, snapshot.TextSnapshot);
        if (!rootPatch.Succeeded)
        {
            return new SegmentedRetainedFrameShadowResult(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, rootPatch.FallbackReason, plan.Kind, []);
        }

        if (!_owner.TryAcceptPartial(batch, RetainedResourceSnapshot.Capture(batch.Resources), rootPatch, hitTargetProjection.HitTargets))
        {
            return new SegmentedRetainedFrameShadowResult(SegmentedRetainedFrameShadowResultKind.ShadowRejected, RetainedPartialApplyFallbackReason.UnstableCommandRange, plan.Kind, []);
        }

        return new SegmentedRetainedFrameShadowResult(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, RetainedPartialApplyFallbackReason.None, plan.Kind, _owner.ReadSegments());
    }

    private static SegmentedRetainedFrameShadowResultKind MapPlanKind(RetainedPartialApplyResultKind planKind)
    {
        return planKind == RetainedPartialApplyResultKind.Rejected
            ? SegmentedRetainedFrameShadowResultKind.ShadowRejected
            : SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull;
    }

    public void Dispose()
    {
        _owner.Dispose();
    }
}
