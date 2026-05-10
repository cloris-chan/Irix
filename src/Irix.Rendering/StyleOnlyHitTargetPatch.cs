namespace Irix.Rendering;

internal static class StyleOnlyHitTargetPatch
{
    public static bool TryBuildPatchedHitTargets(
        IReadOnlyList<HitTestTarget> retainedHitTargets,
        IReadOnlyList<LayoutElement> nextLayoutElements,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges,
        out HitTestTarget[] patchedHitTargets)
    {
        patchedHitTargets = [];
        if (dirtyElementRanges.Count == 0)
        {
            return false;
        }

        var dirtyRanges = RangeUtils.Merge(dirtyElementRanges);
        var patched = retainedHitTargets.Count == 0 ? [] : retainedHitTargets.ToArray();
        var hitTargetIndex = 0;

        for (var elementIndex = 0; elementIndex < nextLayoutElements.Count; elementIndex++)
        {
            var element = nextLayoutElements[elementIndex];
            if (string.IsNullOrWhiteSpace(element.ActionId))
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
                patched[hitTargetIndex] = new HitTestTarget(retainedHitTarget.Bounds, element.ActionId, retainedHitTarget.ClipBounds);
            }

            hitTargetIndex++;
        }

        if (hitTargetIndex != retainedHitTargets.Count)
        {
            return false;
        }

        patchedHitTargets = patched;
        return true;
    }
}