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

internal enum StyleOnlyPatchPlanCase : byte
{
    HoverOnly,
    LayoutAffecting
}

internal sealed record StyleOnlyPatchPlan(
    bool Eligible,
    StyleOnlyPatchFallbackReason FallbackReason,
    IndexRangeList DirtyElementRanges,
    IndexRangeList DirtyCommandRanges,
    IReadOnlyList<HitTestTarget> PatchedHitTargets)
{
    public static StyleOnlyPatchPlan CreateEligible(
        IndexRangeList dirtyElementRanges,
        IndexRangeList dirtyCommandRanges,
        IReadOnlyList<HitTestTarget> patchedHitTargets)
    {
        return new StyleOnlyPatchPlan(
            true,
            StyleOnlyPatchFallbackReason.None,
            dirtyElementRanges,
            dirtyCommandRanges,
            patchedHitTargets.ToArray());
    }

    public static StyleOnlyPatchPlan CreateFallback(
        StyleOnlyPatchFallbackReason fallbackReason,
        IndexRangeList dirtyElementRanges = default)
    {
        return new StyleOnlyPatchPlan(
            false,
            fallbackReason,
            dirtyElementRanges,
            IndexRangeList.Empty,
            []);
    }
}

internal readonly struct StyleOnlyPatchPlanDiagnosticSnapshot(
    StyleOnlyPatchPlanCase Case,
    bool Eligible,
    StyleOnlyPatchFallbackReason FallbackReason,
    IReadOnlyList<(int Start, int Count)> DirtyElementRanges,
    IReadOnlyList<(int Start, int Count)> DirtyCommandRanges,
    int HitTargetCount) : IEquatable<StyleOnlyPatchPlanDiagnosticSnapshot>
{
    public StyleOnlyPatchPlanCase Case { get; } = Case;
    public bool Eligible { get; } = Eligible;
    public StyleOnlyPatchFallbackReason FallbackReason { get; } = FallbackReason;
    public IReadOnlyList<(int Start, int Count)> DirtyElementRanges { get; } = DirtyElementRanges;
    public IReadOnlyList<(int Start, int Count)> DirtyCommandRanges { get; } = DirtyCommandRanges;
    public int HitTargetCount { get; } = HitTargetCount;

    public static StyleOnlyPatchPlanDiagnosticSnapshot FromPlan(StyleOnlyPatchPlanCase @case, StyleOnlyPatchPlan plan)
    {
        return new StyleOnlyPatchPlanDiagnosticSnapshot(
            @case,
            plan.Eligible,
            plan.FallbackReason,
            plan.DirtyElementRanges.ToArray(),
            plan.DirtyCommandRanges.ToArray(),
            plan.PatchedHitTargets.Count);
    }

    public bool Equals(StyleOnlyPatchPlanDiagnosticSnapshot other)
    {
        return Case == other.Case
            && Eligible == other.Eligible
            && FallbackReason == other.FallbackReason
            && EqualityComparer<IReadOnlyList<(int Start, int Count)>>.Default.Equals(DirtyElementRanges, other.DirtyElementRanges)
            && EqualityComparer<IReadOnlyList<(int Start, int Count)>>.Default.Equals(DirtyCommandRanges, other.DirtyCommandRanges)
            && HitTargetCount == other.HitTargetCount;
    }

    public override bool Equals(object? obj) => obj is StyleOnlyPatchPlanDiagnosticSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Case, Eligible, FallbackReason, DirtyElementRanges, DirtyCommandRanges, HitTargetCount);

    public static bool operator ==(StyleOnlyPatchPlanDiagnosticSnapshot left, StyleOnlyPatchPlanDiagnosticSnapshot right) => left.Equals(right);

    public static bool operator !=(StyleOnlyPatchPlanDiagnosticSnapshot left, StyleOnlyPatchPlanDiagnosticSnapshot right) => !left.Equals(right);
}

internal static class StyleOnlyPatchPlanBuilder
{
    public static StyleOnlyPatchPlan Build(
        LayoutDirtyClassificationList dirtyClassifications,
        bool viewportChanged,
        LayoutTreeResult? retainedLayout,
        ElementCommandRangeList retainedElementCommandRanges,
        IReadOnlyList<HitTestTarget> retainedHitTargets,
        ReadOnlySpan<LayoutElement> nextLayoutElements,
        IndexRangeList dirtyElementRanges)
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

        if (!RangeUtils.TryMapContiguousElementRangesToCommandRanges(retainedElementCommandRanges, dirtyElementRanges, out var dirtyCommandRanges))
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
