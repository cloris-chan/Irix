using Irix.Platform;

namespace Irix.Rendering;

internal static class StyleOnlyPatchEligibility
{
    public static bool IsLayoutReuseEligible(
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications,
        PixelRectangle retainedViewport,
        PixelRectangle nextViewport)
    {
        return IsLayoutReuseEligible(dirtyClassifications, retainedViewport != nextViewport);
    }

    public static bool IsLayoutReuseEligible(
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications,
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

    public static bool TryMapStableCommandRanges(
        ElementCommandRange[] elementCommandRanges,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges,
        out IReadOnlyList<(int Start, int Count)> dirtyCommandRanges)
    {
        dirtyCommandRanges = [];
        if (dirtyElementRanges.Count == 0)
        {
            return false;
        }

        var commandRanges = new List<(int Start, int Count)>(dirtyElementRanges.Count);
        foreach (var (elementStart, elementCount) in dirtyElementRanges)
        {
            if (elementStart < 0 || elementCount <= 0 || elementStart > elementCommandRanges.Length - elementCount)
            {
                return false;
            }

            var commandStart = elementCommandRanges[elementStart].CommandStart;
            if (commandStart < 0)
            {
                return false;
            }

            var commandEnd = commandStart;
            var elementEnd = elementStart + elementCount;
            for (var elementIndex = elementStart; elementIndex < elementEnd; elementIndex++)
            {
                var elementCommandRange = elementCommandRanges[elementIndex];
                if (elementCommandRange.CommandStart != commandEnd || elementCommandRange.CommandCount <= 0)
                {
                    dirtyCommandRanges = [];
                    return false;
                }

                commandEnd += elementCommandRange.CommandCount;
            }

            commandRanges.Add((commandStart, commandEnd - commandStart));
        }

        dirtyCommandRanges = RangeUtils.Merge(commandRanges);
        return dirtyCommandRanges.Count > 0;
    }
}