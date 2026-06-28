namespace Irix;

internal static class VirtualNodeDiffer
{
    private const int StackKeyMapEntryCapacity = 64;

    public static PatchBatch CreatePatchBatch(VirtualNodeTree previousTree, VirtualNodeTree nextTree, int screenId = 0)
    {
        var previousReader = previousTree.CreateReader();
        var nextReader = nextTree.CreateReader();

        // Empty → empty: no patches
        var prevEmpty = previousReader.IsDefault;
        var nextEmpty = nextReader.IsDefault;
        var nextSnapshot = nextTree.TextSnapshot;
        var prevSnapshot = previousTree.TextSnapshot;
        if (prevEmpty && nextEmpty)
        {
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
        }

        // Empty → something or something → empty: ReplaceRoot
        if (prevEmpty || nextEmpty)
        {
            if (VirtualNodeStructuralComparer.Equals(previousReader.Root, nextReader.Root, prevSnapshot, nextSnapshot))
            {
                return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
            }
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>(
                [new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, 0, nextTree.Root, screenId)]), 1, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
        }

        // Both non-empty: local diff
        var scratch = new FrameScratchArena();
        var patches = scratch.RentVirtualNodePatchList();
        try
        {
            DiffNode(previousReader.Root, nextReader.Root, ref patches, prevSnapshot, nextSnapshot, scratch);

            if (patches.Count == 0)
            {
                return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
            }

            for (var i = 0; i < patches.Count; i++)
            {
                patches[i] = patches[i].WithScreenId(screenId);
            }

            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>(patches.ToArray()), patches.Count, screenId, textSnapshot: nextSnapshot, hasCanonicalRoot: true);
        }
        finally
        {
            patches.Dispose();
        }
    }

    private static void DiffNode(
        VirtualNodeReader oldNode,
        VirtualNodeReader newNode,
        ref ScratchList<VirtualNodePatch> patches,
        TextBufferSnapshot? prevSnapshot,
        TextBufferSnapshot? nextSnapshot,
        FrameScratchArena scratch)
    {
        // Different kind → can't do incremental, replace whole subtree
        if (oldNode.Kind != newNode.Kind)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, newNode.DfsIndex, newNode.Node));
            return;
        }

        // Same kind → check if content or properties changed
        var contentChanged = !ContentEqual(oldNode.Content, newNode.Content, prevSnapshot, nextSnapshot);
        var propertiesChanged = !PropertiesEqual(oldNode.Properties, newNode.Properties);

        if (contentChanged || propertiesChanged)
        {
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Update, newNode.DfsIndex, newNode.Node));
        }

        // Diff children with keyed reconciliation
        DiffChildren(oldNode, newNode, ref patches, prevSnapshot, nextSnapshot, scratch);
    }

    private static void DiffChildren(
        VirtualNodeReader oldParent,
        VirtualNodeReader newParent,
        ref ScratchList<VirtualNodePatch> patches,
        TextBufferSnapshot? prevSnapshot,
        TextBufferSnapshot? nextSnapshot,
        FrameScratchArena scratch)
    {
        // Fast path: both empty
        if (oldParent.ChildCount == 0 && newParent.ChildCount == 0) return;

        // Check if children use keys
        var hasKeys = false;
        for (var i = 0; i < newParent.ChildCount; i++)
        {
            if (newParent.GetChildKey(i) != NodeKey.None)
            {
                hasKeys = true;
                break;
            }
        }

        if (!hasKeys)
        {
            // No keys → index-based comparison
            DiffChildrenByIndex(oldParent, newParent, ref patches, prevSnapshot, nextSnapshot, scratch);
            return;
        }

        // Keyed reconciliation
        DiffChildrenByKeyed(oldParent, newParent, ref patches, prevSnapshot, nextSnapshot, scratch);
    }

    private static void DiffChildrenByIndex(
        VirtualNodeReader oldParent,
        VirtualNodeReader newParent,
        ref ScratchList<VirtualNodePatch> patches,
        TextBufferSnapshot? prevSnapshot,
        TextBufferSnapshot? nextSnapshot,
        FrameScratchArena scratch)
    {
        var minLen = Math.Min(oldParent.ChildCount, newParent.ChildCount);
        var childOffset = 0;

        // Diff common children
        for (var i = 0; i < minLen; i++)
        {
            var childIndex = newParent.DfsIndex + 1 + childOffset;
            var oldChild = oldParent.GetChild(i, oldParent.DfsIndex + 1 + childOffset);
            var newChild = newParent.GetChild(i, childIndex);
            DiffNode(oldChild, newChild, ref patches, prevSnapshot, nextSnapshot, scratch);
            childOffset += oldChild.CountSubtreeNodes();
        }

        // Remove extra old children
        for (var i = minLen; i < oldParent.ChildCount; i++)
        {
            var childIndex = oldParent.DfsIndex + 1 + childOffset;
            var oldChild = oldParent.GetChild(i, childIndex);
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Remove, childIndex, oldChild.Node));
            childOffset += oldChild.CountSubtreeNodes();
        }

        // Add new children
        for (var i = minLen; i < newParent.ChildCount; i++)
        {
            var childIndex = newParent.DfsIndex + 1 + childOffset;
            var newChild = newParent.GetChild(i, childIndex);
            patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Add, childIndex, newChild.Node));
            childOffset += newChild.CountSubtreeNodes();
        }
    }

    private static void DiffChildrenByKeyed(
        VirtualNodeReader oldParent,
        VirtualNodeReader newParent,
        ref ScratchList<VirtualNodePatch> patches,
        TextBufferSnapshot? prevSnapshot,
        TextBufferSnapshot? nextSnapshot,
        FrameScratchArena scratch)
    {
        const int KeyedLinearThreshold = 8;
        if (Math.Max(oldParent.ChildCount, newParent.ChildCount) <= KeyedLinearThreshold)
        {
            DiffChildrenByKeyedLinear(oldParent, newParent, ref patches, prevSnapshot, nextSnapshot, scratch);
            return;
        }

        DiffChildrenByKeyedMap(oldParent, newParent, ref patches, prevSnapshot, nextSnapshot, scratch);
    }

    private static void DiffChildrenByKeyedLinear(
        VirtualNodeReader oldParent,
        VirtualNodeReader newParent,
        ref ScratchList<VirtualNodePatch> patches,
        TextBufferSnapshot? prevSnapshot,
        TextBufferSnapshot? nextSnapshot,
        FrameScratchArena scratch)
    {
        Span<int> oldChildOffsets = stackalloc int[8];
        WriteChildOffsets(oldParent, oldChildOffsets);
        var newChildOffset = 0;
        for (var i = 0; i < newParent.ChildCount; i++)
        {
            var childIndex = newParent.DfsIndex + 1 + newChildOffset;
            var newChild = newParent.GetChild(i, childIndex);

            if (newChild.Key != NodeKey.None && TryFindChildIndexByKey(oldParent, newChild.Key, out var oldIdx))
            {
                // Same key → diff recursively
                DiffNode(oldParent.GetChild(oldIdx, oldParent.DfsIndex + 1 + oldChildOffsets[oldIdx]), newChild, ref patches, prevSnapshot, nextSnapshot, scratch);
            }
            else
            {
                // New key → Add
                patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Add, childIndex, newChild.Node));
            }

            newChildOffset += newChild.CountSubtreeNodes();
        }

        // Remove old children whose keys are not in new
        for (var i = 0; i < oldParent.ChildCount; i++)
        {
            var oldChild = oldParent.GetChild(i, oldParent.DfsIndex + 1 + oldChildOffsets[i]);
            if (oldChild.Key != NodeKey.None && !ContainsChildKey(newParent, oldChild.Key))
            {
                patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Remove, oldChild.DfsIndex, oldChild.Node));
            }
        }
    }

    private static void DiffChildrenByKeyedMap(
        VirtualNodeReader oldParent,
        VirtualNodeReader newParent,
        ref ScratchList<VirtualNodePatch> patches,
        TextBufferSnapshot? prevSnapshot,
        TextBufferSnapshot? nextSnapshot,
        FrameScratchArena scratch)
    {
        Span<ScratchNodeKeyIndexMap.Entry> oldMapStorage = stackalloc ScratchNodeKeyIndexMap.Entry[StackKeyMapEntryCapacity];
        Span<ScratchNodeKeyIndexMap.Entry> newMapStorage = stackalloc ScratchNodeKeyIndexMap.Entry[StackKeyMapEntryCapacity];
        using var oldKeyToIndex = scratch.CreateNodeKeyIndexMap(oldMapStorage, oldParent.ChildCount);
        using var newKeyToIndex = scratch.CreateNodeKeyIndexMap(newMapStorage, newParent.ChildCount);
        ScratchSpan<int> oldChildOffsetsOwner = default;
        Span<int> oldChildOffsets = stackalloc int[StackKeyMapEntryCapacity];
        if (oldParent.ChildCount > StackKeyMapEntryCapacity)
        {
            oldChildOffsetsOwner = scratch.RentIntSpan(oldParent.ChildCount);
            oldChildOffsets = oldChildOffsetsOwner.Span;
        }

        for (var i = 0; i < oldParent.ChildCount; i++)
        {
            oldKeyToIndex.Set(oldParent.GetChildKey(i), i);
        }

        for (var i = 0; i < newParent.ChildCount; i++)
        {
            newKeyToIndex.Set(newParent.GetChildKey(i), i);
        }

        try
        {
            WriteChildOffsets(oldParent, oldChildOffsets);

            var childOffset = 0;
            for (var i = 0; i < newParent.ChildCount; i++)
            {
                var childIndex = newParent.DfsIndex + 1 + childOffset;
                var newChild = newParent.GetChild(i, childIndex);

                if (newChild.Key != NodeKey.None && oldKeyToIndex.TryGet(newChild.Key, out var oldIdx))
                {
                    DiffNode(oldParent.GetChild(oldIdx, oldParent.DfsIndex + 1 + oldChildOffsets[oldIdx]), newChild, ref patches, prevSnapshot, nextSnapshot, scratch);
                }
                else
                {
                    patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Add, childIndex, newChild.Node));
                }

                childOffset += newChild.CountSubtreeNodes();
            }

            for (var i = 0; i < oldParent.ChildCount; i++)
            {
                var oldChild = oldParent.GetChild(i, oldParent.DfsIndex + 1 + oldChildOffsets[i]);
                if (oldChild.Key != NodeKey.None && !newKeyToIndex.Contains(oldChild.Key))
                {
                    patches.Add(new VirtualNodePatch(VirtualNodePatchOperation.Remove, oldChild.DfsIndex, oldChild.Node));
                }
            }
        }
        finally
        {
            oldChildOffsetsOwner.Dispose();
        }
    }

    private static bool TryFindChildIndexByKey(VirtualNodeReader parent, NodeKey key, out int index)
    {
        // Match the old Dictionary assignment behavior: duplicate keys resolve to the last child.
        for (var i = parent.ChildCount - 1; i >= 0; i--)
        {
            if (parent.GetChildKey(i) == key)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool ContainsChildKey(VirtualNodeReader parent, NodeKey key)
    {
        for (var i = 0; i < parent.ChildCount; i++)
        {
            if (parent.GetChildKey(i) == key)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteChildOffsets(VirtualNodeReader parent, Span<int> offsets)
    {
        var offset = 0;
        for (var i = 0; i < parent.ChildCount; i++)
        {
            offsets[i] = offset;
            offset += parent.GetChild(i, parent.DfsIndex + 1 + offset).CountSubtreeNodes();
        }
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

    internal static bool ContentEqual(ContentResource a, ContentResource b, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
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
        => Equals(new VirtualNodeReader(a, 0), new VirtualNodeReader(b, 0), prevSnapshot, nextSnapshot);

    public static bool Equals(VirtualNodeReader a, VirtualNodeReader b, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
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

        if (a.ChildCount != b.ChildCount)
        {
            return false;
        }

        for (var i = 0; i < a.ChildCount; i++)
        {
            var aChild = a.GetChild(i, 0);
            var bChild = b.GetChild(i, 0);
            if (!Equals(aChild, bChild, prevSnapshot, nextSnapshot))
            {
                return false;
            }
        }

        return true;
    }
}
