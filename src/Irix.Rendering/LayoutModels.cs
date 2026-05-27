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
    TextNodeContent Text = default,
    ActionId ActionId = default,
    ButtonVisualState ButtonState = default) : IEquatable<LayoutElement>
{

    public LayoutElementKind Kind { get; } = Kind;
    public PixelRectangle Bounds { get; } = Bounds;
    public PixelRectangle ClipBounds { get; } = ClipBounds;
    public TextNodeContent Text { get; } = Text;
    public ActionId ActionId { get; } = ActionId;
    public ButtonVisualState ButtonState { get; } = ButtonState;

    public bool Equals(LayoutElement other)
    {
        return Kind == other.Kind
            && Bounds == other.Bounds
            && ClipBounds == other.ClipBounds
            && Text.Equals(other.Text)
            && ActionId.Equals(other.ActionId)
            && ButtonState == other.ButtonState;
    }

    public override bool Equals(object? obj) => obj is LayoutElement other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Bounds, ClipBounds, Text, ActionId, ButtonState);

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
/// <see cref="DrawCommand"/>s it produces. Text/Rectangle → 1 command,
/// Button → 2 commands (FillRect + DrawTextRun).
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

/// <summary>
/// Result of building a layout tree: the flat element array, the flat tree structure
/// for DFS-index lookups, and the dirty element ranges for incremental re-recording.
/// </summary>
internal sealed class LayoutTreeResult(
    IReadOnlyList<LayoutElement> elements,
    LayoutTreeNode[] treeNodes,
    IReadOnlyList<(int Start, int Count)> dirtyElementRanges,
    IReadOnlyList<ScrollContainerDiag> scrollDiagnostics)
{
    public LayoutTreeResult(
        IReadOnlyList<LayoutElement> elements,
        LayoutTreeNode[] treeNodes,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges)
        : this(elements, treeNodes, dirtyElementRanges, [])
    {
    }

    /// <summary>The flat layout element array (full frame).</summary>
    public IReadOnlyList<LayoutElement> Elements { get; } = elements;

    /// <summary>
    /// Flat preorder layout tree nodes. The root is usually <c>TreeNodes[0]</c>;
    /// subtree relationships use <see cref="LayoutTreeNode.SubtreeStart"/> and
    /// <see cref="LayoutTreeNode.SubtreeCount"/>.
    /// </summary>
    public LayoutTreeNode[] TreeNodes { get; } = treeNodes;

    /// <summary>
    /// Merged, sorted ranges of layout elements that correspond to dirty VirtualNodes.
    /// Each tuple is (startIndex, count) into <see cref="Elements"/>.
    /// Overlapping/adjacent ranges are merged to produce the minimal set.
    /// Empty when no dirty nodes are specified.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> DirtyElementRanges { get; } = dirtyElementRanges;

    /// <summary>Diagnostic info for each ScrollContainer encountered during layout.</summary>
    public IReadOnlyList<ScrollContainerDiag> ScrollDiagnostics { get; } = scrollDiagnostics;
}

/// <summary>
/// Diagnostic information for a single ScrollContainer's scroll state.
/// </summary>
/// <param name="DfsIndex">DFS index of this ScrollContainer in the VirtualNode tree.</param>
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
    int ClippedElementCount) : IEquatable<ScrollContainerDiag>
{

    public int DfsIndex { get; } = DfsIndex;
    public int VisibleHeight { get; } = VisibleHeight;
    public int ContentHeight { get; } = ContentHeight;
    public int ScrollY { get; } = ScrollY;
    public int MaxScrollY { get; } = MaxScrollY;
    public int VisibleElementCount { get; } = VisibleElementCount;
    public int ClippedElementCount { get; } = ClippedElementCount;

    public bool Equals(ScrollContainerDiag other)
    {
        return DfsIndex == other.DfsIndex
            && VisibleHeight == other.VisibleHeight
            && ContentHeight == other.ContentHeight
            && ScrollY == other.ScrollY
            && MaxScrollY == other.MaxScrollY
            && VisibleElementCount == other.VisibleElementCount
            && ClippedElementCount == other.ClippedElementCount;
    }

    public override bool Equals(object? obj) => obj is ScrollContainerDiag other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DfsIndex, VisibleHeight, ContentHeight, ScrollY, MaxScrollY, VisibleElementCount, ClippedElementCount);

    public static bool operator ==(ScrollContainerDiag left, ScrollContainerDiag right) => left.Equals(right);

    public static bool operator !=(ScrollContainerDiag left, ScrollContainerDiag right) => !left.Equals(right);
}

/// <summary>
/// Result of recording draw commands: the command batch, resource resolver,
/// element→command range mapping, and dirty command ranges for incremental redraw.
/// </summary>
internal sealed class DrawCommandRecordResult(
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
