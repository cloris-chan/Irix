using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal enum LayoutElementKind : byte
{
    Text,
    Rectangle,
    Button
}

internal readonly record struct ButtonVisualState(
    bool IsHovered,
    bool IsPressed,
    bool IsFocused);

internal readonly record struct LayoutElement(
    LayoutElementKind Kind,
    PixelRectangle Bounds,
    PixelRectangle ClipBounds = default,
    TextNodeContent Text = default,
    ActionId ActionId = default,
    ButtonVisualState ButtonState = default);

internal enum LayoutRebuildReason : byte
{
    None,
    StyleOnly,
    TextSizeAffecting,
    LayoutAffecting,
    TreeStructure,
    ViewportChanged
}

internal readonly record struct LayoutDirtyClassification(int DfsIndex, LayoutRebuildReason Reason);

/// <summary>
/// A node in the layout tree, mapping a VirtualNode's DFS index to its
/// layout element range in the flat <see cref="LayoutElement"/> array.
/// </summary>
internal readonly record struct LayoutTreeNode(
    int DfsIndex,
    VirtualNodeKind Kind,
    int ElementStart,
    int ElementCount,
    LayoutTreeNode[] Children);

/// <summary>
/// Maps a single <see cref="LayoutElement"/> index to the range of
/// <see cref="DrawCommand"/>s it produces. Text/Rectangle → 1 command,
/// Button → 2 commands (FillRect + DrawTextRun).
/// </summary>
internal readonly record struct ElementCommandRange(int CommandStart, int CommandCount);

/// <summary>
/// Result of building a layout tree: the flat element array, the tree structure
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

    /// <summary>Top-level layout tree nodes (usually one root).</summary>
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
internal readonly record struct ScrollContainerDiag(
    int DfsIndex,
    int VisibleHeight,
    int ContentHeight,
    int ScrollY,
    int MaxScrollY,
    int VisibleElementCount,
    int ClippedElementCount);

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
