using Irix.Platform;

namespace Irix.Rendering;

internal static class StyleOnlyPatchEligibility
{
    public static bool IsLayoutReuseEligible(
        LayoutDirtyClassificationList dirtyClassifications,
        PixelRectangle retainedViewport,
        PixelRectangle nextViewport)
    {
        return IsLayoutReuseEligible(dirtyClassifications, retainedViewport != nextViewport);
    }

    public static bool IsLayoutReuseEligible(
        LayoutDirtyClassificationList dirtyClassifications,
        bool viewportChanged)
    {
        if (viewportChanged || dirtyClassifications.Count == 0)
        {
            return false;
        }

        foreach (var classification in dirtyClassifications)
        {
            if (classification.Reason != LayoutRebuildReason.StyleOnly)
            {
                return false;
            }
        }

        return true;
    }

}
