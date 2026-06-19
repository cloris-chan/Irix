namespace Irix.Rendering;

internal static class StyleOnlyLayoutPatcher
{
    private const int InlineDirtyIndexCapacity = 32;

    public static bool TryBuildPatchedLayout(
        LayoutTreeResult retainedLayout,
        VirtualNode nextRoot,
        IReadOnlyList<int> dirtyNodes,
        out LayoutTreeResult patchedLayout)
    {
        patchedLayout = null!;
        if (dirtyNodes.Count == 0 || retainedLayout.ElementRanges.Length == 0)
        {
            return false;
        }

        var nextElements = CopyElements(retainedLayout.Elements);
        if (!TryRefreshTextContent(nextElements, retainedLayout, nextRoot))
        {
            return false;
        }

        Span<int> inlineDirty = stackalloc int[InlineDirtyIndexCapacity];
        Span<int> sortedDirty = dirtyNodes.Count <= inlineDirty.Length
            ? inlineDirty[..dirtyNodes.Count]
            : new int[dirtyNodes.Count];
        for (var i = 0; i < dirtyNodes.Count; i++)
        {
            sortedDirty[i] = dirtyNodes[i];
        }

        sortedDirty.Sort();
        var ranges = new List<(int Start, int Count)>(dirtyNodes.Count);
        var previousDirty = -1;
        for (var i = 0; i < sortedDirty.Length; i++)
        {
            var dfsIndex = sortedDirty[i];
            if (dfsIndex == previousDirty)
            {
                continue;
            }

            previousDirty = dfsIndex;
            if ((uint)dfsIndex >= (uint)retainedLayout.ElementRanges.Length)
            {
                return false;
            }

            var range = retainedLayout.ElementRanges[dfsIndex];
            if (!TryPatchElementRange(nextElements, nextRoot, dfsIndex, range))
            {
                return false;
            }

            ranges.Add((range.ElementStart, range.ElementCount));
        }

        var dirtyElementRanges = MergeDirtyRanges(ranges);
        if (dirtyElementRanges.Count == 0)
        {
            return false;
        }

        patchedLayout = retainedLayout.WithElementsAndDirtyRanges(nextElements, dirtyElementRanges);
        return true;
    }

    private static LayoutElement[] CopyElements(IReadOnlyList<LayoutElement> elements)
    {
        var result = new LayoutElement[elements.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = elements[i];
        }

        return result;
    }

    private static bool TryRefreshTextContent(
        LayoutElement[] elements,
        LayoutTreeResult retainedLayout,
        VirtualNode nextRoot)
    {
        foreach (ref readonly var treeNode in retainedLayout.TreeNodes.AsSpan())
        {
            if (treeNode.ElementCount != 1
                || (treeNode.Kind != VirtualNodeKind.Text && treeNode.Kind != VirtualNodeKind.Button))
            {
                continue;
            }

            var elementIndex = treeNode.ElementStart;
            if ((uint)elementIndex >= (uint)elements.Length || !TryFindNode(nextRoot, treeNode.DfsIndex, out var nextNode))
            {
                return false;
            }

            var retainedElement = elements[elementIndex];
            if (retainedElement.Kind == LayoutElementKind.Text)
            {
                if (nextNode.Kind != VirtualNodeKind.Text || !nextNode.Content.TryGetText(out var content))
                {
                    return false;
                }

                elements[elementIndex] = WithText(retainedElement, content);
            }
            else if (retainedElement.Kind == LayoutElementKind.Button)
            {
                if (nextNode.Kind != VirtualNodeKind.Button)
                {
                    return false;
                }

                var label = GetButtonLabel(nextNode);
                if (label.IsNone)
                {
                    return false;
                }

                elements[elementIndex] = WithText(retainedElement, label);
            }
        }

        return true;
    }

    private static bool TryPatchElementRange(
        LayoutElement[] elements,
        VirtualNode nextRoot,
        int dfsIndex,
        LayoutElementRange range)
    {
        if (range.ElementCount != 1
            || (uint)range.ElementStart >= (uint)elements.Length
            || range.ElementStart + range.ElementCount > elements.Length
            || !TryFindNode(nextRoot, dfsIndex, out var nextNode))
        {
            return false;
        }

        var elementIndex = range.ElementStart;
        var retainedElement = elements[elementIndex];
        return retainedElement.Kind switch
        {
            LayoutElementKind.Text => TryPatchText(elements, elementIndex, retainedElement, nextNode),
            LayoutElementKind.Rectangle => TryPatchRectangle(elements, elementIndex, retainedElement, nextNode),
            LayoutElementKind.Button => TryPatchButton(elements, elementIndex, retainedElement, nextNode),
            _ => false,
        };
    }

    private static bool TryPatchText(
        LayoutElement[] elements,
        int elementIndex,
        LayoutElement retainedElement,
        VirtualNode nextNode)
    {
        if (nextNode.Kind != VirtualNodeKind.Text || !nextNode.Content.TryGetText(out var content))
        {
            return false;
        }

        var properties = new PropertyReader(nextNode.Properties);
        elements[elementIndex] = new LayoutElement(
            LayoutElementKind.Text,
            retainedElement.Bounds,
            retainedElement.ClipBounds,
            Text: content,
            ForegroundColor: ReadColor(properties, VirtualPropertyKey.ForegroundColor));
        return true;
    }

    private static LayoutElement WithText(LayoutElement retainedElement, TextNodeContent text)
    {
        return new LayoutElement(
            retainedElement.Kind,
            retainedElement.Bounds,
            retainedElement.ClipBounds,
            Text: text,
            ActionId: retainedElement.ActionId,
            ButtonState: retainedElement.ButtonState,
            Background: retainedElement.Background,
            Border: retainedElement.Border,
            ForegroundColor: retainedElement.ForegroundColor);
    }

    private static bool TryPatchRectangle(
        LayoutElement[] elements,
        int elementIndex,
        LayoutElement retainedElement,
        VirtualNode nextNode)
    {
        if (nextNode.Kind != VirtualNodeKind.Rectangle)
        {
            return false;
        }

        var properties = new PropertyReader(nextNode.Properties);
        elements[elementIndex] = new LayoutElement(
            LayoutElementKind.Rectangle,
            retainedElement.Bounds,
            retainedElement.ClipBounds,
            Background: ReadPaint(properties, VirtualPropertyKey.Background),
            Border: ReadBorderStroke(properties, VirtualPropertyKey.Border));
        return true;
    }

    private static bool TryPatchButton(
        LayoutElement[] elements,
        int elementIndex,
        LayoutElement retainedElement,
        VirtualNode nextNode)
    {
        if (nextNode.Kind != VirtualNodeKind.Button)
        {
            return false;
        }

        var label = GetButtonLabel(nextNode);
        if (label.IsNone)
        {
            return false;
        }

        var properties = new PropertyReader(nextNode.Properties);
        elements[elementIndex] = new LayoutElement(
            LayoutElementKind.Button,
            retainedElement.Bounds,
            retainedElement.ClipBounds,
            Text: label,
            ActionId: properties.GetActionId(VirtualPropertyKey.ActionId),
            ButtonState: new ButtonVisualState(
                IsHovered: properties.GetBool(VirtualPropertyKey.IsHovered),
                IsPressed: properties.GetBool(VirtualPropertyKey.IsPressed),
                IsFocused: properties.GetBool(VirtualPropertyKey.IsFocused)),
            Background: ReadPaint(properties, VirtualPropertyKey.Background),
            Border: ReadBorderStroke(properties, VirtualPropertyKey.Border),
            ForegroundColor: ReadColor(properties, VirtualPropertyKey.ForegroundColor));
        return true;
    }

    private static IReadOnlyList<(int Start, int Count)> MergeDirtyRanges(List<(int Start, int Count)> ranges)
    {
        if (ranges.Count == 0)
        {
            return [];
        }

        ranges.Sort(RangeStartComparer.Instance);
        var write = 0;
        for (var read = 0; read < ranges.Count; read++)
        {
            var current = ranges[read];
            if (current.Count <= 0)
            {
                continue;
            }

            if (write == 0)
            {
                ranges[write++] = current;
                continue;
            }

            var last = ranges[write - 1];
            var lastEnd = last.Start + last.Count;
            if (current.Start <= lastEnd)
            {
                var currentEnd = current.Start + current.Count;
                ranges[write - 1] = (last.Start, Math.Max(lastEnd, currentEnd) - last.Start);
            }
            else
            {
                ranges[write++] = current;
            }
        }

        if (write == 0)
        {
            return [];
        }

        var merged = new (int Start, int Count)[write];
        for (var i = 0; i < write; i++)
        {
            merged[i] = ranges[i];
        }

        return merged;
    }

    private static bool TryFindNode(VirtualNode root, int targetIndex, out VirtualNode node)
    {
        var currentIndex = 0;
        return TryFindNodeRecursive(root, targetIndex, ref currentIndex, out node);
    }

    private static bool TryFindNodeRecursive(VirtualNode candidate, int targetIndex, ref int currentIndex, out VirtualNode node)
    {
        if (currentIndex == targetIndex)
        {
            node = candidate;
            return true;
        }

        currentIndex++;
        foreach (var child in candidate.Children)
        {
            if (TryFindNodeRecursive(child, targetIndex, ref currentIndex, out node))
            {
                return true;
            }
        }

        node = default;
        return false;
    }

    private static TextNodeContent GetButtonLabel(VirtualNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == VirtualNodeKind.Text && child.Content.TryGetText(out var content) && !content.IsNone)
            {
                return content;
            }
        }

        return default;
    }

    private static StyleColorSlot ReadColor(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetColor(key, out var color) ? StyleColorSlot.Some(color) : StyleColorSlot.None;

    private static PaintSlot ReadPaint(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetPaint(key, out var paint) ? PaintSlot.Some(paint) : PaintSlot.None;

    private static BorderStrokeSlot ReadBorderStroke(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetBorderStroke(key, out var border) ? BorderStrokeSlot.Some(border) : BorderStrokeSlot.None;

    private sealed class RangeStartComparer : IComparer<(int Start, int Count)>
    {
        public static readonly RangeStartComparer Instance = new();

        public int Compare((int Start, int Count) x, (int Start, int Count) y) => x.Start.CompareTo(y.Start);
    }
}
