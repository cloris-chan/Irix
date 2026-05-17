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
    private const int InlineLayoutElementCapacity = 32;
    private const int InlineLayoutTreeNodeCapacity = 32;
    private const int InlineScrollDiagCapacity = 8;
    private const int InlineDirtyIndexCapacity = 32;
    private const int InlineRangeCapacity = 16;

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
        Span<LayoutElement> elementStorage = stackalloc LayoutElement[InlineLayoutElementCapacity];
        Span<LayoutTreeNode> treeNodeStorage = stackalloc LayoutTreeNode[InlineLayoutTreeNodeCapacity];
        Span<ScrollContainerDiag> scrollDiagStorage = stackalloc ScrollContainerDiag[InlineScrollDiagCapacity];
        var state = new LayoutBuildState(
            scratch.CreateLayoutElementList(elementStorage),
            scratch.CreateLayoutTreeNodeList(treeNodeStorage),
            scratch.CreateScrollContainerDiagList(scrollDiagStorage),
            style);
        try
        {
            state.LayoutRoot(root, viewportBounds);

            var dirtyRanges = dirtyNodes is { Count: > 0 }
                ? CollectDirtyRanges(state.TreeNodes, dirtyNodes, scratch)
                : [];

            return new LayoutTreeResult(state.ElementsToArray(), state.TreeNodesToArray(), dirtyRanges, state.ScrollDiagnosticsToArray());
        }
        finally
        {
            state.Dispose();
        }
    }

    /// <summary>
    /// Backward-compatible overload returning flat elements only.
    /// </summary>
    public IReadOnlyList<LayoutElement> Build(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null)
    {
        return BuildLayoutTree(root, viewportBounds, dirtyNodes).Elements;
    }

    private ref struct LayoutBuildState
    {
        private ScratchList<LayoutElement> _elements;
        private ScratchList<LayoutTreeNode> _treeNodes;
        private ScratchList<ScrollContainerDiag> _scrollDiags;
        private readonly LayoutStyle _style;
        private int _cursorY;
        private LayoutContext _ctx;

        public LayoutBuildState(
            ScratchList<LayoutElement> elements,
            ScratchList<LayoutTreeNode> treeNodes,
            ScratchList<ScrollContainerDiag> scrollDiags,
            LayoutStyle style)
        {
            _elements = elements;
            _treeNodes = treeNodes;
            _scrollDiags = scrollDiags;
            _style = style;
            _cursorY = style.VerticalPadding;
            _ctx = default;
        }

        public readonly ReadOnlySpan<LayoutTreeNode> TreeNodes => _treeNodes.Written;

        public LayoutElement[] ElementsToArray() => _elements.ToArray();

        public LayoutTreeNode[] TreeNodesToArray() => _treeNodes.ToArray();

        public ScrollContainerDiag[] ScrollDiagnosticsToArray() => _scrollDiags.ToArray();

        public void LayoutRoot(VirtualNode root, PixelRectangle viewportBounds)
        {
            _ctx = new LayoutContext
            {
                AvailableWidth = Math.Max(viewportBounds.Width - (_style.HorizontalPadding * 2), 0),
                ViewportHeight = viewportBounds.Height,
                Depth = 0,
                ClipBounds = default,
                Style = _style,
            };

            LayoutNode(root, 0);
        }

        public void Dispose()
        {
            _elements.Dispose();
            _treeNodes.Dispose();
            _scrollDiags.Dispose();
        }

        private void LayoutNode(VirtualNode node, int dfsIndex)
        {
            switch (node.Kind)
            {
                case VirtualNodeKind.ScrollContainer:
                    LayoutScrollContainer(node, dfsIndex);
                    return;

                case VirtualNodeKind.Text:
                    LayoutText(node, dfsIndex);
                    return;

                case VirtualNodeKind.Rectangle:
                    LayoutRectangle(node, dfsIndex);
                    return;

                case VirtualNodeKind.Button:
                    LayoutButton(node, dfsIndex);
                    return;

                default:
                    return;
            }
        }

        private void LayoutScrollContainer(VirtualNode node, int dfsIndex)
        {
            var treeNodeIndex = _treeNodes.Count;
            _treeNodes.Add(default);
            var childDfsIndex = dfsIndex + 1;
            var contentTop = _cursorY;
            var isRootContainer = _ctx.Depth == 0;
            var containerClipLeft = isRootContainer ? 0 : _ctx.Style.HorizontalPadding;
            var containerClipTop = isRootContainer ? 0 : contentTop;
            var containerClipWidth = isRootContainer ? _ctx.AvailableWidth + (_ctx.Style.HorizontalPadding * 2) : _ctx.AvailableWidth;

            var implicitVisibleHeight = _ctx.ResolveImplicitVisibleHeight(contentTop);
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
            if (_ctx.ClipBounds.Width > 0 && _ctx.ClipBounds.Height > 0)
            {
                containerClip = IntersectRect(containerClip, _ctx.ClipBounds);
            }

            var parentCtx = _ctx;
            _ctx.Depth = parentCtx.Depth + 1;
            _ctx.ClipBounds = containerClip;
            _cursorY = contentTop;
            var elementStart = _elements.Count;
            var subtreeStart = treeNodeIndex + 1;
            foreach (var child in node.Children)
            {
                LayoutNode(child, childDfsIndex);
                childDfsIndex += CountVirtualNodes(child);
            }

            var subtreeCount = _treeNodes.Count - subtreeStart;
            var elementCount = _elements.Count - elementStart;
            var contentHeight = Math.Max(_cursorY - contentTop, 0);
            _ctx = parentCtx;

            var maxScrollY = Math.Max(contentHeight - containerVisibleHeight, 0);
            var scrollY = Math.Clamp(ReadInt(properties, VirtualPropertyKey.ScrollY, 0), 0, maxScrollY);

            if (scrollY > 0)
            {
                OffsetElementY(_treeNodes.Written[subtreeStart..(subtreeStart + subtreeCount)], -scrollY);
            }

            var visibleCount = 0;
            var clippedCount = 0;
            foreach (var child in _treeNodes.Written[subtreeStart..(subtreeStart + subtreeCount)])
            {
                for (var i = child.ElementStart; i < child.ElementStart + child.ElementCount; i++)
                {
                    var el = _elements[i];
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

            _scrollDiags.Add(new ScrollContainerDiag(
                dfsIndex,
                containerVisibleHeight,
                contentHeight,
                scrollY,
                maxScrollY,
                visibleCount,
                clippedCount));

            _cursorY = contentTop + containerVisibleHeight + _ctx.Style.ItemSpacing;

            if (subtreeCount == 0)
            {
                _treeNodes.WrittenMutable[treeNodeIndex] = new LayoutTreeNode(dfsIndex, VirtualNodeKind.ScrollContainer, 0, 0, subtreeStart, 0);
                return;
            }

            _treeNodes.WrittenMutable[treeNodeIndex] = new LayoutTreeNode(dfsIndex, VirtualNodeKind.ScrollContainer, elementStart, elementCount, subtreeStart, subtreeCount);
        }

        private void LayoutText(VirtualNode node, int dfsIndex)
        {
            var content = GetTextContent(node);
            if (content.IsNone)
            {
                return;
            }

            var elementIndex = _elements.Count;
            _elements.Add(new LayoutElement(
                LayoutElementKind.Text,
                new PixelRectangle(_ctx.Style.HorizontalPadding, _cursorY, _ctx.AvailableWidth, _ctx.Style.TextHeight),
                ClipBounds: _ctx.ClipBounds,
                Text: content));
            _cursorY += _ctx.Style.TextHeight + _ctx.Style.ItemSpacing;
            _treeNodes.Add(new LayoutTreeNode(dfsIndex, VirtualNodeKind.Text, elementIndex, 1, 0, 0));
        }

        private void LayoutRectangle(VirtualNode node, int dfsIndex)
        {
            var properties = new PropertyReader(node.Properties);
            var rectangleBounds = new PixelRectangle(
                _ctx.Style.HorizontalPadding,
                _cursorY,
                ReadInt(properties, VirtualPropertyKey.Width, Math.Min(_ctx.AvailableWidth, 160)),
                ReadInt(properties, VirtualPropertyKey.Height, _ctx.Style.RectangleHeight));
            var elementIndex = _elements.Count;
            _elements.Add(new LayoutElement(LayoutElementKind.Rectangle, rectangleBounds, ClipBounds: _ctx.ClipBounds));
            _cursorY += rectangleBounds.Height + _ctx.Style.ItemSpacing;
            _treeNodes.Add(new LayoutTreeNode(dfsIndex, VirtualNodeKind.Rectangle, elementIndex, 1, 0, 0));
        }

        private void LayoutButton(VirtualNode node, int dfsIndex)
        {
            var label = GetButtonLabel(node);
            if (label.IsNone)
            {
                throw new InvalidOperationException("Button nodes require an explicit text label child.");
            }

            var labelLength = label.Range.Length;
            var width = Math.Min(_ctx.AvailableWidth, Math.Max(
                _ctx.Style.MinimumButtonWidth,
                labelLength * _ctx.Style.ButtonTextWidthFactor + _ctx.Style.ButtonHorizontalPadding));
            var properties = new PropertyReader(node.Properties);
            var bounds = new PixelRectangle(
                _ctx.Style.HorizontalPadding,
                _cursorY,
                ReadInt(properties, VirtualPropertyKey.Width, width),
                ReadInt(properties, VirtualPropertyKey.Height, _ctx.Style.ButtonHeight));
            var actionId = properties.GetActionId(VirtualPropertyKey.ActionId);
            var buttonState = new ButtonVisualState(
                IsHovered: properties.GetBool(VirtualPropertyKey.IsHovered),
                IsPressed: properties.GetBool(VirtualPropertyKey.IsPressed),
                IsFocused: properties.GetBool(VirtualPropertyKey.IsFocused));
            var elementIndex = _elements.Count;
            _elements.Add(new LayoutElement(
                LayoutElementKind.Button,
                bounds,
                ClipBounds: _ctx.ClipBounds,
                Text: label,
                ActionId: actionId,
                ButtonState: buttonState));
            _cursorY += bounds.Height + _ctx.Style.ItemSpacing;
            _treeNodes.Add(new LayoutTreeNode(dfsIndex, VirtualNodeKind.Button, elementIndex, 1, 0, 0));
        }

        private void OffsetElementY(ReadOnlySpan<LayoutTreeNode> nodes, int offsetY)
        {
            if (offsetY == 0)
            {
                return;
            }

            foreach (var node in nodes)
            {
                for (var i = node.ElementStart; i < node.ElementStart + node.ElementCount; i++)
                {
                    var el = _elements[i];
                    _elements[i] = new LayoutElement(
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

    private static IReadOnlyList<(int Start, int Count)> CollectDirtyRanges(
        ReadOnlySpan<LayoutTreeNode> treeNodes,
        IReadOnlyList<int> dirtyIndices,
        RenderScratchBuffer scratch)
    {
        Span<int> sortedDirtyStorage = stackalloc int[InlineDirtyIndexCapacity];
        scoped var sortedDirty = scratch.CreateDirtyIndexList(sortedDirtyStorage);
        for (var i = 0; i < dirtyIndices.Count; i++)
        {
            sortedDirty.Add(dirtyIndices[i]);
        }

        try
        {
            sortedDirty.Sort();

            Span<(int Start, int Count)> rangeStorage = stackalloc (int Start, int Count)[InlineRangeCapacity];
            scoped var ranges = scratch.CreateRangeList(rangeStorage);
            var dirtyCursor = 0;
            try
            {
                CollectDirtyRangesRecursive(treeNodes, sortedDirty.Written, ref dirtyCursor, ref ranges);
                return MergeDirtyRanges(ref ranges);
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

    private static void CollectDirtyRangesRecursive(
        ReadOnlySpan<LayoutTreeNode> treeNodes,
        ReadOnlySpan<int> sortedDirtyIndices,
        ref int dirtyCursor,
        scoped ref ScratchList<(int Start, int Count)> ranges)
    {
        foreach (var node in treeNodes)
        {
            if (AdvanceDirtyCursor(sortedDirtyIndices, ref dirtyCursor, node.DfsIndex))
            {
                ranges.Add((node.ElementStart, node.ElementCount));
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

    private static IReadOnlyList<(int Start, int Count)> MergeDirtyRanges(scoped ref ScratchList<(int Start, int Count)> ranges)
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

}
