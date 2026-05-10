using Irix.Platform;

namespace Irix.Rendering;

/// <summary>
/// Carries layout state through recursive LayoutNode calls.
/// Prevents parameter list bloat as more layout features are added.
/// </summary>
internal struct LayoutContext
{
    public int AvailableWidth;
    public int ViewportHeight;
    public int Depth; // 0 = root container, 1+ = nested
    public PixelRectangle ClipBounds;
    public LayoutStyle Style;
    public List<ScrollContainerDiag> ScrollDiags;

    /// <summary>
    /// Default height for nested containers that have no explicit Height attribute.
    /// Uses the remaining viewport height clamped to a reasonable minimum.
    /// </summary>
    public int DefaultContainerHeight(int containerTop)
    {
        if (Depth == 0)
        {
            // Root: fill remaining viewport
            return Math.Max(ViewportHeight - containerTop, 0);
        }

        // Nested: use remaining viewport or a sensible minimum
        var remaining = Math.Max(ViewportHeight - containerTop, 0);
        return remaining > 0 ? remaining : Style.TextHeight * 3;
    }
}

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
        var cursorY = style.VerticalPadding;

        var ctx = new LayoutContext
        {
            AvailableWidth = Math.Max(viewportBounds.Width - (style.HorizontalPadding * 2), 0),
            ViewportHeight = viewportBounds.Height,
            Depth = 0,
            ClipBounds = default,
            Style = style,
            ScrollDiags = scrollDiags,
        };

        var treeNodes = LayoutNode(root, 0, ref cursorY, elements, ref ctx);

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
        ref int cursorY,
        List<LayoutElement> elements,
        ref LayoutContext ctx)
    {
        switch (node.Kind)
        {
            case VirtualNodeKind.ScrollContainer:
            {
                var children = new List<LayoutTreeNode>();
                var childDfsIndex = dfsIndex + 1;
                var containerTop = cursorY;

                // Container visible height: explicit Height, or default based on depth
                var explicitHeight = GetDimension(node, "Height", 0);
                var containerVisibleHeight = explicitHeight > 0
                    ? Math.Min(explicitHeight, ctx.DefaultContainerHeight(containerTop))
                    : ctx.DefaultContainerHeight(containerTop);

                // Clip bounds: the visible area for children within this container
                var containerClip = new PixelRectangle(
                    ctx.Style.HorizontalPadding,
                    containerTop,
                    ctx.AvailableWidth,
                    containerVisibleHeight);
                // Intersect with parent clip to prevent children from overflowing
                if (ctx.ClipBounds.Width > 0 && ctx.ClipBounds.Height > 0)
                {
                    containerClip = IntersectRect(containerClip, ctx.ClipBounds);
                }

                // Lay out children to measure content height
                var childCtx = ctx;
                childCtx.Depth = ctx.Depth + 1;
                childCtx.ClipBounds = containerClip;
                cursorY = containerTop;
                foreach (var child in node.Children)
                {
                    children.AddRange(LayoutNode(child, childDfsIndex, ref cursorY, elements, ref childCtx));
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

                // Count visible vs clipped elements for diagnostics
                var visibleCount = 0;
                var clippedCount = 0;
                foreach (var child in children)
                {
                    for (var i = child.ElementStart; i < child.ElementStart + child.ElementCount; i++)
                    {
                        var el = elements[i];
                        if (el.Bounds.Y + el.Bounds.Height <= containerTop
                            || el.Bounds.Y >= containerTop + containerVisibleHeight)
                        {
                            clippedCount++;
                        }
                        else
                        {
                            visibleCount++;
                        }
                    }
                }

                // Collect scroll diagnostics
                ctx.ScrollDiags.Add(new ScrollContainerDiag(
                    dfsIndex,
                    containerVisibleHeight,
                    contentHeight,
                    scrollY,
                    maxScrollY,
                    visibleCount,
                    clippedCount));

                // Advance cursor past the container
                cursorY = containerTop + containerVisibleHeight + ctx.Style.ItemSpacing;

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
                        new PixelRectangle(ctx.Style.HorizontalPadding, cursorY, ctx.AvailableWidth, ctx.Style.TextHeight),
                        ClipBounds: ctx.ClipBounds,
                        Text: content));
                    cursorY += ctx.Style.TextHeight + ctx.Style.ItemSpacing;
                    return [new LayoutTreeNode(dfsIndex, VirtualNodeKind.Text, elementIndex, 1, [])];
                }

                return [];
            }

            case VirtualNodeKind.Rectangle:
            {
                var rectangleBounds = new PixelRectangle(
                    ctx.Style.HorizontalPadding,
                    cursorY,
                    GetDimension(node, "Width", Math.Min(ctx.AvailableWidth, 160)),
                    GetDimension(node, "Height", ctx.Style.RectangleHeight));
                var elementIndex = elements.Count;
                elements.Add(new LayoutElement(LayoutElementKind.Rectangle, rectangleBounds, ClipBounds: ctx.ClipBounds));
                cursorY += rectangleBounds.Height + ctx.Style.ItemSpacing;
                return [new LayoutTreeNode(dfsIndex, VirtualNodeKind.Rectangle, elementIndex, 1, [])];
            }

            case VirtualNodeKind.Button:
            {
                var label = GetButtonLabel(node);
                var width = Math.Min(ctx.AvailableWidth, Math.Max(
                    ctx.Style.MinimumButtonWidth,
                    label.Length * ctx.Style.ButtonTextWidthFactor + ctx.Style.ButtonHorizontalPadding));
                var bounds = new PixelRectangle(ctx.Style.HorizontalPadding, cursorY, width, ctx.Style.ButtonHeight);
                var actionId = GetTextAttribute(node, "ActionId");
                var buttonState = new ButtonVisualState(
                    IsHovered: GetBooleanAttribute(node, "IsHovered"),
                    IsPressed: GetBooleanAttribute(node, "IsPressed"),
                    IsFocused: GetBooleanAttribute(node, "IsFocused"));
                var elementIndex = elements.Count;
                elements.Add(new LayoutElement(
                    LayoutElementKind.Button,
                    bounds,
                    ClipBounds: ctx.ClipBounds,
                    Text: label,
                    ActionId: actionId,
                    ButtonState: buttonState));
                cursorY += ctx.Style.ButtonHeight + ctx.Style.ItemSpacing;
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

    private static bool GetBooleanAttribute(VirtualNode node, string attributeName)
    {
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Name == attributeName && attribute.Value.Kind == AttributeValueKind.Boolean)
            {
                return attribute.Value.Boolean;
            }
        }

        return false;
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
