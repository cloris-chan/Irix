using Irix.Platform;

namespace Irix.Rendering;

internal enum LayoutElementKind : byte
{
    Text,
    Rectangle,
    Button
}

internal readonly record struct LayoutElement(
    LayoutElementKind Kind,
    PixelRectangle Bounds,
    string? Text = null,
    string? ActionId = null);

/// <summary>
/// A node in the layout tree, mapping a VirtualNode's DFS index to its
/// layout element range in the flat <see cref="LayoutElement"/> array.
/// </summary>
internal readonly record struct LayoutTreeNode(
    int DfsIndex,
    LayoutElementKind Kind,
    int ElementStart,
    int ElementCount,
    LayoutTreeNode[] Children);

/// <summary>
/// Result of building a layout tree: the flat element array, the tree structure
/// for DFS-index lookups, and the dirty element ranges for incremental re-recording.
/// </summary>
internal sealed class LayoutTreeResult
{
    public LayoutTreeResult(
        IReadOnlyList<LayoutElement> elements,
        LayoutTreeNode[] treeNodes,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges)
    {
        Elements = elements;
        TreeNodes = treeNodes;
        DirtyElementRanges = dirtyElementRanges;
    }

    /// <summary>The flat layout element array (full frame).</summary>
    public IReadOnlyList<LayoutElement> Elements { get; }

    /// <summary>Top-level layout tree nodes (usually one root).</summary>
    public LayoutTreeNode[] TreeNodes { get; }

    /// <summary>
    /// Ranges of layout elements that correspond to dirty VirtualNodes.
    /// Each tuple is (startIndex, count) into <see cref="Elements"/>.
    /// Empty when no dirty nodes are specified.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> DirtyElementRanges { get; }
}
