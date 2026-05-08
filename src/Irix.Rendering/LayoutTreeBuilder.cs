using Irix.Platform;

namespace Irix.Rendering;

internal sealed class LayoutTreeBuilder(LayoutStyle style)
{
    public LayoutTreeBuilder()
        : this(LayoutStyle.Default)
    {
    }

    /// <summary>
    /// Build layout elements and a layout tree for the given VirtualNode tree.
    /// When <paramref name="dirtyNodes"/> is non-null, computes which layout element
    /// ranges correspond to the dirty VirtualNode DFS indices.
    /// </summary>
    public LayoutTreeResult BuildLayoutTree(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null)
    {
        var elements = new List<LayoutElement>();
        var scrollDiags = new List<ScrollContainerDiag>();
        var availableWidth = Math.Max(viewportBounds.Width - (style.HorizontalPadding * 2), 0);
        var cursorY = style.VerticalPadding;

        var treeNodes = LayoutNode(root, 0, availableWidth, viewportBounds.Height, ref cursorY, elements, style, scrollDiags);

        var dirtyRanges = dirtyNodes is { Count: > 0 }
            ? RangeUtils.Merge(CollectDirtyRanges(treeNodes, new HashSet<int>(dirtyNodes)))
            : [];

        return new LayoutTreeResult(elements, treeNodes, dirtyRanges, scrollDiags);
    }

    /// <summary>
    /// Backward-compatible overload returning flat elements only.
    /// </summary>
    public IReadOnlyList<LayoutElement> Build(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null)
    {
        return BuildLayoutTree(root, viewportBounds, dirtyNodes).Elements;
    }

    private LayoutTreeNode[] LayoutNode(
        VirtualNode node,
        int dfsIndex,
        int availableWidth,
        int viewportHeight,
        ref int cursorY,
        List<LayoutElement> elements,
        LayoutStyle style,
        List<ScrollContainerDiag> scrollDiags,
        PixelRectangle clipBounds = default)
    {
        switch (node.Kind)
        {
            case VirtualNodeKind.ScrollContainer:
            {
                var children = new List<LayoutTreeNode>();
                var childDfsIndex = dfsIndex + 1;
                var containerTop = cursorY;

                // Container visible height: explicit Height attribute, or full remaining viewport
                var explicitHeight = GetDimension(node, "Height", 0);
                var containerVisibleHeight = explicitHeight > 0
                    ? Math.Min(explicitHeight, Math.Max(viewportHeight - containerTop, 0))
                    : Math.Max(viewportHeight - containerTop, 0);

                // Clip bounds: the visible area for children within this container
                var containerClip = new PixelRectangle(
                    style.HorizontalPadding,
                    containerTop,
                    availableWidth,
                    containerVisibleHeight);
                // Intersect with parent clip to prevent children from overflowing
                if (clipBounds.Width > 0 && clipBounds.Height > 0)
                {
                    containerClip = IntersectRect(containerClip, clipBounds);
                }

                // Lay out children to measure content height (start at container top)
                cursorY = containerTop;
                foreach (var child in node.Children)
                {
                    children.AddRange(LayoutNode(child, childDfsIndex, availableWidth, viewportHeight, ref cursorY, elements, style, scrollDiags, containerClip));
                    childDfsIndex += CountVirtualNodes(child);
                }
                var contentHeight = Math.Max(cursorY - containerTop, 0);

                // Scroll offset: clamp to [0, MaxScrollY], then shift children
                var maxScrollY = Math.Max(contentHeight - containerVisibleHeight, 0);
                var scrollY = Math.Clamp(GetDimension(node, "ScrollY", 0), 0, maxScrollY);

                if (scrollY > 0)
                {
                    OffsetElementY(elements, children, -scrollY);
                }

                // Collect scroll diagnostics
                scrollDiags.Add(new ScrollContainerDiag(
                    dfsIndex,
                    containerVisibleHeight,
                    contentHeight,
                    scrollY,
                    maxScrollY));

                // Advance cursor past the container
                cursorY = containerTop + containerVisibleHeight + style.ItemSpacing;

                if (children.Count == 0)
                {
                    return [];
                }

                var elementStart = children[0].ElementStart;
                var lastChild = children[^1];
                var elementCount = (lastChild.ElementStart + lastChild.ElementCount) - elementStart;
                return [new LayoutTreeNode(dfsIndex, VirtualNodeKind.ScrollContainer, elementStart, elementCount, children.ToArray())];
            }

            case VirtualNodeKind.Text:
            {
                var content = GetTextContent(node);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var elementIndex = elements.Count;
                    elements.Add(new LayoutElement(
                        LayoutElementKind.Text,
                        new PixelRectangle(style.HorizontalPadding, cursorY, availableWidth, style.TextHeight),
                        ClipBounds: clipBounds,
                        Text: content));
                    cursorY += style.TextHeight + style.ItemSpacing;
                    return [new LayoutTreeNode(dfsIndex, VirtualNodeKind.Text, elementIndex, 1, [])];
                }

                return [];
            }

            case VirtualNodeKind.Rectangle:
            {
                var rectangleBounds = new PixelRectangle(
                    style.HorizontalPadding,
                    cursorY,
                    GetDimension(node, "Width", Math.Min(availableWidth, 160)),
                    GetDimension(node, "Height", style.RectangleHeight));
                var elementIndex = elements.Count;
                elements.Add(new LayoutElement(LayoutElementKind.Rectangle, rectangleBounds, ClipBounds: clipBounds));
                cursorY += rectangleBounds.Height + style.ItemSpacing;
                return [new LayoutTreeNode(dfsIndex, VirtualNodeKind.Rectangle, elementIndex, 1, [])];
            }

            case VirtualNodeKind.Button:
            {
                var label = GetButtonLabel(node);
                var width = Math.Min(availableWidth, Math.Max(
                    style.MinimumButtonWidth,
                    label.Length * style.ButtonTextWidthFactor + style.ButtonHorizontalPadding));
                var bounds = new PixelRectangle(style.HorizontalPadding, cursorY, width, style.ButtonHeight);
                var actionId = GetTextAttribute(node, "ActionId");
                var elementIndex = elements.Count;
                elements.Add(new LayoutElement(
                    LayoutElementKind.Button,
                    bounds,
                    ClipBounds: clipBounds,
                    Text: label,
                    ActionId: actionId));
                cursorY += style.ButtonHeight + style.ItemSpacing;
                return [new LayoutTreeNode(dfsIndex, VirtualNodeKind.Button, elementIndex, 1, [])];
            }

            default:
                return [];
        }
    }

    private static List<(int Start, int Count)> CollectDirtyRanges(
        LayoutTreeNode[] treeNodes,
        HashSet<int> dirtyIndices)
    {
        var ranges = new List<(int Start, int Count)>();
        CollectDirtyRangesRecursive(treeNodes, dirtyIndices, ranges);
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        return ranges;
    }

    private static void CollectDirtyRangesRecursive(
        LayoutTreeNode[] treeNodes,
        HashSet<int> dirtyIndices,
        List<(int Start, int Count)> ranges)
    {
        foreach (var node in treeNodes)
        {
            if (dirtyIndices.Contains(node.DfsIndex))
            {
                ranges.Add((node.ElementStart, node.ElementCount));
            }

            if (node.Children.Length > 0)
            {
                CollectDirtyRangesRecursive(node.Children, dirtyIndices, ranges);
            }
        }
    }

    private static int CountVirtualNodes(VirtualNode node)
    {
        var count = 1;
        foreach (var child in node.Children)
        {
            count += CountVirtualNodes(child);
        }
        return count;
    }

    private static int GetDimension(VirtualNode node, string attributeName, int defaultValue)
    {
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Name == attributeName && attribute.Value.Kind == AttributeValueKind.Number)
            {
                return (int)attribute.Value.Number;
            }
        }

        return defaultValue;
    }

    private static string GetButtonLabel(VirtualNode node)
    {
        foreach (var child in node.Children)
        {
            var content = GetTextContent(child);
            if (child.Kind == VirtualNodeKind.Text && !string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return "Button";
    }

    private static string? GetTextAttribute(VirtualNode node, string attributeName)
    {
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Name == attributeName && attribute.Value.Kind == AttributeValueKind.Text)
            {
                return attribute.Value.Text;
            }
        }

        return null;
    }

    private static string? GetTextContent(VirtualNode node)
    {
        return node.Content.Kind == NodeContentKind.Text ? node.Content.Text : null;
    }

    private static PixelRectangle IntersectRect(PixelRectangle a, PixelRectangle b)
    {
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var width = Math.Max(right - x, 0);
        var height = Math.Max(bottom - y, 0);
        return new PixelRectangle(x, y, width, height);
    }

    private static void OffsetElementY(List<LayoutElement> elements, List<LayoutTreeNode> nodes, int offsetY)
    {
        if (offsetY == 0)
        {
            return;
        }

        foreach (var node in nodes)
        {
            for (var i = node.ElementStart; i < node.ElementStart + node.ElementCount; i++)
            {
                var el = elements[i];
                elements[i] = el with { Bounds = new PixelRectangle(el.Bounds.X, el.Bounds.Y + offsetY, el.Bounds.Width, el.Bounds.Height) };
            }
        }
    }
}
