namespace Irix.Rendering;

internal static class StyleOnlyLayoutPatcher
{
    private const int InlineDirtyIndexCapacity = 32;
    private const int InlineRangeCapacity = 16;

    public static bool TryBuildPatchedLayout(
        LayoutTreeResult retainedLayout,
        VirtualNode nextRoot,
        IReadOnlyList<int> dirtyNodes,
        out LayoutTreeResult patchedLayout)
    {
        return TryBuildPatchedLayout(retainedLayout, new VirtualNodeTree(nextRoot), dirtyNodes, out patchedLayout);
    }

    public static bool TryBuildPatchedLayout(
        LayoutTreeResult retainedLayout,
        VirtualNodeTree nextTree,
        IReadOnlyList<int> dirtyNodes,
        out LayoutTreeResult patchedLayout)
    {
        patchedLayout = default;
        if (dirtyNodes.Count == 0 || retainedLayout.ElementRanges.Length == 0)
        {
            return false;
        }

        var nextReader = nextTree.CreateReader().Root;
        var elementCount = retainedLayout.Elements.Length;
        Span<LayoutElement> inlineElements = stackalloc LayoutElement[LayoutElementList.InlineCapacity];
        LayoutElement[]? ownedElements = elementCount > LayoutElementList.InlineCapacity
            ? new LayoutElement[elementCount]
            : null;
        var nextElements = ownedElements is null ? inlineElements[..elementCount] : ownedElements.AsSpan();
        retainedLayout.Elements.CopyTo(nextElements);
        var scratch = new RenderScratchBuffer();
        Span<int> dirtyIndexStorage = stackalloc int[InlineDirtyIndexCapacity];
        scoped var sortedDirty = scratch.CreateDirtyIndexList(dirtyIndexStorage);
        try
        {
            for (var i = 0; i < dirtyNodes.Count; i++)
            {
                sortedDirty.Add(dirtyNodes[i]);
            }

            sortedDirty.Sort();

            Span<(int Start, int Count)> rangeStorage = stackalloc (int Start, int Count)[InlineRangeCapacity];
            scoped var ranges = scratch.CreateRangeList(rangeStorage);
            try
            {
                var dfsIndex = 0;
                var treeCursor = 0;
                var dirtyCursor = 0;
                if (!TryPatchTree(
                    nextReader,
                    retainedLayout,
                    nextElements,
                    sortedDirty.Written,
                    ref dfsIndex,
                    ref dirtyCursor,
                    ref treeCursor,
                    ref ranges)
                    || !DirtyDfsIndexCursor.IsComplete(sortedDirty.Written, dirtyCursor)
                    || treeCursor != retainedLayout.TreeNodes.Length
                    || dfsIndex != retainedLayout.ElementRanges.Length)
                {
                    return false;
                }

                var dirtyElementRanges = RangeUtils.Merge(ref ranges);
                if (dirtyElementRanges.Count == 0)
                {
                    return false;
                }

                var patchedElements = ownedElements is null
                    ? LayoutElementList.CopyFrom(nextElements)
                    : LayoutElementList.FromOwnedArray(ownedElements);
                patchedLayout = retainedLayout.WithElementsAndDirtyRanges(patchedElements, dirtyElementRanges);
                return true;
            }
            finally
            {
                ranges.Dispose();
            }
        }
        finally
        {
            sortedDirty.Dispose();
        }
    }

    private static bool TryPatchTree(
        VirtualNodeReader nextNode,
        LayoutTreeResult retainedLayout,
        Span<LayoutElement> elements,
        ReadOnlySpan<int> sortedDirty,
        ref int dfsIndex,
        ref int dirtyCursor,
        ref int treeCursor,
        scoped ref ScratchList<(int Start, int Count)> ranges)
    {
        var currentIndex = dfsIndex++;
        if (!TryReadTreeNode(retainedLayout.TreeNodes, ref treeCursor, currentIndex, out var retainedTreeNode)
            || !TryRefreshTextContent(elements, retainedTreeNode, nextNode)
            || !DirtyDfsIndexCursor.TryRead(sortedDirty, ref dirtyCursor, currentIndex, out var isDirty))
        {
            return false;
        }

        if (isDirty)
        {
            if ((uint)currentIndex >= (uint)retainedLayout.ElementRanges.Length)
            {
                return false;
            }

            var range = retainedLayout.ElementRanges[currentIndex];
            if (!TryPatchElementRange(elements, nextNode, range))
            {
                return false;
            }

            ranges.Add((range.ElementStart, range.ElementCount));
        }

        var childOffset = 0;
        for (var i = 0; i < nextNode.ChildCount; i++)
        {
            var childDfsIndex = nextNode.DfsIndex + 1 + childOffset;
            var child = nextNode.GetChild(i, childDfsIndex);
            if (!TryPatchTree(child, retainedLayout, elements, sortedDirty, ref dfsIndex, ref dirtyCursor, ref treeCursor, ref ranges))
            {
                return false;
            }

            childOffset += child.CountSubtreeNodes();
        }

        return true;
    }

    private static bool TryReadTreeNode(
        LayoutTreeNodeList treeNodes,
        ref int treeCursor,
        int dfsIndex,
        out LayoutTreeNode treeNode)
    {
        treeNode = default;
        if (treeCursor >= treeNodes.Length)
        {
            return true;
        }

        var candidate = treeNodes[treeCursor];
        if (candidate.DfsIndex < dfsIndex)
        {
            return false;
        }

        if (candidate.DfsIndex != dfsIndex)
        {
            return true;
        }

        treeNode = candidate;
        treeCursor++;
        return true;
    }

    private static bool TryRefreshTextContent(
        Span<LayoutElement> elements,
        LayoutTreeNode retainedTreeNode,
        VirtualNodeReader nextNode)
    {
        if (retainedTreeNode.Kind == VirtualNodeKind.None)
        {
            return true;
        }

        if (retainedTreeNode.Kind != nextNode.Kind)
        {
            return false;
        }

        if (retainedTreeNode.Kind != VirtualNodeKind.Content && retainedTreeNode.Kind != VirtualNodeKind.Container)
        {
            return true;
        }

        if (retainedTreeNode.ElementCount != 1)
        {
            return true;
        }

        if ((uint)retainedTreeNode.ElementStart >= (uint)elements.Length)
        {
            return false;
        }

        var elementIndex = retainedTreeNode.ElementStart;
        var retainedElement = elements[elementIndex];
        if (retainedElement.Kind == LayoutElementKind.Text)
        {
            if (retainedTreeNode.Kind != VirtualNodeKind.Content)
            {
                return true;
            }

            if (!LayoutNodeReader.IsTextContent(nextNode) || !nextNode.Content.TryGetText(out var content))
            {
                return false;
            }

            elements[elementIndex] = WithText(retainedElement, content);
        }
        else if (retainedElement.Kind == LayoutElementKind.Button)
        {
            if (retainedTreeNode.Kind != VirtualNodeKind.Container || !LayoutNodeReader.IsInteractiveContainer(nextNode))
            {
                return true;
            }

            if (nextNode.Kind != VirtualNodeKind.Container)
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

        return true;
    }

    private static bool TryPatchElementRange(
        Span<LayoutElement> elements,
        VirtualNodeReader nextNode,
        LayoutElementRange range)
    {
        if (range.ElementCount != 1
            || (uint)range.ElementStart >= (uint)elements.Length
            || range.ElementStart + range.ElementCount > elements.Length)
        {
            return false;
        }

        var elementIndex = range.ElementStart;
        var retainedElement = elements[elementIndex];
        return retainedElement.Kind switch
        {
            LayoutElementKind.Text => TryPatchTextContent(elements, elementIndex, retainedElement, nextNode),
            LayoutElementKind.Rectangle => TryPatchRectangleContent(elements, elementIndex, retainedElement, nextNode),
            LayoutElementKind.Button => TryPatchInteractiveContainer(elements, elementIndex, retainedElement, nextNode),
            _ => false,
        };
    }

    private static bool TryPatchTextContent(
        Span<LayoutElement> elements,
        int elementIndex,
        LayoutElement retainedElement,
        VirtualNodeReader nextNode)
    {
        if (!LayoutNodeReader.IsTextContent(nextNode) || !nextNode.Content.TryGetText(out var content))
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

    private static LayoutElement WithText(LayoutElement retainedElement, TextContentResource text)
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

    private static bool TryPatchRectangleContent(
        Span<LayoutElement> elements,
        int elementIndex,
        LayoutElement retainedElement,
        VirtualNodeReader nextNode)
    {
        if (!LayoutNodeReader.IsRectangleContent(nextNode))
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

    private static bool TryPatchInteractiveContainer(
        Span<LayoutElement> elements,
        int elementIndex,
        LayoutElement retainedElement,
        VirtualNodeReader nextNode)
    {
        if (nextNode.Kind != VirtualNodeKind.Container)
        {
            return false;
        }

        var label = LayoutNodeReader.GetButtonLabel(nextNode);
        if (label.IsNone)
        {
            return false;
        }

        var properties = new PropertyReader(nextNode.Properties);
        _ = LayoutNodeReader.TryGetFirstRectangleContent(nextNode, out var rectangleContent);
        _ = LayoutNodeReader.TryGetFirstTextContent(nextNode, out var textContent);
        var rectangleProperties = new PropertyReader(rectangleContent.Properties);
        var textProperties = new PropertyReader(textContent.Properties);
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
            Background: LayoutNodeReader.ReadPaint(rectangleProperties, VirtualPropertyKey.Background),
            Border: LayoutNodeReader.ReadBorderStroke(rectangleProperties, VirtualPropertyKey.Border),
            ForegroundColor: LayoutNodeReader.ReadColor(textProperties, VirtualPropertyKey.ForegroundColor));
        return true;
    }
}
