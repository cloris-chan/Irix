namespace Irix.Rendering;

internal static class DirtyDfsIndexCursor
{
    public static bool IsComplete(ReadOnlySpan<int> sortedIndices, int cursor) => cursor == sortedIndices.Length;

    public static bool TryRead(ReadOnlySpan<int> sortedIndices, ref int cursor, int dfsIndex, out bool isDirty)
    {
        isDirty = false;
        if (cursor >= sortedIndices.Length)
        {
            return true;
        }

        var dirtyIndex = sortedIndices[cursor];
        if (dirtyIndex < dfsIndex)
        {
            return false;
        }

        if (dirtyIndex != dfsIndex)
        {
            return true;
        }

        isDirty = true;
        do
        {
            cursor++;
        }
        while (cursor < sortedIndices.Length && sortedIndices[cursor] == dfsIndex);

        return true;
    }
}
