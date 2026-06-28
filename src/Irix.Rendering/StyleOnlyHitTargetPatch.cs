namespace Irix.Rendering;

internal static class StyleOnlyHitTargetPatch
{
    public static bool TryBuildPatchedHitTargets(
        HitTargetList retainedHitTargets,
        ReadOnlySpan<LayoutElement> nextLayoutElements,
        IndexRangeList dirtyElementRanges,
        out HitTargetList patchedHitTargets)
    {
        return TryBuildPatchedHitTargets(
            retainedHitTargets,
            LayoutElementList.CopyFrom(nextLayoutElements),
            dirtyElementRanges,
            out patchedHitTargets);
    }

    public static bool TryBuildPatchedHitTargets(
        HitTargetList retainedHitTargets,
        LayoutElementList nextLayoutElements,
        IndexRangeList dirtyElementRanges,
        out HitTargetList patchedHitTargets)
    {
        patchedHitTargets = HitTargetList.Empty;
        if (dirtyElementRanges.Count == 0)
        {
            return false;
        }

        var dirtyRanges = RangeUtils.Merge(dirtyElementRanges);
        Span<HitTestTarget> inlinePatched = stackalloc HitTestTarget[HitTargetList.InlineCapacity];
        var ownedPatched = retainedHitTargets.Count > HitTargetList.InlineCapacity ? new HitTestTarget[retainedHitTargets.Count] : null;
        var patched = ownedPatched is null ? inlinePatched[..retainedHitTargets.Count] : ownedPatched.AsSpan();
        for (var i = 0; i < retainedHitTargets.Count; i++)
        {
            patched[i] = retainedHitTargets[i];
        }

        var hitTargetIndex = 0;

        for (var elementIndex = 0; elementIndex < nextLayoutElements.Length; elementIndex++)
        {
            var element = nextLayoutElements[elementIndex];
            if (element.ActionId.IsNone)
            {
                continue;
            }

            if (hitTargetIndex >= retainedHitTargets.Count)
            {
                return false;
            }

            var retainedHitTarget = retainedHitTargets[hitTargetIndex];
            if (retainedHitTarget.Bounds != element.Bounds || retainedHitTarget.ClipBounds != element.ClipBounds)
            {
                return false;
            }

            if (RangeUtils.Contains(dirtyRanges, elementIndex))
            {
                patched[hitTargetIndex] = new HitTestTarget(
                    retainedHitTarget.Bounds,
                    element.ActionId,
                    retainedHitTarget.ClipBounds,
                    retainedHitTarget.CommandStart,
                    retainedHitTarget.CommandCount);
            }

            hitTargetIndex++;
        }

        if (hitTargetIndex != retainedHitTargets.Count)
        {
            return false;
        }

        patchedHitTargets = ownedPatched is null
            ? HitTargetList.CopyFrom(patched)
            : HitTargetList.FromOwnedArray(ownedPatched);
        return true;
    }
}
