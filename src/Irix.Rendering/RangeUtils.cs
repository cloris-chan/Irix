namespace Irix.Rendering;

/// <summary>
/// Shared utilities for working with (Start, Count) range tuples.
/// Used by both layout element ranges and draw command ranges.
/// </summary>
internal static class RangeUtils
{
    /// <summary>
    /// Sort and merge overlapping/adjacent ranges into the minimal non-overlapping set.
    /// Input ranges are (startIndex, count). Adjacent ranges (where current.Start == lastEnd) are merged.
    /// </summary>
    public static IReadOnlyList<(int Start, int Count)> Merge(IReadOnlyList<(int Start, int Count)> ranges)
    {
        if (ranges.Count <= 1)
        {
            return ranges;
        }

        var sorted = new List<(int Start, int Count)>(ranges);
        sorted.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(int Start, int Count)> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var last = merged[^1];
            var current = sorted[i];
            var lastEnd = last.Start + last.Count;

            if (current.Start <= lastEnd)
            {
                var newEnd = Math.Max(lastEnd, current.Start + current.Count);
                merged[^1] = (last.Start, newEnd - last.Start);
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    public static bool TryNormalizeStrict(
        IReadOnlyList<(int Start, int Count)> ranges,
        int maxCount,
        out IReadOnlyList<(int Start, int Count)> normalized)
    {
        normalized = [];
        if (maxCount < 0 || ranges.Count == 0)
        {
            return false;
        }

        var sorted = new List<(int Start, int Count)>(ranges.Count);
        foreach (var (start, count) in ranges)
        {
            if (start < 0 || count <= 0 || start > maxCount - count)
            {
                return false;
            }

            sorted.Add((start, count));
        }

        sorted.Sort((left, right) => left.Start.CompareTo(right.Start));
        var merged = new List<(int Start, int Count)>(sorted.Count) { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var last = merged[^1];
            var current = sorted[i];
            var lastEnd = last.Start + last.Count;
            if (current.Start < lastEnd)
            {
                return false;
            }

            if (current.Start == lastEnd)
            {
                merged[^1] = (last.Start, last.Count + current.Count);
            }
            else
            {
                merged.Add(current);
            }
        }

        normalized = merged;
        return normalized.Count > 0;
    }

    /// <summary>
    /// Map element ranges to command ranges using an element→command mapping, then merge.
    /// </summary>
    public static IReadOnlyList<(int Start, int Count)> MapAndMerge(
        ElementCommandRange[] elementRanges,
        IReadOnlyList<(int Start, int Count)> elementDirtyRanges)
    {
        var ranges = new List<(int Start, int Count)>();
        foreach (var (elementStart, elementCount) in elementDirtyRanges)
        {
            var elementEnd = elementStart + elementCount;
            if (elementStart >= elementRanges.Length)
            {
                continue;
            }

            var clampedEnd = Math.Min(elementEnd, elementRanges.Length);
            var cmdStart = elementRanges[elementStart].CommandStart;
            var lastRange = elementRanges[clampedEnd - 1];
            var cmdEnd = lastRange.CommandStart + lastRange.CommandCount;
            ranges.Add((cmdStart, cmdEnd - cmdStart));
        }

        return Merge(ranges);
    }

    /// <summary>
    /// Check whether a given index falls within any of the sorted, merged ranges.
    /// </summary>
    public static bool Contains(IReadOnlyList<(int Start, int Count)> ranges, int index)
    {
        foreach (var (start, count) in ranges)
        {
            if (index >= start && index < start + count)
            {
                return true;
            }

            if (start > index)
            {
                break; // sorted, no point continuing
            }
        }

        return false;
    }
}
