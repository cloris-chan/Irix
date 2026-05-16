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
    /// Implicit visible height for a container without a usable explicit Height.
    /// Root containers fill the viewport; nested containers keep a small
    /// measurable fallback when they start below the viewport.
    /// </summary>
    public int ResolveImplicitVisibleHeight(int contentTop)
    {
        if (Depth == 0)
        {
            return Math.Max(ViewportHeight, 0);
        }

        var remaining = Math.Max(ViewportHeight - contentTop, 0);
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
                var contentTop = cursorY;
                var isRootContainer = ctx.Depth == 0;
                var containerClipLeft = isRootContainer ? 0 : ctx.Style.HorizontalPadding;
                var containerClipTop = isRootContainer ? 0 : contentTop;
                var containerClipWidth = isRootContainer ? ctx.AvailableWidth + (ctx.Style.HorizontalPadding * 2) : ctx.AvailableWidth;

                var implicitVisibleHeight = ctx.ResolveImplicitVisibleHeight(contentTop);
                var explicitHeight = GetDimension(node, VirtualPropertyKey.Height, 0);
                var containerVisibleHeight = explicitHeight > 0
                    ? Math.Min(explicitHeight, implicitVisibleHeight)
                    : implicitVisibleHeight;

                var containerClip = new PixelRectangle(
                    containerClipLeft,
                    containerClipTop,
                    containerClipWidth,
                    containerVisibleHeight);
                if (ctx.ClipBounds.Width > 0 && ctx.ClipBounds.Height > 0)
                {
                    containerClip = IntersectRect(containerClip, ctx.ClipBounds);
                }

                var childCtx = ctx;
                childCtx.Depth = ctx.Depth + 1;
                childCtx.ClipBounds = containerClip;
                cursorY = contentTop;
                foreach (var child in node.Children)
                {
                    children.AddRange(LayoutNode(child, childDfsIndex, ref cursorY, elements, ref childCtx));
                    childDfsIndex += CountVirtualNodes(child);
                }
                var contentHeight = Math.Max(cursorY - contentTop, 0);

                var maxScrollY = Math.Max(contentHeight - containerVisibleHeight, 0);
                var scrollY = Math.Clamp(GetDimension(node, VirtualPropertyKey.ScrollY, 0), 0, maxScrollY);

                if (scrollY > 0)
                {
                    OffsetElementY(elements, children, -scrollY);
                }

                var visibleCount = 0;
                var clippedCount = 0;
                foreach (var child in children)
                {
                    for (var i = child.ElementStart; i < child.ElementStart + child.ElementCount; i++)
                    {
                        var el = elements[i];
                        if (el.Bounds.Y + el.Bounds.Height <= containerClip.Y
                            || el.Bounds.Y >= containerClip.Y + containerClip.Height)
                        {
                            clippedCount++;
                        }
                        else
                        {
                            visibleCount++;
                        }
                    }
                }

                ctx.ScrollDiags.Add(new ScrollContainerDiag(
                    dfsIndex,
                    containerVisibleHeight,
                    contentHeight,
                    scrollY,
                    maxScrollY,
                    visibleCount,
                    clippedCount));

                cursorY = contentTop + containerVisibleHeight + ctx.Style.ItemSpacing;

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
                if (!content.IsNone)
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
                    GetDimension(node, VirtualPropertyKey.Width, Math.Min(ctx.AvailableWidth, 160)),
                    GetDimension(node, VirtualPropertyKey.Height, ctx.Style.RectangleHeight));
                var elementIndex = elements.Count;
                elements.Add(new LayoutElement(LayoutElementKind.Rectangle, rectangleBounds, ClipBounds: ctx.ClipBounds));
                cursorY += rectangleBounds.Height + ctx.Style.ItemSpacing;
                return [new LayoutTreeNode(dfsIndex, VirtualNodeKind.Rectangle, elementIndex, 1, [])];
            }

            case VirtualNodeKind.Button:
            {
                var label = GetButtonLabel(node);
                var labelLength = label.IsNone ? 6 : label.Range.Length; // 6 = "Button".Length
                var width = Math.Min(ctx.AvailableWidth, Math.Max(
                    ctx.Style.MinimumButtonWidth,
                    labelLength * ctx.Style.ButtonTextWidthFactor + ctx.Style.ButtonHorizontalPadding));
                var bounds = new PixelRectangle(
                    ctx.Style.HorizontalPadding,
                    cursorY,
                    GetDimension(node, VirtualPropertyKey.Width, width),
                    GetDimension(node, VirtualPropertyKey.Height, ctx.Style.ButtonHeight));
                var actionId = GetActionId(node);
                var buttonState = new ButtonVisualState(
                    IsHovered: GetBooleanProperty(node, VirtualPropertyKey.IsHovered),
                    IsPressed: GetBooleanProperty(node, VirtualPropertyKey.IsPressed),
                    IsFocused: GetBooleanProperty(node, VirtualPropertyKey.IsFocused));
                var elementIndex = elements.Count;
                elements.Add(new LayoutElement(
                    LayoutElementKind.Button,
                    bounds,
                    ClipBounds: ctx.ClipBounds,
                    Text: label,
                    ActionId: actionId,
                    ButtonState: buttonState));
                cursorY += bounds.Height + ctx.Style.ItemSpacing;
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

    private static int GetDimension(VirtualNode node, VirtualPropertyKey key, int defaultValue)
    {
        foreach (var property in node.Properties)
        {
            if (property.Key == key && property.Value.Kind == PropertyValueKind.Number)
            {
                return (int)property.Value.Number;
            }
        }

        return defaultValue;
    }

    private static TextNodeContent GetButtonLabel(VirtualNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == VirtualNodeKind.Text)
            {
                var content = GetTextContent(child);
                if (!content.IsNone)
                {
                    return content;
                }
            }
        }

        return default;
    }

    private static ActionId GetActionId(VirtualNode node)
    {
        foreach (var property in node.Properties)
        {
            if (property.Key == VirtualPropertyKey.ActionId && property.Value.Kind == PropertyValueKind.ActionId)
            {
                return property.Value.ActionIdValue;
            }
        }

        return ActionId.None;
    }

    private static bool GetBooleanProperty(VirtualNode node, VirtualPropertyKey key)
    {
        foreach (var property in node.Properties)
        {
            if (property.Key == key && property.Value.Kind == PropertyValueKind.Boolean)
            {
                return property.Value.Boolean;
            }
        }

        return false;
    }

    private static TextNodeContent GetTextContent(VirtualNode node)
    {
        return node.Content.TryGetText(out var textContent) ? textContent : default;
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
