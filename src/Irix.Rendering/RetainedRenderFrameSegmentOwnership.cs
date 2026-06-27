using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly struct RetainedRenderFrameSegmentOwnershipOptions(
    bool EnableSegmentedOwner,
    Func<IFrameResourceResolver, RetainedResourceSnapshot>? ResourceSnapshotFactory = null) : IEquatable<RetainedRenderFrameSegmentOwnershipOptions>
{
    public bool EnableSegmentedOwner { get; } = EnableSegmentedOwner;

    public Func<IFrameResourceResolver, RetainedResourceSnapshot>? ResourceSnapshotFactory { get; } = ResourceSnapshotFactory;

    public static RetainedRenderFrameSegmentOwnershipOptions Disabled => default;

    public static RetainedRenderFrameSegmentOwnershipOptions Enabled => new(true);

    public bool Equals(RetainedRenderFrameSegmentOwnershipOptions other)
    {
        return EnableSegmentedOwner == other.EnableSegmentedOwner
            && EqualityComparer<Func<IFrameResourceResolver, RetainedResourceSnapshot>?>.Default.Equals(ResourceSnapshotFactory, other.ResourceSnapshotFactory);
    }

    public override bool Equals(object? obj) => obj is RetainedRenderFrameSegmentOwnershipOptions other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(EnableSegmentedOwner, ResourceSnapshotFactory);

    public static bool operator ==(RetainedRenderFrameSegmentOwnershipOptions left, RetainedRenderFrameSegmentOwnershipOptions right) => left.Equals(right);

    public static bool operator !=(RetainedRenderFrameSegmentOwnershipOptions left, RetainedRenderFrameSegmentOwnershipOptions right) => !left.Equals(right);
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
        LastResult = StampBatch(UpdateSegmentedOwner(snapshot, root, viewportBounds, batch), batch);
        return LastResult;
    }

    private static SegmentedRetainedFrameProductionOwnerFeedResult StampBatch(SegmentedRetainedFrameProductionOwnerFeedResult result, RenderFrameBatch batch)
    {
        return new SegmentedRetainedFrameProductionOwnerFeedResult(
            result.ShadowResult,
            result.RuntimeOwnerEnabled,
            result.FallbackApplied,
            result.OwnerStatePreservedBeforeFallback,
            batch.Resources is FrameDrawingResources frameResources ? frameResources.FrameId : 0,
            batch.Commands.Count,
            batch.Resources,
            batch.Commands.Owner);
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

        var owner = _runtimeOwner ??= new SegmentedRetainedFrameRuntimeOwner(options.ResourceSnapshotFactory);
        if (owner.CommandCount == 0)
        {
            return new SegmentedRetainedFrameProductionOwnerFeedResult(owner.ApplyFull(batch, root), true, false, true);
        }

        if (batch.DirtyCommandRanges.Count == 0 || snapshot is not { } retainedSnapshot)
        {
            return new SegmentedRetainedFrameProductionOwnerFeedResult(owner.Rebuild(batch, root), true, false, true);
        }

        var plan = RetainedPartialApplyPlanner.Plan(retainedSnapshot, viewportBounds, batch.Resources, batch.Resources);
        if (plan.Kind != RetainedPartialApplyResultKind.AppliedPartial)
        {
            return ApplyFallback(owner, batch, root, plan.Reason, plan.Kind);
        }

        var dirtyDfsIndices = new int[retainedSnapshot.DirtyClassifications.Count];
        for (var i = 0; i < retainedSnapshot.DirtyClassifications.Count; i++)
        {
            dirtyDfsIndices[i] = retainedSnapshot.DirtyClassifications[i].DfsIndex;
        }

        var hitTargetProjection = HitTargetMetadataProjector.ProjectActionIds(owner.RetainedRoot, root, dirtyDfsIndices, owner.HitTargets);
        if (!hitTargetProjection.Succeeded)
        {
            return ApplyFallback(owner, batch, root, hitTargetProjection.FallbackReason, plan.Kind);
        }

        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(owner.RetainedRoot, root, retainedSnapshot.DirtyClassifications, retainedSnapshot.PreviousTextSnapshot, retainedSnapshot.TextSnapshot);
        if (!rootPatch.Succeeded)
        {
            return ApplyFallback(owner, batch, root, rootPatch.FallbackReason, plan.Kind);
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
        bool ownerStatePreserved = true)
    {
        return new SegmentedRetainedFrameProductionOwnerFeedResult(
            owner.ApplyFallbackFull(batch, root, reason, planKind),
            true,
            true,
            ownerStatePreserved);
    }

    private static SegmentedRetainedFrameProductionOwnerFeedResult ReportFallback(
        SegmentedRetainedFrameRuntimeOwner owner,
        RetainedPartialApplyFallbackReason reason,
        RetainedPartialApplyResultKind planKind,
        bool ownerStatePreserved)
    {
        return new SegmentedRetainedFrameProductionOwnerFeedResult(
            new SegmentedRetainedFrameShadowResult(
                SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull,
                reason,
                planKind,
                owner.ReadSegments()),
            true,
            false,
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

    public void InvalidateSegmentedOwner()
    {
        _runtimeOwner?.Invalidate();
    }

    public bool TryGetSegmentedOwnerActionIdAt(int x, int y, out ActionId actionId)
    {
        if (_runtimeOwner is null)
        {
            actionId = ActionId.None;
            return false;
        }

        foreach (var hitTarget in _runtimeOwner.HitTargets)
        {
            if (x < hitTarget.Bounds.X
                || y < hitTarget.Bounds.Y
                || x >= hitTarget.Bounds.X + hitTarget.Bounds.Width
                || y >= hitTarget.Bounds.Y + hitTarget.Bounds.Height)
            {
                continue;
            }

            if (hitTarget.ClipBounds.Width > 0 && hitTarget.ClipBounds.Height > 0)
            {
                if (x < hitTarget.ClipBounds.X
                    || y < hitTarget.ClipBounds.Y
                    || x >= hitTarget.ClipBounds.X + hitTarget.ClipBounds.Width
                    || y >= hitTarget.ClipBounds.Y + hitTarget.ClipBounds.Height)
                {
                    continue;
                }
            }

            actionId = hitTarget.ActionId;
            return true;
        }

        actionId = ActionId.None;
        return false;
    }

    public void Dispose()
    {
        _runtimeOwner?.Dispose();
    }
}
