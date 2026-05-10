namespace Irix.Rendering;

internal enum StyleOnlyPatchFallbackReason : byte
{
    None,
    MissingRetainedLayout,
    ViewportChanged,
    NotStyleOnly,
    UnstableCommandRange,
    HitTargetPatchFailed
}

internal sealed record StyleOnlyPatchPlan(
    bool Eligible,
    StyleOnlyPatchFallbackReason FallbackReason,
    IReadOnlyList<(int Start, int Count)> DirtyElementRanges,
    IReadOnlyList<(int Start, int Count)> DirtyCommandRanges,
    IReadOnlyList<HitTestTarget> PatchedHitTargets)
{
    public static StyleOnlyPatchPlan CreateEligible(
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges,
        IReadOnlyList<(int Start, int Count)> dirtyCommandRanges,
        IReadOnlyList<HitTestTarget> patchedHitTargets)
    {
        return new StyleOnlyPatchPlan(
            true,
            StyleOnlyPatchFallbackReason.None,
            dirtyElementRanges.ToArray(),
            dirtyCommandRanges.ToArray(),
            patchedHitTargets.ToArray());
    }

    public static StyleOnlyPatchPlan CreateFallback(
        StyleOnlyPatchFallbackReason fallbackReason,
        IReadOnlyList<(int Start, int Count)>? dirtyElementRanges = null)
    {
        return new StyleOnlyPatchPlan(
            false,
            fallbackReason,
            dirtyElementRanges?.ToArray() ?? [],
            [],
            []);
    }
}

internal static class StyleOnlyPatchPlanBuilder
{
    public static StyleOnlyPatchPlan Build(
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications,
        bool viewportChanged,
        LayoutTreeResult? retainedLayout,
        ElementCommandRange[] retainedElementCommandRanges,
        IReadOnlyList<HitTestTarget> retainedHitTargets,
        IReadOnlyList<LayoutElement> nextLayoutElements,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges)
    {
        if (retainedLayout is null)
        {
            return StyleOnlyPatchPlan.CreateFallback(StyleOnlyPatchFallbackReason.MissingRetainedLayout, dirtyElementRanges);
        }

        if (viewportChanged)
        {
            return StyleOnlyPatchPlan.CreateFallback(StyleOnlyPatchFallbackReason.ViewportChanged, dirtyElementRanges);
        }

        if (!StyleOnlyPatchEligibility.IsLayoutReuseEligible(dirtyClassifications, viewportChanged: false))
        {
            return StyleOnlyPatchPlan.CreateFallback(StyleOnlyPatchFallbackReason.NotStyleOnly, dirtyElementRanges);
        }

        if (!StyleOnlyPatchEligibility.TryMapStableCommandRanges(retainedElementCommandRanges, dirtyElementRanges, out var dirtyCommandRanges))
        {
            return StyleOnlyPatchPlan.CreateFallback(StyleOnlyPatchFallbackReason.UnstableCommandRange, dirtyElementRanges);
        }

        if (!StyleOnlyHitTargetPatch.TryBuildPatchedHitTargets(retainedHitTargets, nextLayoutElements, dirtyElementRanges, out var patchedHitTargets))
        {
            return StyleOnlyPatchPlan.CreateFallback(StyleOnlyPatchFallbackReason.HitTargetPatchFailed, dirtyElementRanges);
        }

        return StyleOnlyPatchPlan.CreateEligible(dirtyElementRanges, dirtyCommandRanges, patchedHitTargets);
    }
}