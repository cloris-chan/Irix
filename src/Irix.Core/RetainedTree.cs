namespace Irix;

/// <summary>
/// Result of <see cref="RetainedTree.Apply"/>, containing the dirty node set
/// and the previous tree state for dirty classification.
/// </summary>
public readonly struct ApplyResult(
    IReadOnlyList<int> Dirty,
    VirtualNode PreviousRoot,
    TextBufferSnapshot PreviousTextSnapshot) : IEquatable<ApplyResult>
{
    public IReadOnlyList<int> Dirty { get; } = Dirty;
    public VirtualNode PreviousRoot { get; } = PreviousRoot;
    public TextBufferSnapshot PreviousTextSnapshot { get; } = PreviousTextSnapshot;

    public bool Equals(ApplyResult other)
    {
        return EqualityComparer<IReadOnlyList<int>>.Default.Equals(Dirty, other.Dirty)
            && PreviousRoot.Equals(other.PreviousRoot)
            && PreviousTextSnapshot.Equals(other.PreviousTextSnapshot);
    }

    public override bool Equals(object? obj) => obj is ApplyResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Dirty, PreviousRoot, PreviousTextSnapshot);

    public static bool operator ==(ApplyResult left, ApplyResult right) => left.Equals(right);

    public static bool operator !=(ApplyResult left, ApplyResult right) => !left.Equals(right);
}

/// <summary>
/// Applies <see cref="PatchBatch"/> patches to a <see cref="VirtualNode"/> tree,
/// producing a new immutable tree. Uses a single DFS pass to avoid index drift
/// from sequential patch application.
///
/// <para><b>Patch semantics:</b></para>
/// <list type="bullet">
///   <item><b>Canonical root</b> — when <see cref="PatchBatch.HasCanonicalRoot"/> is true,
///     <see cref="PatchBatch.Root"/> is the canonical next retained root and wins over the
///     reconstructed patch result.</item>
///   <item><b>ReplaceRoot</b> — replaces the entire root node (and all children) with the patch node.</item>
///   <item><b>Update</b> — replaces the <em>content and properties</em> of the target node, but
///     <em>preserves its existing children</em> from the old tree. The patch node's Children are ignored.</item>
///   <item><b>Add</b> — inserts a new node at the DFS position specified by <c>NodeIndex</c>.
///     The index is in the <em>old tree's</em> DFS coordinate system (before this batch is applied).</item>
///   <item><b>Remove</b> — removes the node at the DFS position specified by <c>NodeIndex</c>.
///     For keyed nodes (Key ≠ 0), matching is by key; for unkeyed nodes, by DFS index.</item>
/// </list>
///
/// <para><b>Dirty index semantics:</b></para>
/// <para>
/// The returned dirty set contains the DFS indices of parent nodes whose <em>children list</em>
/// changed (Add/Remove), or the node itself whose content changed (Update/ReplaceRoot).
/// All indices refer to the <em>result tree's</em> DFS ordering. The output is
/// <b>sorted ascending and deduplicated</b>.
/// </para>
/// </summary>
public sealed class RetainedTree(VirtualNodeTree tree)
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
        var prevRoot = _tree.Root;
        var prevSnapshot = _tree.TextSnapshot;
        if (batch.HasCanonicalRoot)
        {
            return ApplyCanonicalRootBatch(batch, prevRoot, prevSnapshot);
        }

        if (batch.Count == 0)
        {
            return new ApplyResult([], prevRoot, prevSnapshot);
        }

        // Legacy manual patch fallback for hand-authored tests/tools. Runtime diff batches
        // carry HasCanonicalRoot=true and take the scratch-backed canonical path above.
        var dirty = new List<int>();
        var memory = batch.Memory.Span;
        var replacePatches = new Dictionary<int, VirtualNode>();
        var updatePatches = new Dictionary<int, VirtualNode>();
        var addPatches = new Dictionary<int, List<VirtualNode>>();
        var removeKeySet = new HashSet<NodeKey>();
        var removeIndexSet = new HashSet<int>();

        for (var i = 0; i < memory.Length; i++)
        {
            var patch = memory[i];
            switch (patch.Operation)
            {
                case VirtualNodePatchOperation.ReplaceRoot:
                    replacePatches[patch.NodeIndex] = patch.Node; break;
                case VirtualNodePatchOperation.Update:
                    updatePatches[patch.NodeIndex] = patch.Node; break;
                case VirtualNodePatchOperation.Add:
                    if (!addPatches.TryGetValue(patch.NodeIndex, out var addList)) { addList = []; addPatches[patch.NodeIndex] = addList; }
                    addList.Add(patch.Node); break;
                case VirtualNodePatchOperation.Remove:
                    if (patch.Node.Key != NodeKey.None) removeKeySet.Add(patch.Node.Key);
                    else removeIndexSet.Add(patch.NodeIndex); break;
            }
        }

        if (replacePatches.TryGetValue(0, out VirtualNode value))
        {
            _tree = new VirtualNodeTree(value, batch.TextSnapshot);
            return new ApplyResult([0], prevRoot, prevSnapshot);
        }

        var appliedRoot = ApplyRecursive(_tree.Root, 0, updatePatches, addPatches, removeKeySet, removeIndexSet, dirty, out _);
        _tree = new VirtualNodeTree(appliedRoot, batch.TextSnapshot);
        return new ApplyResult(SortAndDeduplicateDirty(dirty), prevRoot, prevSnapshot);
    }

    private ApplyResult ApplyCanonicalRootBatch(PatchBatch batch, VirtualNode prevRoot, TextBufferSnapshot prevSnapshot)
    {
        _tree = new VirtualNodeTree(batch.Root, batch.TextSnapshot);
        if (batch.Count == 0)
        {
            return new ApplyResult([], prevRoot, prevSnapshot);
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
                BuildParentIndexTable(batch.Root, ref nextParentIndex);
            }

            if (needsPreviousParentIndex)
            {
                BuildParentIndexTable(prevRoot, ref previousParentIndex);
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

            return new ApplyResult(SortAndDeduplicateDirty(ref dirty), prevRoot, prevSnapshot);
        }
        finally
        {
            dirty.Dispose();
            nextParentIndex.Dispose();
            previousParentIndex.Dispose();
        }
    }

    private static VirtualNode ApplyRecursive(
        VirtualNode node, int currentIndex,
        Dictionary<int, VirtualNode> updates,
        Dictionary<int, List<VirtualNode>> adds,
        HashSet<NodeKey> removeKeySet, HashSet<int> removeIndexSet,
        List<int> dirty)
    {
        return ApplyRecursive(node, currentIndex, updates, adds, removeKeySet, removeIndexSet, dirty, out _);
    }

    private static VirtualNode ApplyRecursive(
        VirtualNode node, int currentIndex,
        Dictionary<int, VirtualNode> updates,
        Dictionary<int, List<VirtualNode>> adds,
        HashSet<NodeKey> removeKeySet, HashSet<int> removeIndexSet,
        List<int> dirty,
        out bool changed)
    {
        changed = false;
        if (updates.Remove(currentIndex, out var replacement))
        {
            dirty.Add(currentIndex);
            node = new VirtualNode(replacement.Kind, replacement.Key, replacement.Content, replacement.Properties, node.Children);
            changed = true;
        }

        var oldChildren = node.Children;
        var extraCapacity = 0;
        foreach (var kvp in adds) extraCapacity += kvp.Value.Count;
        var newChildren = new List<VirtualNode>(oldChildren.Length + extraCapacity);
        var offset = currentIndex + 1;

        for (var i = 0; i < oldChildren.Length; i++)
        {
            var child = oldChildren[i];
            var childSize = CountNodes(child);
            var childEnd = offset + childSize;

            if (adds.Remove(offset, out var addNodes))
            {
                newChildren.AddRange(addNodes);
                dirty.Add(currentIndex);
                changed = true;
            }

            var shouldRemove = (child.Key != NodeKey.None && removeKeySet.Remove(child.Key))
                             || removeIndexSet.Remove(offset);
            if (shouldRemove)
            {
                dirty.Add(currentIndex);
                changed = true;
            }
            else
            {
                var newChild = ApplyRecursive(child, offset, updates, adds, removeKeySet, removeIndexSet, dirty, out var childChanged);
                newChildren.Add(newChild);
                changed |= childChanged;
            }

            if (adds.Remove(childEnd, out var addAfterNodes))
            {
                newChildren.AddRange(addAfterNodes);
                dirty.Add(currentIndex);
                changed = true;
            }

            offset = childEnd;
        }

        if (oldChildren.Length > 0)
        {
            if (adds.Count > 0)
            {
                var remainingAdds = adds.Where(kvp => kvp.Key >= offset).OrderBy(kvp => kvp.Key).ToList();
                foreach (var kvp in remainingAdds)
                {
                    newChildren.AddRange(kvp.Value);
                    dirty.Add(currentIndex);
                    adds.Remove(kvp.Key);
                    changed = true;
                }
            }

            if (removeKeySet.Count > 0 || removeIndexSet.Count > 0)
            {
                dirty.Add(currentIndex);
                removeKeySet.Clear();
                removeIndexSet.Clear();
                changed = true;
            }
        }

        if (newChildren.Count != oldChildren.Length)
        {
            changed = true;
            return new VirtualNode(node.Kind, node.Key, node.Content, node.Properties, [.. newChildren]);
        }

        return changed
            ? new VirtualNode(node.Kind, node.Key, node.Content, node.Properties, [.. newChildren])
            : node;
    }

    private static int CountNodes(VirtualNode node)
    {
        var count = 1;
        var children = node.Children;
        for (var i = 0; i < children.Length; i++) count += CountNodes(children[i]);
        return count;
    }

    private static IReadOnlyList<int> SortAndDeduplicateDirty(List<int> dirty)
    {
        if (dirty.Count == 0)
        {
            return [];
        }

        dirty.Sort();
        var write = 1;
        for (var read = 1; read < dirty.Count; read++)
        {
            if (dirty[read] != dirty[write - 1])
            {
                dirty[write++] = dirty[read];
            }
        }

        if (write == dirty.Count)
        {
            return dirty;
        }

        return dirty.GetRange(0, write);
    }

    private static IReadOnlyList<int> SortAndDeduplicateDirty(int[] dirty)
    {
        if (dirty.Length == 0)
        {
            return [];
        }

        Array.Sort(dirty);
        var write = 1;
        for (var read = 1; read < dirty.Length; read++)
        {
            if (dirty[read] != dirty[write - 1])
            {
                dirty[write++] = dirty[read];
            }
        }

        if (write == dirty.Length)
        {
            return dirty;
        }

        var result = new int[write];
        Array.Copy(dirty, result, write);
        return result;
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

    private static void BuildParentIndexTable(VirtualNode root, ref ScratchList<NodeIndexEntry> table)
    {
        table.Add(new NodeIndexEntry(0, 0));
        BuildParentIndexTableRecursive(root, 0, ref table);
    }

    private static int BuildParentIndexTableRecursive(VirtualNode node, int currentIndex, ref ScratchList<NodeIndexEntry> table)
    {
        var nextIndex = currentIndex + 1;
        var children = node.Children;
        for (var i = 0; i < children.Length; i++)
        {
            var childIndex = nextIndex;
            table.Add(new NodeIndexEntry(childIndex, currentIndex));
            nextIndex += BuildParentIndexTableRecursive(children[i], childIndex, ref table);
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
