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
        if (snapshot is null)
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.MissingRetainedSnapshot);
        }

        if (snapshot.Viewport != currentViewport)
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.ViewportChanged, snapshot.DirtyElementRanges);
        }

        if (!StyleOnlyPatchEligibility.IsLayoutReuseEligible(snapshot.DirtyClassifications, viewportChanged: false))
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.NotStyleOnly, snapshot.DirtyElementRanges);
        }

        if (!StyleOnlyPatchEligibility.TryMapStableCommandRanges(snapshot.ElementCommandRanges, snapshot.DirtyElementRanges, out var dirtyCommandRanges))
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.UnstableCommandRange, snapshot.DirtyElementRanges);
        }

        if (!StyleOnlyHitTargetPatch.TryBuildPatchedHitTargets(snapshot.HitTargets, snapshot.LayoutResult.Elements, snapshot.DirtyElementRanges, out var patchedHitTargets))
        {
            return RetainedPartialApplyPlan.FallbackFull(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, snapshot.DirtyElementRanges);
        }

        if (!ResourcesMatch(retainedResources, replacementResources))
        {
            return RetainedPartialApplyPlan.Rejected(
                RetainedPartialApplyFallbackReason.ResourceOwnershipMismatch,
                snapshot.DirtyElementRanges,
                dirtyCommandRanges);
        }

        return RetainedPartialApplyPlan.AppliedPartial(snapshot.DirtyElementRanges, dirtyCommandRanges, patchedHitTargets);
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