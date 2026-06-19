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

        var dirtyElementRanges = RangeUtils.Merge(ranges);
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

                var label = LayoutNodeReader.GetButtonLabel(nextNode);
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
            ForegroundColor: LayoutNodeReader.ReadColor(properties, VirtualPropertyKey.ForegroundColor));
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
            Background: LayoutNodeReader.ReadPaint(properties, VirtualPropertyKey.Background),
            Border: LayoutNodeReader.ReadBorderStroke(properties, VirtualPropertyKey.Border));
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

        var label = LayoutNodeReader.GetButtonLabel(nextNode);
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
            Background: LayoutNodeReader.ReadPaint(properties, VirtualPropertyKey.Background),
            Border: LayoutNodeReader.ReadBorderStroke(properties, VirtualPropertyKey.Border),
            ForegroundColor: LayoutNodeReader.ReadColor(properties, VirtualPropertyKey.ForegroundColor));
        return true;
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

}
