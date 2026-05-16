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
            if (NodesEqual(prevRoot, nextTree.Root, prevSnapshot, nextSnapshot))
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
            && root.Properties.Count == 0
            && root.Children.Count == 0;
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

    private static void DiffChildren(IReadOnlyList<VirtualNode> oldChildren, IReadOnlyList<VirtualNode> newChildren, int parentIndex, List<VirtualNodePatch> patches, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        // Fast path: both empty
        if (oldChildren.Count == 0 && newChildren.Count == 0) return;

        // Check if children use keys
        var hasKeys = false;
        for (var i = 0; i < newChildren.Count; i++)
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

    private static void DiffChildrenByIndex(IReadOnlyList<VirtualNode> oldChildren, IReadOnlyList<VirtualNode> newChildren, int parentIndex, List<VirtualNodePatch> patches, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        var minLen = Math.Min(oldChildren.Count, newChildren.Count);
        var childOffset = 0;

        // Diff common children
        for (var i = 0; i < minLen; i++)
        {
            var childIndex = parentIndex + 1 + childOffset;
            DiffNode(oldChildren[i], newChildren[i], childIndex, patches, prevSnapshot, nextSnapshot);
            childOffset += CountNodes(oldChildren[i]);
        }

        // Remove extra old children
        for (var i = minLen; i < oldChildren.Count; i++)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Remove, parentIndex + 1 + childOffset, oldChildren[i]));
            childOffset += CountNodes(oldChildren[i]);
        }

        // Add new children
        for (var i = minLen; i < newChildren.Count; i++)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Add, parentIndex + 1 + childOffset, newChildren[i]));
            childOffset += CountNodes(newChildren[i]);
        }
    }

    private static void DiffChildrenByKeyed(IReadOnlyList<VirtualNode> oldChildren, IReadOnlyList<VirtualNode> newChildren, int parentIndex, List<VirtualNodePatch> patches, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        // Build key → index map for old children
        var oldKeyMap = new Dictionary<NodeKey, int>(oldChildren.Count);
        for (var i = 0; i < oldChildren.Count; i++)
        {
            if (oldChildren[i].Key != NodeKey.None)
            {
                oldKeyMap[oldChildren[i].Key] = i;
            }
        }

        var childOffset = 0;
        for (var i = 0; i < newChildren.Count; i++)
        {
            var newChild = newChildren[i];
            var childIndex = parentIndex + 1 + childOffset;

            if (newChild.Key != NodeKey.None && oldKeyMap.TryGetValue(newChild.Key, out var oldIdx))
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
        var newKeySet = new HashSet<NodeKey>();
        for (var i = 0; i < newChildren.Count; i++)
        {
            if (newChildren[i].Key != NodeKey.None) newKeySet.Add(newChildren[i].Key);
        }

        for (var i = 0; i < oldChildren.Count; i++)
        {
            if (oldChildren[i].Key != NodeKey.None && !newKeySet.Contains(oldChildren[i].Key))
            {
                patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Remove, parentIndex + 1 + childOffset, oldChildren[i]));
                childOffset += CountNodes(oldChildren[i]);
            }
        }
    }

    private static int CountNodes(VirtualNode node)
    {
        var count = 1;
        var children = node.Children;
        for (var i = 0; i < children.Count; i++)
        {
            count += CountNodes(children[i]);
        }
        return count;
    }

    private static bool PropertiesEqual(IReadOnlyList<VirtualNodeProperty> a, IReadOnlyList<VirtualNodeProperty> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    internal static bool NodesEqual(VirtualNode a, VirtualNode b, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        if (a.Kind != b.Kind || a.Key != b.Key)
        {
            return false;
        }

        if (!ContentEqual(a.Content, b.Content, prevSnapshot, nextSnapshot))
        {
            return false;
        }

        var aProperties = a.Properties;
        var bProperties = b.Properties;
        if (aProperties.Count != bProperties.Count)
        {
            return false;
        }

        for (var i = 0; i < aProperties.Count; i++)
        {
            if (aProperties[i] != bProperties[i])
            {
                return false;
            }
        }

        var aChildren = a.Children;
        var bChildren = b.Children;
        if (aChildren.Count != bChildren.Count)
        {
            return false;
        }

        for (var i = 0; i < aChildren.Count; i++)
        {
            if (!NodesEqual(aChildren[i], bChildren[i], prevSnapshot, nextSnapshot))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool ContentEqual(NodeContent a, NodeContent b, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        if (a == b) return true;
        if (a.TryGetText(out var aText) && b.TryGetText(out var bText))
        {
            if (prevSnapshot is not { } ps || nextSnapshot is not { } ns) return false;
            var aSpan = ps.ResolveRequired(aText);
            var bSpan = ns.ResolveRequired(bText);
            if (aSpan.IsEmpty || bSpan.IsEmpty) return false;
            return aSpan.SequenceEqual(bSpan);
        }
        return false;
    }
}
