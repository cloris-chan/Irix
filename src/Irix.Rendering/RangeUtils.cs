namespace Irix.Rendering;

/// <summary>
/// Shared utilities for working with (Start, Count) range tuples.
/// Used by both layout element ranges and draw command ranges.
/// </summary>
internal static class RangeUtils
{
    private const int InlineRangeCapacity = 16;

    /// <summary>
    /// Sort and merge overlapping/adjacent ranges into the minimal non-overlapping set.
    /// Input ranges are (startIndex, count). Adjacent ranges (where current.Start == lastEnd) are merged.
    /// </summary>
    public static IReadOnlyList<(int Start, int Count)> Merge(IReadOnlyList<(int Start, int Count)> ranges)
    {
        if (ranges.Count == 0)
        {
            return [];
        }

        var scratch = new RenderScratchBuffer();
        Span<(int Start, int Count)> storage = stackalloc (int Start, int Count)[InlineRangeCapacity];
        var sorted = scratch.CreateRangeList(storage);
        try
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                sorted.Add(ranges[i]);
            }

            return MergeScratch(ref sorted, rejectOverlap: false, out _);
        }
        finally
        {
            sorted.Dispose();
        }
    }

    private static IReadOnlyList<(int Start, int Count)> MergeScratch(
        ref ScratchList<(int Start, int Count)> ranges,
        bool rejectOverlap,
        out bool valid)
    {
        valid = true;
        if (ranges.Count == 0)
        {
            return [];
        }

        if (ranges.Count == 1)
        {
            return ranges.ToArray();
        }

        ranges.Sort(RangeStartComparer.Instance);
        var span = ranges.WrittenMutable;
        var write = 1;
        for (var read = 1; read < span.Length; read++)
        {
            var last = span[write - 1];
            var current = span[read];
            var lastEnd = last.Start + last.Count;

            if (rejectOverlap && current.Start < lastEnd)
            {
                valid = false;
                return [];
            }

            if (current.Start <= lastEnd)
            {
                var newEnd = Math.Max(lastEnd, current.Start + current.Count);
                span[write - 1] = (last.Start, newEnd - last.Start);
            }
            else
            {
                span[write++] = current;
            }
        }

        var result = new (int Start, int Count)[write];
        span[..write].CopyTo(result);
        return result;
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

        var scratch = new RenderScratchBuffer();
        Span<(int Start, int Count)> storage = stackalloc (int Start, int Count)[InlineRangeCapacity];
        var sorted = scratch.CreateRangeList(storage);
        try
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                var (start, count) = ranges[i];
                if (start < 0 || count <= 0 || start > maxCount - count)
                {
                    return false;
                }

                sorted.Add((start, count));
            }

            normalized = MergeScratch(ref sorted, rejectOverlap: true, out var valid);
            if (!valid)
            {
                normalized = [];
                return false;
            }

            return normalized.Count > 0;
        }
        finally
        {
            sorted.Dispose();
        }
    }

    /// <summary>
    /// Map element ranges to command ranges using an element→command mapping, then merge.
    /// </summary>
    public static IReadOnlyList<(int Start, int Count)> MapAndMerge(
        ElementCommandRange[] elementRanges,
        IReadOnlyList<(int Start, int Count)> elementDirtyRanges)
    {
        var scratch = new RenderScratchBuffer();
        Span<(int Start, int Count)> storage = stackalloc (int Start, int Count)[InlineRangeCapacity];
        var ranges = scratch.CreateRangeList(storage);
        try
        {
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

            return MergeScratch(ref ranges, rejectOverlap: false, out _);
        }
        finally
        {
            ranges.Dispose();
        }
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

    private sealed class RangeStartComparer : IComparer<(int Start, int Count)>
    {
        public static readonly RangeStartComparer Instance = new();

        public int Compare((int Start, int Count) x, (int Start, int Count) y) => x.Start.CompareTo(y.Start);
    }
}
