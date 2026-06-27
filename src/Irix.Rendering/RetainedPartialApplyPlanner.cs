using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal enum RetainedPartialApplyResultKind : byte
{
    AppliedPartial,
    FallbackFull,
    Rejected
}

internal enum RetainedPartialApplyFallbackReason : byte
{
    None,
    MissingRetainedSnapshot,
    NotStyleOnly,
    ViewportChanged,
    UnstableCommandRange,
    ResourceOwnershipMismatch,
    HitTargetPatchFailed
}

internal sealed record RetainedPartialApplyPlan(
    RetainedPartialApplyResultKind Kind,
    RetainedPartialApplyFallbackReason Reason,
    IReadOnlyList<(int Start, int Count)> DirtyElementRanges,
    IReadOnlyList<(int Start, int Count)> DirtyCommandRanges,
    IReadOnlyList<HitTestTarget> PatchedHitTargets)
{
    public static RetainedPartialApplyPlan AppliedPartial(
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges,
        IReadOnlyList<(int Start, int Count)> dirtyCommandRanges,
        IReadOnlyList<HitTestTarget> patchedHitTargets)
    {
        return new RetainedPartialApplyPlan(
            RetainedPartialApplyResultKind.AppliedPartial,
            RetainedPartialApplyFallbackReason.None,
            dirtyElementRanges.ToArray(),
            dirtyCommandRanges.ToArray(),
            patchedHitTargets.ToArray());
    }

    public static RetainedPartialApplyPlan FallbackFull(
        RetainedPartialApplyFallbackReason reason,
        IReadOnlyList<(int Start, int Count)>? dirtyElementRanges = null)
    {
        return new RetainedPartialApplyPlan(
            RetainedPartialApplyResultKind.FallbackFull,
            reason,
            dirtyElementRanges?.ToArray() ?? [],
            [],
            []);
    }

    public static RetainedPartialApplyPlan Rejected(
        RetainedPartialApplyFallbackReason reason,
        IReadOnlyList<(int Start, int Count)>? dirtyElementRanges = null,
        IReadOnlyList<(int Start, int Count)>? dirtyCommandRanges = null)
    {
        return new RetainedPartialApplyPlan(
            RetainedPartialApplyResultKind.Rejected,
            reason,
            dirtyElementRanges?.ToArray() ?? [],
            dirtyCommandRanges?.ToArray() ?? [],
            []);
    }
}

internal static class RetainedPartialApplyPlanner
{
    public static RetainedPartialApplyPlan Plan(
        RenderPipelineRetainedInputSnapshot? snapshot,
        PixelRectangle currentViewport,
        IFrameResourceResolver retainedResources,
        IFrameResourceResolver replacementResources)
    {
        if (snapshot is not { } retainedSnapshot)
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.MissingRetainedSnapshot);
        }

        if (retainedSnapshot.Viewport != currentViewport)
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.ViewportChanged, retainedSnapshot.DirtyElementRanges);
        }

        if (!StyleOnlyPatchEligibility.IsLayoutReuseEligible(retainedSnapshot.DirtyClassifications, viewportChanged: false))
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.NotStyleOnly, retainedSnapshot.DirtyElementRanges);
        }

        if (!RangeUtils.TryMapContiguousElementRangesToCommandRanges(retainedSnapshot.ElementCommandRanges, retainedSnapshot.DirtyElementRanges, out var dirtyCommandRanges))
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.UnstableCommandRange, retainedSnapshot.DirtyElementRanges);
        }

        if (!StyleOnlyHitTargetPatch.TryBuildPatchedHitTargets(retainedSnapshot.HitTargets, retainedSnapshot.LayoutResult.Elements, retainedSnapshot.DirtyElementRanges, out var patchedHitTargets))
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, retainedSnapshot.DirtyElementRanges);
        }

        if (!ResourcesMatch(retainedResources, replacementResources))
        {
            return RetainedPartialApplyPlan.Rejected(
                RetainedPartialApplyFallbackReason.ResourceOwnershipMismatch,
                retainedSnapshot.DirtyElementRanges,
                dirtyCommandRanges);
        }

        return RetainedPartialApplyPlan.AppliedPartial(retainedSnapshot.DirtyElementRanges, dirtyCommandRanges, patchedHitTargets);
    }

    private static bool ResourcesMatch(IFrameResourceResolver retainedResources, IFrameResourceResolver replacementResources)
    {
        if (retainedResources is FrameDrawingResources retainedFrameResources && replacementResources is FrameDrawingResources replacementFrameResources)
        {
            return ReferenceEquals(retainedFrameResources, replacementFrameResources)
                && retainedFrameResources.FrameId == replacementFrameResources.FrameId;
        }

        return ReferenceEquals(retainedResources, replacementResources);
    }
}
