namespace Irix;

public static class VirtualNodeDiffer
{
    public static PatchBatch CreatePatchBatch(VirtualNodeTree previousTree, VirtualNodeTree nextTree, int screenId = 0)
    {
        // Empty → empty: no patches
        var prevEmpty = IsDefaultTree(previousTree);
        var nextEmpty = IsDefaultTree(nextTree);
        var nextSnapshot = nextTree.TextSnapshot;
        var prevSnapshot = previousTree.TextSnapshot;
        var prevRoot = previousTree.Root;
        if (prevEmpty && nextEmpty)
        {
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
        }

        // Empty → something or something → empty: ReplaceRoot
        if (prevEmpty || nextEmpty)
        {
            if (VirtualNodeStructuralComparer.Equals(prevRoot, nextTree.Root, prevSnapshot, nextSnapshot))
            {
                return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
            }
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>(
                [new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, 0, nextTree.Root, screenId)]), 1, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
        }

        // Both non-empty: local diff
        var patches = new List<VirtualNodePatch>();
        DiffNode(prevRoot, nextTree.Root, 0, patches, prevSnapshot, nextSnapshot);

        if (patches.Count == 0)
        {
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
        }

        for (var i = 0; i < patches.Count; i++)
        {
            patches[i] = patches[i].WithScreenId(screenId);
        }

        return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([.. patches]), patches.Count, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
    }

    private static bool IsDefaultTree(VirtualNodeTree tree)
    {
        var root = tree.Root;
        return root.Kind == VirtualNodeKind.None && root.Key == NodeKey.None && root.Content == default
            && root.Properties.Length == 0
            && root.Children.Length == 0;
    }

    private static void DiffNode(VirtualNode oldNode, VirtualNode newNode, int nodeIndex, List<VirtualNodePatch> patches, TextBufferSnapshot? prevSnapshot = null, TextBufferSnapshot? nextSnapshot = null)
    {
        // Different kind → can't do incremental, replace whole subtree
        if (oldNode.Kind != newNode.Kind)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, nodeIndex, newNode));
            return;
        }

        // Same kind → check if content or properties changed
        var contentChanged = !ContentEqual(oldNode.Content, newNode.Content, prevSnapshot, nextSnapshot);
        var propertiesChanged = !PropertiesEqual(oldNode.Properties, newNode.Properties);

        if (contentChanged || propertiesChanged)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Update, nodeIndex, newNode));
        }

        // Diff children with keyed reconciliation
        DiffChildren(oldNode.Children, newNode.Children, nodeIndex, patches, prevSnapshot, nextSnapshot);
    }

    private static void DiffChildren(ReadOnlySpan<VirtualNode> oldChildren, ReadOnlySpan<VirtualNode> newChildren, int parentIndex, List<VirtualNodePatch> patches, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        // Fast path: both empty
        if (oldChildren.Length == 0 && newChildren.Length == 0) return;

        // Check if children use keys
        var hasKeys = false;
        for (var i = 0; i < newChildren.Length; i++)
        {
            if (newChildren[i].Key != NodeKey.None)
            {
                hasKeys = true;
                break;
            }
        }

        if (!hasKeys)
        {
            // No keys → index-based comparison
            DiffChildrenByIndex(oldChildren, newChildren, parentIndex, patches, prevSnapshot, nextSnapshot);
            return;
        }

        // Keyed reconciliation
        DiffChildrenByKeyed(oldChildren, newChildren, parentIndex, patches, prevSnapshot, nextSnapshot);
    }

    private static void DiffChildrenByIndex(ReadOnlySpan<VirtualNode> oldChildren, ReadOnlySpan<VirtualNode> newChildren, int parentIndex, List<VirtualNodePatch> patches, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        var minLen = Math.Min(oldChildren.Length, newChildren.Length);
        var childOffset = 0;

        // Diff common children
        for (var i = 0; i < minLen; i++)
        {
            var childIndex = parentIndex + 1 + childOffset;
            DiffNode(oldChildren[i], newChildren[i], childIndex, patches, prevSnapshot, nextSnapshot);
            childOffset += CountNodes(oldChildren[i]);
        }

        // Remove extra old children
        for (var i = minLen; i < oldChildren.Length; i++)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Remove, parentIndex + 1 + childOffset, oldChildren[i]));
            childOffset += CountNodes(oldChildren[i]);
        }

        // Add new children
        for (var i = minLen; i < newChildren.Length; i++)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Add, parentIndex + 1 + childOffset, newChildren[i]));
            childOffset += CountNodes(newChildren[i]);
        }
    }

    private static void DiffChildrenByKeyed(ReadOnlySpan<VirtualNode> oldChildren, ReadOnlySpan<VirtualNode> newChildren, int parentIndex, List<VirtualNodePatch> patches, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        var childOffset = 0;
        for (var i = 0; i < newChildren.Length; i++)
        {
            var newChild = newChildren[i];
            var childIndex = parentIndex + 1 + childOffset;

            if (newChild.Key != NodeKey.None && TryFindChildIndexByKey(oldChildren, newChild.Key, out var oldIdx))
            {
                // Same key → diff recursively
                DiffNode(oldChildren[oldIdx], newChild, childIndex, patches, prevSnapshot, nextSnapshot);
            }
            else
            {
                // New key → Add
                patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Add, childIndex, newChild));
            }

            childOffset += CountNodes(newChild);
        }

        // Remove old children whose keys are not in new
        for (var i = 0; i < oldChildren.Length; i++)
        {
            if (oldChildren[i].Key != NodeKey.None && !ContainsChildKey(newChildren, oldChildren[i].Key))
            {
                patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Remove, parentIndex + 1 + childOffset, oldChildren[i]));
                childOffset += CountNodes(oldChildren[i]);
            }
        }
    }

    private static bool TryFindChildIndexByKey(ReadOnlySpan<VirtualNode> children, NodeKey key, out int index)
    {
        // Match the old Dictionary assignment behavior: duplicate keys resolve to the last child.
        for (var i = children.Length - 1; i >= 0; i--)
        {
            if (children[i].Key == key)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool ContainsChildKey(ReadOnlySpan<VirtualNode> children, NodeKey key)
    {
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i].Key == key)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountNodes(VirtualNode node)
    {
        var count = 1;
        var children = node.Children;
        for (var i = 0; i < children.Length; i++)
        {
            count += CountNodes(children[i]);
        }
        return count;
    }

    private static bool PropertiesEqual(ReadOnlySpan<VirtualNodeProperty> a, ReadOnlySpan<VirtualNodeProperty> b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    internal static bool ContentEqual(NodeContent a, NodeContent b, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        if (a == b) return true;
        if (a.TryGetText(out var aText) && b.TryGetText(out var bText))
        {
            if (prevSnapshot is not { } ps || nextSnapshot is not { } ns) return false;
            if (!ps.TryResolve(aText, out var aSpan) || !ns.TryResolve(bText, out var bSpan)) return false;
            return aSpan.SequenceEqual(bSpan);
        }
        return false;
    }
}

internal static class VirtualNodeStructuralComparer
{
    public static bool Equals(VirtualNode a, VirtualNode b, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        if (a.Kind != b.Kind || a.Key != b.Key)
        {
            return false;
        }

        if (!VirtualNodeDiffer.ContentEqual(a.Content, b.Content, prevSnapshot, nextSnapshot))
        {
            return false;
        }

        var aProperties = a.Properties;
        var bProperties = b.Properties;
        if (aProperties.Length != bProperties.Length)
        {
            return false;
        }

        for (var i = 0; i < aProperties.Length; i++)
        {
            if (aProperties[i] != bProperties[i])
            {
                return false;
            }
        }

        var aChildren = a.Children;
        var bChildren = b.Children;
        if (aChildren.Length != bChildren.Length)
        {
            return false;
        }

        for (var i = 0; i < aChildren.Length; i++)
        {
            if (!Equals(aChildren[i], bChildren[i], prevSnapshot, nextSnapshot))
            {
                return false;
            }
        }

        return true;
    }
}
