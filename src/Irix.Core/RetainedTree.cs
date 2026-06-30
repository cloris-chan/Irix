namespace Irix;

/// <summary>
/// Result of <see cref="RetainedTree.Apply"/>, containing the dirty node set
/// and the previous tree state for dirty classification.
/// </summary>
internal readonly struct ApplyResult(
    IReadOnlyList<int> Dirty,
    VirtualNodeTree PreviousTree)
{
    public IReadOnlyList<int> Dirty { get; } = Dirty;
    public VirtualNodeTree PreviousTree { get; } = PreviousTree;
    public VirtualNode PreviousRoot => PreviousTree.Root;
    public TextBufferSnapshot PreviousTextSnapshot => PreviousTree.TextSnapshot;
}

/// <summary>
/// Applies canonical diff batches to a <see cref="VirtualNode"/> tree.
/// Non-canonical patch application is intentionally unsupported; runtime diff
/// batches carry <see cref="PatchBatch.HasCanonicalRoot"/> and provide the next
/// retained root directly.
///
/// <para><b>Dirty index semantics:</b></para>
/// <para>
/// The returned dirty set contains the DFS indices of parent nodes whose <em>children list</em>
/// changed (Add/Remove), or the node itself whose content changed (Update/ReplaceRoot).
/// All indices refer to the <em>result tree's</em> DFS ordering. The output is
/// <b>sorted ascending and deduplicated</b>.
/// </para>
/// </summary>
internal sealed class RetainedTree(VirtualNodeTree tree)
{
    private const int StackDirtyCapacity = 32;
    private const int StackParentIndexCapacity = 64;

    private VirtualNodeTree _tree = tree;

    /// <summary>The current retained tree.</summary>
    public VirtualNodeTree Tree => _tree;

    /// <summary>
    /// Apply all patches in the batch in a single DFS pass.
    /// Returns the dirty set and previous tree state for classification.
    /// </summary>
    public ApplyResult Apply(PatchBatch batch)
    {
        var previousTree = _tree;
        if (batch.HasCanonicalRoot)
        {
            return ApplyCanonicalRootBatch(batch, previousTree);
        }

        if (batch.Count == 0)
        {
            return new ApplyResult([], previousTree);
        }

        throw new InvalidOperationException("RetainedTree only accepts canonical diff batches.");
    }

    private ApplyResult ApplyCanonicalRootBatch(PatchBatch batch, VirtualNodeTree previousTree)
    {
        _tree = batch.Tree;
        if (batch.Count == 0)
        {
            return new ApplyResult([], previousTree);
        }

        var memory = batch.Memory.Span;
        var scratch = new FrameScratchArena();
        Span<int> dirtyStorage = stackalloc int[Math.Min(memory.Length, StackDirtyCapacity)];
        Span<NodeIndexEntry> nextParentIndexStorage = stackalloc NodeIndexEntry[StackParentIndexCapacity];
        Span<NodeIndexEntry> previousParentIndexStorage = stackalloc NodeIndexEntry[StackParentIndexCapacity];
        var dirty = scratch.CreateIntList(dirtyStorage);
        var nextParentIndex = scratch.CreateList(nextParentIndexStorage);
        var previousParentIndex = scratch.CreateList(previousParentIndexStorage);
        try
        {
            var needsNextParentIndex = false;
            var needsPreviousParentIndex = false;
            for (var i = 0; i < memory.Length; i++)
            {
                if (memory[i].Operation == VirtualNodePatchOperation.Add)
                {
                    needsNextParentIndex = true;
                }
                else if (memory[i].Operation == VirtualNodePatchOperation.Remove)
                {
                    needsPreviousParentIndex = true;
                }
            }

            if (needsNextParentIndex)
            {
                BuildParentIndexTable(batch.Tree, ref nextParentIndex);
            }

            if (needsPreviousParentIndex)
            {
                BuildParentIndexTable(previousTree, ref previousParentIndex);
            }

            for (var i = 0; i < memory.Length; i++)
            {
                var patch = memory[i];
                dirty.Add(patch.Operation switch
                {
                    VirtualNodePatchOperation.Update => patch.NodeIndex,
                    VirtualNodePatchOperation.ReplaceRoot => patch.NodeIndex,
                    VirtualNodePatchOperation.Add => FindParentIndex(nextParentIndex.Written, patch.NodeIndex),
                    VirtualNodePatchOperation.Remove => FindParentIndex(previousParentIndex.Written, patch.NodeIndex),
                    _ => patch.NodeIndex
                });
            }

            return new ApplyResult(SortAndDeduplicateDirty(ref dirty), previousTree);
        }
        finally
        {
            dirty.Dispose();
            nextParentIndex.Dispose();
            previousParentIndex.Dispose();
        }
    }

    private static IReadOnlyList<int> SortAndDeduplicateDirty(ref ScratchList<int> dirty)
    {
        if (dirty.Count == 0)
        {
            return [];
        }

        dirty.Sort();
        var span = dirty.WrittenMutable;
        var write = 1;
        for (var read = 1; read < span.Length; read++)
        {
            if (span[read] != span[write - 1])
            {
                span[write++] = span[read];
            }
        }

        if (write == span.Length)
        {
            return dirty.ToArray();
        }

        var result = new int[write];
        span[..write].CopyTo(result);
        return result;
    }

    private static void BuildParentIndexTable(VirtualNodeTree tree, ref ScratchList<NodeIndexEntry> table)
    {
        table.Add(new NodeIndexEntry(0, 0));
        BuildParentIndexTableRecursive(tree.CreateReader().Root, 0, ref table);
    }

    private static int BuildParentIndexTableRecursive(VirtualNodeReader node, int currentIndex, ref ScratchList<NodeIndexEntry> table)
    {
        var nextIndex = currentIndex + 1;
        for (var i = 0; i < node.ChildCount; i++)
        {
            var childIndex = nextIndex;
            var child = node.GetChild(i, childIndex);
            table.Add(new NodeIndexEntry(childIndex, currentIndex));
            nextIndex += BuildParentIndexTableRecursive(child, childIndex, ref table);
        }

        return nextIndex - currentIndex;
    }

    private static int FindParentIndex(ReadOnlySpan<NodeIndexEntry> table, int targetIndex)
    {
        if (targetIndex <= 0 || table.IsEmpty)
        {
            return 0;
        }

        var left = 0;
        var right = table.Length - 1;
        while (left <= right)
        {
            var mid = left + ((right - left) >> 1);
            var entry = table[mid];
            if (entry.DfsIndex == targetIndex)
            {
                return entry.ParentDfsIndex;
            }

            if (entry.DfsIndex < targetIndex)
            {
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return 0;
    }
}
