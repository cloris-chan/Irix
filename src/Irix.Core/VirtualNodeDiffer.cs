namespace Irix;

public static class VirtualNodeDiffer
{
    public static PatchBatch CreatePatchBatch(VirtualNodeTree previousTree, VirtualNodeTree nextTree, int screenId = 0)
    {
        // Empty → empty: no patches
        var prevEmpty = IsDefaultTree(previousTree);
        var nextEmpty = IsDefaultTree(nextTree);
        if (prevEmpty && nextEmpty)
        {
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId);
        }

        // Empty → something or something → empty: ReplaceRoot
        if (prevEmpty || nextEmpty)
        {
            if (NodesEqual(previousTree.Root, nextTree.Root))
            {
                return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId);
            }
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>(
                [new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, 0, nextTree.Root, screenId)]), 1, screenId);
        }

        // Both non-empty: local diff
        var patches = new List<VirtualNodePatch>();
        DiffNode(previousTree.Root, nextTree.Root, 0, patches);

        if (patches.Count == 0)
        {
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId);
        }

        for (var i = 0; i < patches.Count; i++)
        {
            patches[i] = patches[i] with { ScreenId = screenId };
        }

        return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([.. patches]), patches.Count, screenId);
    }

    private static bool IsDefaultTree(VirtualNodeTree tree)
    {
        var root = tree.Root;
        return root.Kind == default && root.Key == 0 && root.Content == default
            && (root.Attributes == null || root.Attributes.Length == 0)
            && (root.Children == null || root.Children.Length == 0);
    }

    private static void DiffNode(VirtualNode oldNode, VirtualNode newNode, int nodeIndex, List<VirtualNodePatch> patches)
    {
        // Different kind → can't do incremental, replace whole subtree
        if (oldNode.Kind != newNode.Kind)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, nodeIndex, newNode));
            return;
        }

        // Same kind → check if content or attributes changed
        var contentChanged = oldNode.Content != newNode.Content;
        var attributesChanged = !AttributesEqual(oldNode.Attributes ?? [], newNode.Attributes ?? []);

        if (contentChanged || attributesChanged)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Update, nodeIndex, newNode));
        }

        // Diff children with keyed reconciliation
        DiffChildren(oldNode.Children ?? [], newNode.Children ?? [], nodeIndex, patches);
    }

    private static void DiffChildren(VirtualNode[] oldChildren, VirtualNode[] newChildren, int parentIndex, List<VirtualNodePatch> patches)
    {
        // Fast path: both empty
        if (oldChildren.Length == 0 && newChildren.Length == 0) return;

        // Check if children use keys
        var hasKeys = false;
        for (var i = 0; i < newChildren.Length; i++)
        {
            if (newChildren[i].Key != 0)
            {
                hasKeys = true;
                break;
            }
        }

        if (!hasKeys)
        {
            // No keys → index-based comparison
            DiffChildrenByIndex(oldChildren, newChildren, parentIndex, patches);
            return;
        }

        // Keyed reconciliation
        DiffChildrenByKeyed(oldChildren, newChildren, parentIndex, patches);
    }

    private static void DiffChildrenByIndex(VirtualNode[] oldChildren, VirtualNode[] newChildren, int parentIndex, List<VirtualNodePatch> patches)
    {
        var minLen = Math.Min(oldChildren.Length, newChildren.Length);
        var childOffset = 0;

        // Diff common children
        for (var i = 0; i < minLen; i++)
        {
            var childIndex = parentIndex + 1 + childOffset;
            DiffNode(oldChildren[i], newChildren[i], childIndex, patches);
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

    private static void DiffChildrenByKeyed(VirtualNode[] oldChildren, VirtualNode[] newChildren, int parentIndex, List<VirtualNodePatch> patches)
    {
        // Build key → index map for old children
        var oldKeyMap = new Dictionary<ulong, int>(oldChildren.Length);
        for (var i = 0; i < oldChildren.Length; i++)
        {
            if (oldChildren[i].Key != 0)
            {
                oldKeyMap[oldChildren[i].Key] = i;
            }
        }

        var childOffset = 0;
        for (var i = 0; i < newChildren.Length; i++)
        {
            var newChild = newChildren[i];
            var childIndex = parentIndex + 1 + childOffset;

            if (newChild.Key != 0 && oldKeyMap.TryGetValue(newChild.Key, out var oldIdx))
            {
                // Same key → diff recursively
                DiffNode(oldChildren[oldIdx], newChild, childIndex, patches);
            }
            else
            {
                // New key → Add
                patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Add, childIndex, newChild));
            }

            childOffset += CountNodes(newChild);
        }

        // Remove old children whose keys are not in new
        var newKeySet = new HashSet<ulong>();
        for (var i = 0; i < newChildren.Length; i++)
        {
            if (newChildren[i].Key != 0) newKeySet.Add(newChildren[i].Key);
        }

        for (var i = 0; i < oldChildren.Length; i++)
        {
            if (oldChildren[i].Key != 0 && !newKeySet.Contains(oldChildren[i].Key))
            {
                patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Remove, parentIndex + 1 + childOffset, oldChildren[i]));
                childOffset += CountNodes(oldChildren[i]);
            }
        }
    }

    private static int CountNodes(VirtualNode node)
    {
        var count = 1;
        var children = node.Children ?? [];
        for (var i = 0; i < children.Length; i++)
        {
            count += CountNodes(children[i]);
        }
        return count;
    }

    private static bool AttributesEqual(VirtualNodeAttribute[] a, VirtualNodeAttribute[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    internal static bool NodesEqual(VirtualNode a, VirtualNode b)
    {
        if (a.Kind != b.Kind || a.Key != b.Key || a.Content != b.Content)
        {
            return false;
        }

        var aAttrs = a.Attributes ?? [];
        var bAttrs = b.Attributes ?? [];
        if (aAttrs.Length != bAttrs.Length)
        {
            return false;
        }

        for (var i = 0; i < aAttrs.Length; i++)
        {
            if (aAttrs[i] != bAttrs[i])
            {
                return false;
            }
        }

        var aChildren = a.Children ?? [];
        var bChildren = b.Children ?? [];
        if (aChildren.Length != bChildren.Length)
        {
            return false;
        }

        for (var i = 0; i < aChildren.Length; i++)
        {
            if (!NodesEqual(aChildren[i], bChildren[i]))
            {
                return false;
            }
        }

        return true;
    }
}
