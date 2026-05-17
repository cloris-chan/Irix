using Irix.Platform;

namespace Irix.Rendering;

/// <summary>
/// Carries layout state through recursive LayoutNode calls.
/// Prevents parameter list bloat as more layout features are added.
/// </summary>
internal ref struct LayoutContext
{
    public int AvailableWidth;
    public int ViewportHeight;
    public int Depth; // 0 = root container, 1+ = nested
    public PixelRectangle ClipBounds;
    public LayoutStyle Style;

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
        var scratch = new RenderScratchBuffer();
        var elements = scratch.RentLayoutElementList();
        var scrollDiags = scratch.RentScrollContainerDiagList();
        try
        {
            var cursorY = style.VerticalPadding;

            var ctx = new LayoutContext
            {
                AvailableWidth = Math.Max(viewportBounds.Width - (style.HorizontalPadding * 2), 0),
                ViewportHeight = viewportBounds.Height,
                Depth = 0,
                ClipBounds = default,
                Style = style,
            };

            var treeNodes = LayoutNode(root, 0, ref cursorY, ref elements, ref ctx, ref scrollDiags, scratch);

            var dirtyRanges = dirtyNodes is { Count: > 0 }
                ? CollectDirtyRanges(treeNodes, dirtyNodes, scratch)
                : [];

            return new LayoutTreeResult(elements.ToArray(), treeNodes, dirtyRanges, scrollDiags.ToArray());
        }
        finally
        {
            elements.Dispose();
            scrollDiags.Dispose();
        }
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
        ref ScratchList<LayoutElement> elements,
        ref LayoutContext ctx,
        ref ScratchList<ScrollContainerDiag> scrollDiags,
        RenderScratchBuffer scratch)
    {
        switch (node.Kind)
        {
            case VirtualNodeKind.ScrollContainer:
            {
                using var children = scratch.RentLayoutTreeNodeList(node.Children.Length);
                var childDfsIndex = dfsIndex + 1;
                var contentTop = cursorY;
                var isRootContainer = ctx.Depth == 0;
                var containerClipLeft = isRootContainer ? 0 : ctx.Style.HorizontalPadding;
                var containerClipTop = isRootContainer ? 0 : contentTop;
                var containerClipWidth = isRootContainer ? ctx.AvailableWidth + (ctx.Style.HorizontalPadding * 2) : ctx.AvailableWidth;

                var implicitVisibleHeight = ctx.ResolveImplicitVisibleHeight(contentTop);
                var properties = new PropertyReader(node.Properties);
                var explicitHeight = ReadInt(properties, VirtualPropertyKey.Height, 0);
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
                    children.AddRange(LayoutNode(child, childDfsIndex, ref cursorY, ref elements, ref childCtx, ref scrollDiags, scratch));
                    childDfsIndex += CountVirtualNodes(child);
                }
                var contentHeight = Math.Max(cursorY - contentTop, 0);

                var maxScrollY = Math.Max(contentHeight - containerVisibleHeight, 0);
                var scrollY = Math.Clamp(ReadInt(properties, VirtualPropertyKey.ScrollY, 0), 0, maxScrollY);

                if (scrollY > 0)
                {
                    OffsetElementY(ref elements, children.Written, -scrollY);
                }

                var visibleCount = 0;
                var clippedCount = 0;
                foreach (var child in children.Written)
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

                scrollDiags.Add(new ScrollContainerDiag(
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
                var lastChild = children[children.Count - 1];
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
                var properties = new PropertyReader(node.Properties);
                var rectangleBounds = new PixelRectangle(
                    ctx.Style.HorizontalPadding,
                    cursorY,
                    ReadInt(properties, VirtualPropertyKey.Width, Math.Min(ctx.AvailableWidth, 160)),
                    ReadInt(properties, VirtualPropertyKey.Height, ctx.Style.RectangleHeight));
                var elementIndex = elements.Count;
                elements.Add(new LayoutElement(LayoutElementKind.Rectangle, rectangleBounds, ClipBounds: ctx.ClipBounds));
                cursorY += rectangleBounds.Height + ctx.Style.ItemSpacing;
                return [new LayoutTreeNode(dfsIndex, VirtualNodeKind.Rectangle, elementIndex, 1, [])];
            }

            case VirtualNodeKind.Button:
            {
                var label = GetButtonLabel(node);
                if (label.IsNone)
                {
                    throw new InvalidOperationException("Button nodes require an explicit text label child.");
                }

                var labelLength = label.Range.Length;
                var width = Math.Min(ctx.AvailableWidth, Math.Max(
                    ctx.Style.MinimumButtonWidth,
                    labelLength * ctx.Style.ButtonTextWidthFactor + ctx.Style.ButtonHorizontalPadding));
                var properties = new PropertyReader(node.Properties);
                var bounds = new PixelRectangle(
                    ctx.Style.HorizontalPadding,
                    cursorY,
                    ReadInt(properties, VirtualPropertyKey.Width, width),
                    ReadInt(properties, VirtualPropertyKey.Height, ctx.Style.ButtonHeight));
                var actionId = properties.GetActionId(VirtualPropertyKey.ActionId);
                var buttonState = new ButtonVisualState(
                    IsHovered: properties.GetBool(VirtualPropertyKey.IsHovered),
                    IsPressed: properties.GetBool(VirtualPropertyKey.IsPressed),
                    IsFocused: properties.GetBool(VirtualPropertyKey.IsFocused));
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

    private static IReadOnlyList<(int Start, int Count)> CollectDirtyRanges(
        LayoutTreeNode[] treeNodes,
        IReadOnlyList<int> dirtyIndices,
        RenderScratchBuffer scratch)
    {
        using var sortedDirty = scratch.RentIntList(dirtyIndices.Count);
        for (var i = 0; i < dirtyIndices.Count; i++)
        {
            sortedDirty.Add(dirtyIndices[i]);
        }

        sortedDirty.Sort();

        var ranges = scratch.RentRangeList();
        try
        {
            var dirtyCursor = 0;
            CollectDirtyRangesRecursive(treeNodes, sortedDirty.Written, ref dirtyCursor, ref ranges);
            return MergeDirtyRanges(ref ranges);
        }
        finally
        {
            ranges.Dispose();
        }
    }

    private static void CollectDirtyRangesRecursive(
        LayoutTreeNode[] treeNodes,
        ReadOnlySpan<int> sortedDirtyIndices,
        ref int dirtyCursor,
        ref ScratchList<(int Start, int Count)> ranges)
    {
        foreach (var node in treeNodes)
        {
            if (AdvanceDirtyCursor(sortedDirtyIndices, ref dirtyCursor, node.DfsIndex))
            {
                ranges.Add((node.ElementStart, node.ElementCount));
            }

            if (node.Children.Length > 0)
            {
                CollectDirtyRangesRecursive(node.Children, sortedDirtyIndices, ref dirtyCursor, ref ranges);
            }
        }
    }

    private static bool AdvanceDirtyCursor(ReadOnlySpan<int> sortedDirtyIndices, ref int dirtyCursor, int dfsIndex)
    {
        while (dirtyCursor < sortedDirtyIndices.Length && sortedDirtyIndices[dirtyCursor] < dfsIndex)
        {
            dirtyCursor++;
        }

        if (dirtyCursor >= sortedDirtyIndices.Length || sortedDirtyIndices[dirtyCursor] != dfsIndex)
        {
            return false;
        }

        while (dirtyCursor < sortedDirtyIndices.Length && sortedDirtyIndices[dirtyCursor] == dfsIndex)
        {
            dirtyCursor++;
        }

        return true;
    }

    private static IReadOnlyList<(int Start, int Count)> MergeDirtyRanges(ref ScratchList<(int Start, int Count)> ranges)
    {
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

    private sealed class RangeStartComparer : IComparer<(int Start, int Count)>
    {
        public static readonly RangeStartComparer Instance = new();

        public int Compare((int Start, int Count) x, (int Start, int Count) y) => x.Start.CompareTo(y.Start);
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

    private static int ReadInt(PropertyReader reader, VirtualPropertyKey key, int defaultValue) =>
        (int)reader.GetNumber(key, defaultValue);

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

    private static void OffsetElementY(ref ScratchList<LayoutElement> elements, ReadOnlySpan<LayoutTreeNode> nodes, int offsetY)
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
                elements[i] = new LayoutElement(
                    el.Kind,
                    new PixelRectangle(el.Bounds.X, el.Bounds.Y + offsetY, el.Bounds.Width, el.Bounds.Height),
                    el.ClipBounds,
                    el.Text,
                    el.ActionId,
                    el.ButtonState);
            }
        }
    }
}
