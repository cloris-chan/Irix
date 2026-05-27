using System.Diagnostics;
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
    private static readonly (int Start, int Count)[] EmptyDirtyElementRanges = Array.Empty<(int Start, int Count)>();
    private static readonly ScrollContainerDiag[] EmptyScrollDiagnostics = Array.Empty<ScrollContainerDiag>();

    private const int InlineLayoutElementCapacity = 32;
    private const int InlineLayoutTreeNodeCapacity = 32;
    private const int InlineLayoutElementRangeCapacity = 64;
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
        return BuildLayoutTreeCore(root, viewportBounds, dirtyNodes, measureAllocation: false, out _);
    }

    internal LayoutTreeResult BuildLayoutTree(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes, out LayoutBuildAllocationAttribution attribution)
    {
        return BuildLayoutTreeCore(root, viewportBounds, dirtyNodes, measureAllocation: true, out attribution);
    }

    private LayoutTreeResult BuildLayoutTreeCore(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes, bool measureAllocation, out LayoutBuildAllocationAttribution attribution)
    {
        attribution = default;
        var scratch = new RenderScratchBuffer();
        Span<LayoutElement> elementStorage = stackalloc LayoutElement[InlineLayoutElementCapacity];
        Span<LayoutTreeNode> treeNodeStorage = stackalloc LayoutTreeNode[InlineLayoutTreeNodeCapacity];
        Span<LayoutElementRange> elementRangeStorage = stackalloc LayoutElementRange[InlineLayoutElementRangeCapacity];
        Span<ScrollContainerDiag> scrollDiagStorage = stackalloc ScrollContainerDiag[InlineScrollDiagCapacity];
        var state = new LayoutBuildState(
            scratch.CreateLayoutElementList(elementStorage),
            scratch.CreateLayoutTreeNodeList(treeNodeStorage),
            scratch.CreateLayoutElementRangeList(elementRangeStorage),
            scratch.CreateScrollContainerDiagList(scrollDiagStorage),
            style);
        try
        {
            var beforeNodeWalk = GetAllocatedBytes(measureAllocation);
            state.LayoutRoot(root, viewportBounds);
            attribution = attribution.WithNodeWalk(AllocatedDelta(measureAllocation, beforeNodeWalk));

            var beforeDirtyRanges = GetAllocatedBytes(measureAllocation);
            var dirtyRanges = dirtyNodes is { Count: > 0 }
                ? CollectDirtyRanges(state.ElementRanges, dirtyNodes, scratch)
                : EmptyDirtyElementRanges;
            attribution = attribution.WithDirtyRanges(AllocatedDelta(measureAllocation, beforeDirtyRanges));

            var beforeElementsArray = GetAllocatedBytes(measureAllocation);
            var elements = state.ElementsToArray();
            attribution = attribution.WithElementArray(AllocatedDelta(measureAllocation, beforeElementsArray));

            var beforeTreeNodesArray = GetAllocatedBytes(measureAllocation);
            var treeNodes = state.TreeNodesToArray();
            attribution = attribution.WithTreeNodeArray(AllocatedDelta(measureAllocation, beforeTreeNodesArray));

            var beforeScrollDiagnosticsArray = GetAllocatedBytes(measureAllocation);
            var scrollDiagnostics = state.ScrollDiagnosticsToArray();
            attribution = attribution.WithScrollDiagnosticsArray(AllocatedDelta(measureAllocation, beforeScrollDiagnosticsArray));

            var beforeResult = GetAllocatedBytes(measureAllocation);
            var result = new LayoutTreeResult(elements, treeNodes, dirtyRanges, scrollDiagnostics);
            attribution = attribution.WithResult(AllocatedDelta(measureAllocation, beforeResult));
            return result;
        }
        finally
        {
            state.Dispose();
        }
    }

    public IReadOnlyList<LayoutElement> BuildElements(VirtualNode root, PixelRectangle viewportBounds, IReadOnlyList<int>? dirtyNodes = null)
    {
        return BuildLayoutTree(root, viewportBounds, dirtyNodes).Elements;
    }

    private ref struct LayoutBuildState
    {
        private ScratchList<LayoutElement> _elements;
        private ScratchList<LayoutTreeNode> _treeNodes;
        private ScratchList<LayoutElementRange> _elementRanges;
        private ScratchList<ScrollContainerDiag> _scrollDiags;
        private readonly LayoutStyle _style;
        private int _cursorY;
        private LayoutContext _ctx;

        public LayoutBuildState(
            ScratchList<LayoutElement> elements,
            ScratchList<LayoutTreeNode> treeNodes,
            ScratchList<LayoutElementRange> elementRanges,
            ScratchList<ScrollContainerDiag> scrollDiags,
            LayoutStyle style)
        {
            _elements = elements;
            _treeNodes = treeNodes;
            _elementRanges = elementRanges;
            _scrollDiags = scrollDiags;
            _style = style;
            _cursorY = style.VerticalPadding;
            _ctx = default;
        }

        public readonly ReadOnlySpan<LayoutTreeNode> TreeNodes => _treeNodes.Written;

        public readonly ReadOnlySpan<LayoutElementRange> ElementRanges => _elementRanges.Written;

        public LayoutElement[] ElementsToArray() => _elements.ToArray();

        public LayoutTreeNode[] TreeNodesToArray() => _treeNodes.ToArray();

        public ScrollContainerDiag[] ScrollDiagnosticsToArray() =>
            _scrollDiags.Count == 0 ? EmptyScrollDiagnostics : _scrollDiags.ToArray();

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

            var consumed = LayoutNode(root, 0);
            Debug.Assert(consumed == _elementRanges.Count, "Layout DFS projection did not register every consumed virtual node.");
        }

        public void Dispose()
        {
            _elements.Dispose();
            _treeNodes.Dispose();
            _elementRanges.Dispose();
            _scrollDiags.Dispose();
        }

        private int LayoutNode(VirtualNode node, int dfsIndex)
        {
            switch (node.Kind)
            {
                case VirtualNodeKind.ScrollContainer:
                    return LayoutScrollContainer(node, dfsIndex);

                case VirtualNodeKind.Text:
                    return LayoutText(node, dfsIndex);

                case VirtualNodeKind.Rectangle:
                    return LayoutRectangle(node, dfsIndex);

                case VirtualNodeKind.Button:
                    return LayoutButton(node, dfsIndex);

                default:
                    RegisterElementRange(dfsIndex, 0, 0);
                    return 1;
            }
        }

        private int LayoutScrollContainer(VirtualNode node, int dfsIndex)
        {
            var treeNodeIndex = _treeNodes.Count;
            _treeNodes.Add(default);
            var consumedCount = 1;
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
                consumedCount += LayoutNode(child, dfsIndex + consumedCount);
            }

            var subtreeCount = _treeNodes.Count - subtreeStart;
            var elementCount = _elements.Count - elementStart;
            var contentHeight = Math.Max(_cursorY - contentTop, 0);
            _ctx = parentCtx;

            var maxScrollY = Math.Max(contentHeight - containerVisibleHeight, 0);
            var scrollY = Math.Clamp(ReadInt(properties, VirtualPropertyKey.ScrollY, 0), 0, maxScrollY);

            if (scrollY > 0)
            {
                OffsetElementY(elementStart, elementCount, -scrollY);
            }

            var visibleCount = 0;
            var clippedCount = 0;
            for (var i = elementStart; i < elementStart + elementCount; i++)
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

            _scrollDiags.Add(new ScrollContainerDiag(
                dfsIndex,
                containerVisibleHeight,
                contentHeight,
                scrollY,
                maxScrollY,
                visibleCount,
                clippedCount,
                containerClip));

            _cursorY = contentTop + containerVisibleHeight + _ctx.Style.ItemSpacing;

            if (subtreeCount == 0)
            {
                _treeNodes.WrittenMutable[treeNodeIndex] = new LayoutTreeNode(dfsIndex, node.Key, VirtualNodeKind.ScrollContainer, 0, 0, subtreeStart, 0);
                RegisterElementRange(dfsIndex, 0, 0);
                return consumedCount;
            }

            _treeNodes.WrittenMutable[treeNodeIndex] = new LayoutTreeNode(dfsIndex, node.Key, VirtualNodeKind.ScrollContainer, elementStart, elementCount, subtreeStart, subtreeCount);
            RegisterElementRange(dfsIndex, elementStart, elementCount);
            return consumedCount;
        }

        private int LayoutText(VirtualNode node, int dfsIndex)
        {
            var content = GetTextContent(node);
            if (content.IsNone)
            {
                RegisterElementRange(dfsIndex, 0, 0);
                return 1;
            }

            var elementIndex = _elements.Count;
            _elements.Add(new LayoutElement(
                LayoutElementKind.Text,
                new PixelRectangle(_ctx.Style.HorizontalPadding, _cursorY, _ctx.AvailableWidth, _ctx.Style.TextHeight),
                ClipBounds: _ctx.ClipBounds,
                Text: content));
            _cursorY += _ctx.Style.TextHeight + _ctx.Style.ItemSpacing;
            _treeNodes.Add(new LayoutTreeNode(dfsIndex, node.Key, VirtualNodeKind.Text, elementIndex, 1, 0, 0));
            RegisterElementRange(dfsIndex, elementIndex, 1);
            return 1;
        }

        private int LayoutRectangle(VirtualNode node, int dfsIndex)
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
            _treeNodes.Add(new LayoutTreeNode(dfsIndex, node.Key, VirtualNodeKind.Rectangle, elementIndex, 1, 0, 0));
            RegisterElementRange(dfsIndex, elementIndex, 1);
            return 1;
        }

        private int LayoutButton(VirtualNode node, int dfsIndex)
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
            _treeNodes.Add(new LayoutTreeNode(dfsIndex, node.Key, VirtualNodeKind.Button, elementIndex, 1, 0, 0));
            RegisterElementRange(dfsIndex, elementIndex, 1);

            var childCount = node.Children.Length;
            for (var i = 0; i < childCount; i++)
            {
                RegisterElementRange(dfsIndex + 1 + i, elementIndex, 1);
            }

            return 1 + childCount;
        }

        private void RegisterElementRange(int dfsIndex, int elementStart, int elementCount)
        {
            while (_elementRanges.Count <= dfsIndex)
            {
                _elementRanges.Add(default);
            }

            _elementRanges[dfsIndex] = new LayoutElementRange(elementStart, elementCount);
        }

        private void OffsetElementY(int elementStart, int elementCount, int offsetY)
        {
            if (offsetY == 0 || elementCount <= 0)
            {
                return;
            }

            for (var i = elementStart; i < elementStart + elementCount; i++)
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

    private static IReadOnlyList<(int Start, int Count)> CollectDirtyRanges(
        ReadOnlySpan<LayoutElementRange> elementRanges,
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
                CollectDirtyRangesFromElementRanges(elementRanges, sortedDirty.Written, ref dirtyCursor, ref ranges);
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

    private static void CollectDirtyRangesFromElementRanges(
        ReadOnlySpan<LayoutElementRange> elementRanges,
        ReadOnlySpan<int> sortedDirtyIndices,
        ref int dirtyCursor,
        scoped ref ScratchList<(int Start, int Count)> ranges)
    {
        for (var dfsIndex = 0; dfsIndex < elementRanges.Length; dfsIndex++)
        {
            var range = elementRanges[dfsIndex];
            if (AdvanceDirtyCursor(sortedDirtyIndices, ref dirtyCursor, dfsIndex) && range.ElementCount > 0)
            {
                ranges.Add((range.ElementStart, range.ElementCount));
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
            return EmptyDirtyElementRanges;
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

    private static long GetAllocatedBytes(bool enabled) => enabled ? GC.GetTotalAllocatedBytes(false) : 0;

    private static long AllocatedDelta(bool enabled, long before) => enabled ? GC.GetTotalAllocatedBytes(false) - before : 0;
}

internal readonly struct LayoutBuildAllocationAttribution(
    long NodeWalkBytes,
    long DirtyRangeBytes,
    long ElementArrayBytes,
    long TreeNodeArrayBytes,
    long ScrollDiagnosticsArrayBytes,
    long ResultBytes) : IEquatable<LayoutBuildAllocationAttribution>
{
    public long NodeWalkBytes { get; } = NodeWalkBytes;
    public long DirtyRangeBytes { get; } = DirtyRangeBytes;
    public long ElementArrayBytes { get; } = ElementArrayBytes;
    public long TreeNodeArrayBytes { get; } = TreeNodeArrayBytes;
    public long ScrollDiagnosticsArrayBytes { get; } = ScrollDiagnosticsArrayBytes;
    public long ResultBytes { get; } = ResultBytes;
    public long TotalBytes => NodeWalkBytes + DirtyRangeBytes + ElementArrayBytes + TreeNodeArrayBytes + ScrollDiagnosticsArrayBytes + ResultBytes;

    public LayoutBuildAllocationAttribution Add(LayoutBuildAllocationAttribution other) =>
        new(
            NodeWalkBytes + other.NodeWalkBytes,
            DirtyRangeBytes + other.DirtyRangeBytes,
            ElementArrayBytes + other.ElementArrayBytes,
            TreeNodeArrayBytes + other.TreeNodeArrayBytes,
            ScrollDiagnosticsArrayBytes + other.ScrollDiagnosticsArrayBytes,
            ResultBytes + other.ResultBytes);

    public LayoutBuildAllocationAttribution WithNodeWalk(long bytes) => new(NodeWalkBytes + bytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithDirtyRanges(long bytes) => new(NodeWalkBytes, DirtyRangeBytes + bytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithElementArray(long bytes) => new(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes + bytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithTreeNodeArray(long bytes) => new(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes + bytes, ScrollDiagnosticsArrayBytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithScrollDiagnosticsArray(long bytes) => new(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes + bytes, ResultBytes);

    public LayoutBuildAllocationAttribution WithResult(long bytes) => new(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes + bytes);

    public bool Equals(LayoutBuildAllocationAttribution other)
    {
        return NodeWalkBytes == other.NodeWalkBytes
            && DirtyRangeBytes == other.DirtyRangeBytes
            && ElementArrayBytes == other.ElementArrayBytes
            && TreeNodeArrayBytes == other.TreeNodeArrayBytes
            && ScrollDiagnosticsArrayBytes == other.ScrollDiagnosticsArrayBytes
            && ResultBytes == other.ResultBytes;
    }

    public override bool Equals(object? obj) => obj is LayoutBuildAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(NodeWalkBytes, DirtyRangeBytes, ElementArrayBytes, TreeNodeArrayBytes, ScrollDiagnosticsArrayBytes, ResultBytes);
}
