using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal enum LayoutElementKind : byte
{
    Text,
    Rectangle,
    Button
}

internal readonly struct ButtonVisualState(bool IsHovered, bool IsPressed, bool IsFocused) : IEquatable<ButtonVisualState>
{

    public bool IsHovered { get; } = IsHovered;
    public bool IsPressed { get; } = IsPressed;
    public bool IsFocused { get; } = IsFocused;

    public bool Equals(ButtonVisualState other)
    {
        return IsHovered == other.IsHovered
            && IsPressed == other.IsPressed
            && IsFocused == other.IsFocused;
    }

    public override bool Equals(object? obj) => obj is ButtonVisualState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsHovered, IsPressed, IsFocused);

    public static bool operator ==(ButtonVisualState left, ButtonVisualState right) => left.Equals(right);

    public static bool operator !=(ButtonVisualState left, ButtonVisualState right) => !left.Equals(right);
}

internal readonly struct LayoutElement(
    LayoutElementKind Kind,
    PixelRectangle Bounds,
    PixelRectangle ClipBounds = default,
    TextContentResource Text = default,
    ActionId ActionId = default,
    ButtonVisualState ButtonState = default,
    PaintSlot Background = default,
    BorderStrokeSlot Border = default,
    StyleColorSlot ForegroundColor = default) : IEquatable<LayoutElement>
{

    public LayoutElementKind Kind { get; } = Kind;
    public PixelRectangle Bounds { get; } = Bounds;
    public PixelRectangle ClipBounds { get; } = ClipBounds;
    public TextContentResource Text { get; } = Text;
    public ActionId ActionId { get; } = ActionId;
    public ButtonVisualState ButtonState { get; } = ButtonState;
    public PaintSlot Background { get; } = Background;
    public BorderStrokeSlot Border { get; } = Border;
    public StyleColorSlot ForegroundColor { get; } = ForegroundColor;

    public bool Equals(LayoutElement other)
    {
        return Kind == other.Kind
            && Bounds == other.Bounds
            && ClipBounds == other.ClipBounds
            && Text.Equals(other.Text)
            && ActionId.Equals(other.ActionId)
            && ButtonState == other.ButtonState
            && Background == other.Background
            && Border == other.Border
            && ForegroundColor == other.ForegroundColor;
    }

    public override bool Equals(object? obj) => obj is LayoutElement other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Bounds);
        hash.Add(ClipBounds);
        hash.Add(Text);
        hash.Add(ActionId);
        hash.Add(ButtonState);
        hash.Add(Background);
        hash.Add(Border);
        hash.Add(ForegroundColor);
        return hash.ToHashCode();
    }

    public static bool operator ==(LayoutElement left, LayoutElement right) => left.Equals(right);

    public static bool operator !=(LayoutElement left, LayoutElement right) => !left.Equals(right);
}

internal enum LayoutRebuildReason : byte
{
    None,
    StyleOnly,
    TextSizeAffecting,
    LayoutAffecting,
    TreeStructure,
    ViewportChanged
}

internal readonly struct LayoutDirtyClassification(int DfsIndex, LayoutRebuildReason Reason, InvalidationKind InvalidationKind = InvalidationKind.None) : IEquatable<LayoutDirtyClassification>
{

    public LayoutDirtyClassification(int dfsIndex, LayoutRebuildReason reason)
        : this(dfsIndex, reason, InvalidationFromReason(reason))
    {
    }

    public int DfsIndex { get; } = DfsIndex;
    public LayoutRebuildReason Reason { get; } = Reason;
    public InvalidationKind InvalidationKind { get; } = InvalidationKind;

    private static InvalidationKind InvalidationFromReason(LayoutRebuildReason reason)
    {
        return reason switch
        {
            LayoutRebuildReason.StyleOnly => InvalidationKind.VisualOnly,
            LayoutRebuildReason.TextSizeAffecting => InvalidationKind.TextMeasure,
            LayoutRebuildReason.LayoutAffecting => InvalidationKind.Layout,
            LayoutRebuildReason.TreeStructure => InvalidationKind.TreeStructure,
            LayoutRebuildReason.ViewportChanged => InvalidationKind.ViewportChanged,
            _ => InvalidationKind.None,
        };
    }

    public bool Equals(LayoutDirtyClassification other)
    {
        return DfsIndex == other.DfsIndex
            && Reason == other.Reason
            && InvalidationKind == other.InvalidationKind;
    }

    public override bool Equals(object? obj) => obj is LayoutDirtyClassification other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DfsIndex, Reason, InvalidationKind);

    public static bool operator ==(LayoutDirtyClassification left, LayoutDirtyClassification right) => left.Equals(right);

    public static bool operator !=(LayoutDirtyClassification left, LayoutDirtyClassification right) => !left.Equals(right);
}

/// <summary>
/// A node in the layout tree, mapping a VirtualNode's DFS index to its
/// layout element range in the flat <see cref="LayoutElement"/> array and
/// its contiguous subtree range in the flat preorder layout tree.
/// </summary>
internal readonly struct LayoutTreeNode(
    int DfsIndex,
    NodeKey Key,
    VirtualNodeKind Kind,
    int ElementStart,
    int ElementCount,
    int SubtreeStart,
    int SubtreeCount) : IEquatable<LayoutTreeNode>
{

    public int DfsIndex { get; } = DfsIndex;
    public NodeKey Key { get; } = Key;
    public VirtualNodeKind Kind { get; } = Kind;
    public int ElementStart { get; } = ElementStart;
    public int ElementCount { get; } = ElementCount;
    public int SubtreeStart { get; } = SubtreeStart;
    public int SubtreeCount { get; } = SubtreeCount;

    public bool Equals(LayoutTreeNode other)
    {
        return DfsIndex == other.DfsIndex
            && Key == other.Key
            && Kind == other.Kind
            && ElementStart == other.ElementStart
            && ElementCount == other.ElementCount
            && SubtreeStart == other.SubtreeStart
            && SubtreeCount == other.SubtreeCount;
    }

    public override bool Equals(object? obj) => obj is LayoutTreeNode other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DfsIndex, Key, Kind, ElementStart, ElementCount, SubtreeStart, SubtreeCount);

    public static bool operator ==(LayoutTreeNode left, LayoutTreeNode right) => left.Equals(right);

    public static bool operator !=(LayoutTreeNode left, LayoutTreeNode right) => !left.Equals(right);
}

internal readonly struct LayoutElementRange(int ElementStart, int ElementCount) : IEquatable<LayoutElementRange>
{
    public int ElementStart { get; } = ElementStart;
    public int ElementCount { get; } = ElementCount;

    public bool Equals(LayoutElementRange other)
    {
        return ElementStart == other.ElementStart
            && ElementCount == other.ElementCount;
    }

    public override bool Equals(object? obj) => obj is LayoutElementRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ElementStart, ElementCount);

    public static bool operator ==(LayoutElementRange left, LayoutElementRange right) => left.Equals(right);

    public static bool operator !=(LayoutElementRange left, LayoutElementRange right) => !left.Equals(right);
}

/// <summary>
/// Maps a single <see cref="LayoutElement"/> index to the range of
/// <see cref="DrawCommand"/>s it produces. Text produces one command,
/// Rectangle produces one or two commands, and Button produces one to three.
/// </summary>
internal readonly struct ElementCommandRange(int CommandStart, int CommandCount) : IEquatable<ElementCommandRange>
{

    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;

    public bool Equals(ElementCommandRange other) => CommandStart == other.CommandStart && CommandCount == other.CommandCount;

    public override bool Equals(object? obj) => obj is ElementCommandRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CommandStart, CommandCount);

    public static bool operator ==(ElementCommandRange left, ElementCommandRange right) => left.Equals(right);

    public static bool operator !=(ElementCommandRange left, ElementCommandRange right) => !left.Equals(right);
}

internal readonly struct CompositionTarget(
    CompositionLayerId LayerId,
    NodeKey Key,
    VirtualNodeKind Kind,
    int DfsIndex,
    int CommandStart,
    int CommandCount) : IEquatable<CompositionTarget>
{
    public CompositionLayerId LayerId { get; } = LayerId;
    public NodeKey Key { get; } = Key;
    public VirtualNodeKind Kind { get; } = Kind;
    public int DfsIndex { get; } = DfsIndex;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;

    public bool IsValidForCommandCount(int commandCount)
    {
        return LayerId.IsValid
            && Key != NodeKey.None
            && CommandStart >= 0
            && CommandCount > 0
            && CommandStart <= commandCount
            && CommandStart + CommandCount <= commandCount;
    }

    public bool Equals(CompositionTarget other)
    {
        return LayerId == other.LayerId
            && Key == other.Key
            && Kind == other.Kind
            && DfsIndex == other.DfsIndex
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount;
    }

    public override bool Equals(object? obj) => obj is CompositionTarget other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LayerId, Key, Kind, DfsIndex, CommandStart, CommandCount);

    public static bool operator ==(CompositionTarget left, CompositionTarget right) => left.Equals(right);

    public static bool operator !=(CompositionTarget left, CompositionTarget right) => !left.Equals(right);
}

internal readonly struct ScrollCompositionTarget(
    NodeKey Key,
    int DfsIndex,
    float RetainedScrollY,
    float MaxScrollY,
    ScrollCompositionLayerTarget Layer,
    ScrollCompositionLayerTarget[]? AdditionalLayers = null) : IEquatable<ScrollCompositionTarget>
{
    private readonly ScrollCompositionLayerTarget[]? _additionalLayers = AdditionalLayers;

    public NodeKey Key { get; } = Key;
    public int DfsIndex { get; } = DfsIndex;
    public float RetainedScrollY { get; } = RetainedScrollY;
    public float MaxScrollY { get; } = MaxScrollY;
    public ScrollCompositionLayerTarget Layer { get; } = Layer;
    public int LayerCount => Layer.LayerId.IsValid ? 1 + (_additionalLayers?.Length ?? 0) : 0;

    public CompositionLayerId LayerId => Layer.LayerId;
    public int CommandStart => ResolveCommandStart();
    public int CommandCount => ResolveCommandEnd() - CommandStart;
    public PixelRectangle ClipBounds => Layer.ClipBounds;

    public ScrollCompositionLayerTarget GetLayer(int index)
    {
        if (index == 0 && LayerCount > 0)
        {
            return Layer;
        }

        if (_additionalLayers is not null && (uint)(index - 1) < (uint)_additionalLayers.Length)
        {
            return _additionalLayers[index - 1];
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    public bool IsValidForCommandCount(int commandCount)
    {
        if (Key == NodeKey.None
            || !float.IsFinite(RetainedScrollY)
            || !float.IsFinite(MaxScrollY)
            || RetainedScrollY < 0f
            || MaxScrollY < 0f
            || RetainedScrollY > MaxScrollY
            || LayerCount == 0)
        {
            return false;
        }

        for (var i = 0; i < LayerCount; i++)
        {
            var layer = GetLayer(i);
            if (!layer.IsValidForCommandCount(commandCount) || HasDuplicateLayerId(i, layer.LayerId))
            {
                return false;
            }
        }

        return true;
    }

    public bool Equals(ScrollCompositionTarget other)
    {
        if (Key != other.Key
            || DfsIndex != other.DfsIndex
            || !RetainedScrollY.Equals(other.RetainedScrollY)
            || !MaxScrollY.Equals(other.MaxScrollY)
            || LayerCount != other.LayerCount)
        {
            return false;
        }

        for (var i = 0; i < LayerCount; i++)
        {
            if (GetLayer(i) != other.GetLayer(i))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ScrollCompositionTarget other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(Key);
        hashCode.Add(DfsIndex);
        hashCode.Add(RetainedScrollY);
        hashCode.Add(MaxScrollY);
        for (var i = 0; i < LayerCount; i++)
        {
            hashCode.Add(GetLayer(i));
        }

        return hashCode.ToHashCode();
    }

    public static bool operator ==(ScrollCompositionTarget left, ScrollCompositionTarget right) => left.Equals(right);

    public static bool operator !=(ScrollCompositionTarget left, ScrollCompositionTarget right) => !left.Equals(right);

    private bool HasDuplicateLayerId(int currentIndex, CompositionLayerId layerId)
    {
        for (var i = 0; i < currentIndex; i++)
        {
            if (GetLayer(i).LayerId == layerId)
            {
                return true;
            }
        }

        return false;
    }

    private int ResolveCommandStart()
    {
        var layerCount = LayerCount;
        if (layerCount == 0)
        {
            return 0;
        }

        var start = GetLayer(0).CommandStart;
        for (var i = 1; i < layerCount; i++)
        {
            start = Math.Min(start, GetLayer(i).CommandStart);
        }

        return start;
    }

    private int ResolveCommandEnd()
    {
        var layerCount = LayerCount;
        if (layerCount == 0)
        {
            return 0;
        }

        var end = 0;
        for (var i = 0; i < layerCount; i++)
        {
            var layer = GetLayer(i);
            end = Math.Max(end, layer.CommandStart + layer.CommandCount);
        }

        return end;
    }
}

internal readonly struct ScrollCompositionLayerTarget(
    CompositionLayerId LayerId,
    int CommandStart,
    int CommandCount,
    PixelRectangle ClipBounds) : IEquatable<ScrollCompositionLayerTarget>
{
    public CompositionLayerId LayerId { get; } = LayerId;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public PixelRectangle ClipBounds { get; } = ClipBounds;

    public bool IsValidForCommandCount(int commandCount)
    {
        return LayerId.IsValid
            && CommandStart >= 0
            && CommandCount > 0
            && CommandStart <= commandCount
            && CommandStart + CommandCount <= commandCount
            && ClipBounds.Width > 0
            && ClipBounds.Height > 0;
    }

    public bool Equals(ScrollCompositionLayerTarget other)
    {
        return LayerId == other.LayerId
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && ClipBounds == other.ClipBounds;
    }

    public override bool Equals(object? obj) => obj is ScrollCompositionLayerTarget other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LayerId, CommandStart, CommandCount, ClipBounds);

    public static bool operator ==(ScrollCompositionLayerTarget left, ScrollCompositionLayerTarget right) => left.Equals(right);

    public static bool operator !=(ScrollCompositionLayerTarget left, ScrollCompositionLayerTarget right) => !left.Equals(right);
}

/// <summary>
/// Result of building a layout tree: the flat element array, the flat tree structure
/// for DFS-index lookups, and the dirty element ranges for incremental re-recording.
/// </summary>
internal readonly struct LayoutTreeResult
{
    private static readonly LayoutElement[] EmptyElements = Array.Empty<LayoutElement>();
    private static readonly LayoutTreeNode[] EmptyTreeNodes = Array.Empty<LayoutTreeNode>();
    private static readonly LayoutElementRange[] EmptyElementRanges = Array.Empty<LayoutElementRange>();
    private static readonly (int Start, int Count)[] EmptyDirtyElementRanges = Array.Empty<(int Start, int Count)>();
    private static readonly ScrollContainerDiag[] EmptyScrollDiagnostics = Array.Empty<ScrollContainerDiag>();
    private readonly LayoutElement[]? _elements;
    private readonly LayoutTreeNode[]? _treeNodes;
    private readonly LayoutElementRange[]? _elementRanges;
    private readonly IReadOnlyList<(int Start, int Count)>? _dirtyElementRanges;
    private readonly IReadOnlyList<ScrollContainerDiag>? _scrollDiagnostics;

    public LayoutTreeResult(
        LayoutElement[] elements,
        LayoutTreeNode[] treeNodes,
        LayoutElementRange[] elementRanges,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges,
        IReadOnlyList<ScrollContainerDiag> scrollDiagnostics)
    {
        _elements = NormalizeElements(elements);
        _treeNodes = NormalizeTreeNodes(treeNodes);
        _elementRanges = NormalizeElementRanges(elementRanges);
        _dirtyElementRanges = NormalizeDirtyElementRanges(dirtyElementRanges);
        _scrollDiagnostics = NormalizeScrollDiagnostics(scrollDiagnostics);
    }

    public LayoutTreeResult(
        LayoutElement[] elements,
        LayoutTreeNode[] treeNodes,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges)
        : this(elements, treeNodes, [], dirtyElementRanges, [])
    {
    }

    public LayoutTreeResult(
        LayoutElement[] elements,
        LayoutTreeNode[] treeNodes,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges,
        IReadOnlyList<ScrollContainerDiag> scrollDiagnostics)
        : this(elements, treeNodes, [], dirtyElementRanges, scrollDiagnostics)
    {
    }

    /// <summary>The flat layout element array (full frame).</summary>
    public IReadOnlyList<LayoutElement> Elements => _elements ?? EmptyElements;

    /// <summary>
    /// Flat preorder layout tree nodes. The root is usually <c>TreeNodes[0]</c>;
    /// subtree relationships use <see cref="LayoutTreeNode.SubtreeStart"/> and
    /// <see cref="LayoutTreeNode.SubtreeCount"/>.
    /// </summary>
    public LayoutTreeNode[] TreeNodes => _treeNodes ?? EmptyTreeNodes;

    /// <summary>
    /// DFS-indexed map from VirtualNodes to their layout element range. Entries
    /// with zero count have no directly rendered element.
    /// </summary>
    public LayoutElementRange[] ElementRanges => _elementRanges ?? EmptyElementRanges;

    /// <summary>
    /// Merged, sorted ranges of layout elements that correspond to dirty VirtualNodes.
    /// Each tuple is (startIndex, count) into <see cref="Elements"/>.
    /// Overlapping/adjacent ranges are merged to produce the minimal set.
    /// Empty when no dirty nodes are specified.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> DirtyElementRanges => _dirtyElementRanges ?? EmptyDirtyElementRanges;

    /// <summary>Diagnostic info for each scrollable container encountered during layout.</summary>
    public IReadOnlyList<ScrollContainerDiag> ScrollDiagnostics => _scrollDiagnostics ?? EmptyScrollDiagnostics;

    public LayoutTreeResult WithElementsAndDirtyRanges(
        LayoutElement[] nextElements,
        IReadOnlyList<(int Start, int Count)> nextDirtyElementRanges)
    {
        return new LayoutTreeResult(
            nextElements,
            TreeNodes,
            ElementRanges,
            nextDirtyElementRanges,
            ScrollDiagnostics);
    }

    private static LayoutElement[] NormalizeElements(LayoutElement[] elements) =>
        elements.Length == 0 ? EmptyElements : elements;

    private static LayoutTreeNode[] NormalizeTreeNodes(LayoutTreeNode[] treeNodes) =>
        treeNodes.Length == 0 ? EmptyTreeNodes : treeNodes;

    private static LayoutElementRange[] NormalizeElementRanges(LayoutElementRange[] elementRanges) =>
        elementRanges.Length == 0 ? EmptyElementRanges : elementRanges;

    private static IReadOnlyList<(int Start, int Count)> NormalizeDirtyElementRanges(IReadOnlyList<(int Start, int Count)> dirtyElementRanges) =>
        dirtyElementRanges.Count == 0 ? EmptyDirtyElementRanges : dirtyElementRanges;

    private static IReadOnlyList<ScrollContainerDiag> NormalizeScrollDiagnostics(IReadOnlyList<ScrollContainerDiag> scrollDiagnostics) =>
        scrollDiagnostics.Count == 0 ? EmptyScrollDiagnostics : scrollDiagnostics;
}

/// <summary>
/// Diagnostic information for a single scrollable container's scroll state.
/// </summary>
/// <param name="DfsIndex">DFS index of this container in the VirtualNode tree.</param>
/// <param name="VisibleHeight">The container's visible area height (after explicit Height or viewport default).</param>
/// <param name="ContentHeight">Total height of all children before scrolling.</param>
/// <param name="ScrollY">The clamped scroll offset applied to children (always in [0, MaxScrollY]).</param>
/// <param name="MaxScrollY">Maximum scroll offset: max(ContentHeight - VisibleHeight, 0).</param>
/// <param name="VisibleElementCount">
/// Number of child elements that <b>intersect</b> the container's visible area.
/// An element is counted as visible if any part of its bounds overlaps with the visible region
/// (i.e., not fully above or fully below the visible area).
/// </param>
/// <param name="ClippedElementCount">
/// Number of child elements that are <b>fully outside</b> the container's visible area.
/// An element is clipped if its bottom edge is at or above the visible top,
/// or its top edge is at or below the visible bottom.
/// </param>
internal readonly struct ScrollContainerDiag(
    int DfsIndex,
    int VisibleHeight,
    int ContentHeight,
    int ScrollY,
    int MaxScrollY,
    int VisibleElementCount,
    int ClippedElementCount,
    PixelRectangle ClipBounds = default) : IEquatable<ScrollContainerDiag>
{

    public int DfsIndex { get; } = DfsIndex;
    public int VisibleHeight { get; } = VisibleHeight;
    public int ContentHeight { get; } = ContentHeight;
    public int ScrollY { get; } = ScrollY;
    public int MaxScrollY { get; } = MaxScrollY;
    public int VisibleElementCount { get; } = VisibleElementCount;
    public int ClippedElementCount { get; } = ClippedElementCount;
    public PixelRectangle ClipBounds { get; } = ClipBounds;

    public bool Equals(ScrollContainerDiag other)
    {
        return DfsIndex == other.DfsIndex
            && VisibleHeight == other.VisibleHeight
            && ContentHeight == other.ContentHeight
            && ScrollY == other.ScrollY
            && MaxScrollY == other.MaxScrollY
            && VisibleElementCount == other.VisibleElementCount
            && ClippedElementCount == other.ClippedElementCount
            && ClipBounds == other.ClipBounds;
    }

    public override bool Equals(object? obj) => obj is ScrollContainerDiag other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DfsIndex, VisibleHeight, ContentHeight, ScrollY, MaxScrollY, VisibleElementCount, ClippedElementCount, ClipBounds);

    public static bool operator ==(ScrollContainerDiag left, ScrollContainerDiag right) => left.Equals(right);

    public static bool operator !=(ScrollContainerDiag left, ScrollContainerDiag right) => !left.Equals(right);
}

/// <summary>
/// Result of recording draw commands: the command batch, resource resolver,
/// element→command range mapping, and dirty command ranges for incremental redraw.
/// </summary>
internal readonly struct DrawCommandRecordResult(
    DrawCommandBatch commands,
    IFrameResourceResolver resources,
    ElementCommandRange[] elementCommandRanges,
    IReadOnlyList<(int Start, int Count)> dirtyCommandRanges)
{
    public DrawCommandRecordResult(DrawCommandBatch commands, IFrameResourceResolver resources)
        : this(commands, resources, [], [])
    {
    }

    public DrawCommandBatch Commands { get; } = commands;
    public IFrameResourceResolver Resources { get; } = resources;

    /// <summary>
    /// Maps each <see cref="LayoutElement"/> index to its <see cref="DrawCommand"/> range.
    /// <c>ElementCommandRanges[elementIndex]</c> gives (commandStart, commandCount).
    /// </summary>
    public ElementCommandRange[] ElementCommandRanges { get; } = elementCommandRanges;

    /// <summary>
    /// Merged, sorted ranges of draw commands that correspond to dirty layout elements.
    /// Each tuple is (startIndex, count) into the command batch.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> DirtyCommandRanges { get; } = dirtyCommandRanges;
}
