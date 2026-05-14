namespace Irix;

/// <summary>
/// Applies <see cref="PatchBatch"/> patches to a <see cref="VirtualNode"/> tree,
/// producing a new immutable tree. Uses a single DFS pass to avoid index drift
/// from sequential patch application.
///
/// <para><b>Patch semantics:</b></para>
/// <list type="bullet">
///   <item><b>ReplaceRoot</b> — replaces the entire root node (and all children) with the patch node.</item>
///   <item><b>Update</b> — replaces the <em>content and attributes</em> of the target node, but
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
    private VirtualNodeTree _tree = tree;


    /// <summary>The current retained tree.</summary>
    public VirtualNodeTree Tree => _tree;

    /// <summary>
    /// Apply all patches in the batch in a single DFS pass.
    /// Returns sorted, deduplicated DFS node indices that were affected.
    /// </summary>
    public IReadOnlyList<int> Apply(PatchBatch batch)
    {
        if (batch.Count == 0)
        {
            if (batch.Root.Kind != default) _tree = new VirtualNodeTree(batch.Root, batch.TextSnapshot);
            return [];
        }

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

        if (replacePatches.ContainsKey(0))
        {
            _tree = new VirtualNodeTree(replacePatches[0], batch.TextSnapshot);
            return [0];
        }

        _tree = new VirtualNodeTree(ApplyRecursive(_tree.Root, 0, updatePatches, addPatches, removeKeySet, removeIndexSet, dirty), batch.TextSnapshot);
        dirty.Sort();
        var deduped = new List<int>(dirty.Count);
        var last = -1;
        foreach (var d in dirty)
        {
            if (d != last) { deduped.Add(d); last = d; }
        }
        return deduped;
    }

    private static VirtualNode ApplyRecursive(
        VirtualNode node, int currentIndex,
        Dictionary<int, VirtualNode> updates,
        Dictionary<int, List<VirtualNode>> adds,
        HashSet<NodeKey> removeKeySet, HashSet<int> removeIndexSet,
        List<int> dirty)
    {
        if (updates.Remove(currentIndex, out var replacement))
        {
            dirty.Add(currentIndex);
            node = new VirtualNode(replacement.Kind, replacement.Key, replacement.Content, replacement.Attributes, node.Children);
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
            }

            var shouldRemove = (child.Key != NodeKey.None && removeKeySet.Remove(child.Key))
                             || removeIndexSet.Remove(offset);
            if (shouldRemove)
            {
                dirty.Add(currentIndex);
            }
            else
            {
                newChildren.Add(ApplyRecursive(child, offset, updates, adds, removeKeySet, removeIndexSet, dirty));
            }

            if (adds.Remove(childEnd, out var addAfterNodes))
            {
                newChildren.AddRange(addAfterNodes);
                dirty.Add(currentIndex);
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
                }
            }

            if (removeKeySet.Count > 0 || removeIndexSet.Count > 0)
            {
                dirty.Add(currentIndex);
                removeKeySet.Clear();
                removeIndexSet.Clear();
            }
        }

        if (newChildren.Count != oldChildren.Length)
            return new VirtualNode(node.Kind, node.Key, node.Content, node.Attributes, newChildren.ToArray());

        var changed = false;
        for (var i = 0; i < newChildren.Count; i++)
        {
            if (newChildren[i] != oldChildren[i]) { changed = true; break; }
        }

        return changed
            ? new VirtualNode(node.Kind, node.Key, node.Content, node.Attributes, newChildren.ToArray())
            : node;
    }

    private static int CountNodes(VirtualNode node)
    {
        var count = 1;
        var children = node.Children;
        for (var i = 0; i < children.Length; i++) count += CountNodes(children[i]);
        return count;
    }
}
