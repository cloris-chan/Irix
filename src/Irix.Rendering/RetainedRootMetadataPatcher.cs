namespace Irix.Rendering;

internal sealed record RetainedRootMetadataPatch(
    bool Succeeded,
    RetainedPartialApplyFallbackReason FallbackReason,
    VirtualNode Root)
{
    public static RetainedRootMetadataPatch CreateSucceeded(VirtualNode root)
    {
        return new RetainedRootMetadataPatch(true, RetainedPartialApplyFallbackReason.None, root);
    }

    public static RetainedRootMetadataPatch CreateFallback(RetainedPartialApplyFallbackReason reason)
    {
        return new RetainedRootMetadataPatch(false, reason, default);
    }
}

internal static class RetainedRootMetadataPatcher
{
    public static RetainedRootMetadataPatch ProjectControlMetadata(
        VirtualNode retainedRoot,
        VirtualNode nextRoot,
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications,
        TextBufferSnapshot? snapshot = null)
    {
        var dirtySet = new HashSet<int>();
        foreach (var classification in dirtyClassifications)
        {
            if (classification.Reason != LayoutRebuildReason.StyleOnly)
            {
                return RetainedRootMetadataPatch.CreateFallback(RetainedPartialApplyFallbackReason.NotStyleOnly);
            }

            dirtySet.Add(classification.DfsIndex);
        }

        if (dirtySet.Count == 0)
        {
            return RetainedRootMetadataPatch.CreateFallback(RetainedPartialApplyFallbackReason.HitTargetPatchFailed);
        }

        var dfsIndex = 0;
        var patchedDirtyCount = 0;
        return TryProject(retainedRoot, nextRoot, dirtySet, ref dfsIndex, ref patchedDirtyCount, out var root, out var reason, snapshot)
            && patchedDirtyCount == dirtySet.Count
            ? RetainedRootMetadataPatch.CreateSucceeded(root)
            : RetainedRootMetadataPatch.CreateFallback(reason);
    }

    private static bool TryProject(
        VirtualNode retainedNode,
        VirtualNode nextNode,
        HashSet<int> dirtySet,
        ref int dfsIndex,
        ref int patchedDirtyCount,
        out VirtualNode patchedNode,
        out RetainedPartialApplyFallbackReason reason,
        TextBufferSnapshot? snapshot)
    {
        var currentIndex = dfsIndex;
        var isDirty = dirtySet.Contains(currentIndex);
        patchedNode = default;
        reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;

        if (retainedNode.Kind != nextNode.Kind || retainedNode.Key != nextNode.Key || retainedNode.Children.Count != nextNode.Children.Count)
        {
            return false;
        }

        if (!VirtualNodeDiffer.ContentEqual(retainedNode.Content, nextNode.Content, snapshot, snapshot))
        {
            reason = RetainedPartialApplyFallbackReason.NotStyleOnly;
            return false;
        }

        if (isDirty)
        {
            if (retainedNode.Kind != VirtualNodeKind.Button || !TryValidateDirtyProperties(retainedNode.Properties, nextNode.Properties, out reason))
            {
                return false;
            }
        }
        else if (!PropertiesEqual(retainedNode.Properties, nextNode.Properties))
        {
            return false;
        }

        dfsIndex++;
        var patchedChildren = retainedNode.Children;
        VirtualNode[]? patchedChildrenArray = null;
        for (var i = 0; i < retainedNode.Children.Count; i++)
        {
            if (!TryProject(retainedNode.Children[i], nextNode.Children[i], dirtySet, ref dfsIndex, ref patchedDirtyCount, out var patchedChild, out reason, snapshot))
            {
                return false;
            }

            if (patchedChildrenArray is null && patchedChild != retainedNode.Children[i])
            {
                patchedChildrenArray = retainedNode.Children.ToArray();
            }

            if (patchedChildrenArray is not null)
            {
                patchedChildrenArray[i] = patchedChild;
            }
        }

        if (isDirty)
        {
            patchedDirtyCount++;
            patchedNode = new VirtualNode(retainedNode.Kind, retainedNode.Key, retainedNode.Content, nextNode.Properties, patchedChildrenArray ?? patchedChildren);
            return true;
        }

        patchedNode = patchedChildrenArray is null
            ? retainedNode
            : new VirtualNode(retainedNode.Kind, retainedNode.Key, retainedNode.Content, retainedNode.Properties, patchedChildrenArray);
        return true;
    }

    private static bool TryValidateDirtyProperties(
        IReadOnlyList<VirtualNodeProperty> retainedProperties,
        IReadOnlyList<VirtualNodeProperty> nextProperties,
        out RetainedPartialApplyFallbackReason reason)
    {
        reason = RetainedPartialApplyFallbackReason.None;
        if (!TryGetChangedPropertyKeys(retainedProperties, nextProperties, out var changedKeys))
        {
            reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
            return false;
        }

        foreach (var key in changedKeys)
        {
            if (!PropertyChangeSetClassification.IsControlMetadataKey(key))
            {
                reason = RetainedPartialApplyFallbackReason.NotStyleOnly;
                return false;
            }

            if (!TryValidateNextMetadataProperty(nextProperties, key))
            {
                reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
                return false;
            }
        }

        return true;
    }

    private static bool TryGetChangedPropertyKeys(
        IReadOnlyList<VirtualNodeProperty> retainedProperties,
        IReadOnlyList<VirtualNodeProperty> nextProperties,
        out VirtualPropertyKey[] changedKeys)
    {
        changedKeys = [];
        var keys = new HashSet<VirtualPropertyKey>();
        foreach (var property in retainedProperties)
        {
            keys.Add(property.Key);
        }

        foreach (var property in nextProperties)
        {
            keys.Add(property.Key);
        }

        var changed = new List<VirtualPropertyKey>();
        foreach (var key in keys)
        {
            if (key == default(VirtualPropertyKey)) continue;
            if (!TryGetUniqueProperty(retainedProperties, key, out var retainedFound, out var retainedProperty)
                || !TryGetUniqueProperty(nextProperties, key, out var nextFound, out var nextProperty))
            {
                return false;
            }

            if (retainedFound != nextFound || retainedProperty.Value != nextProperty.Value)
            {
                changed.Add(key);
            }
        }

        changedKeys = [.. changed];
        return true;
    }

    private static bool TryValidateNextMetadataProperty(IReadOnlyList<VirtualNodeProperty> properties, VirtualPropertyKey key)
    {
        if (!TryGetUniqueProperty(properties, key, out var found, out var property))
        {
            return false;
        }

        if (!found)
        {
            return key != VirtualPropertyKey.ActionId;
        }

        if (key == VirtualPropertyKey.ActionId)
            return property.Value.Kind == PropertyValueKind.ActionId && !property.Value.GetRequiredActionId().IsNone;
        if (key == VirtualPropertyKey.IsHovered || key == VirtualPropertyKey.IsPressed || key == VirtualPropertyKey.IsFocused)
            return property.Value.Kind == PropertyValueKind.Boolean;
        return VirtualPropertyMetadata.TryGet(key, out var metadata)
            && (metadata.Effects & StyleEffect.Layout) == 0
            && property.Value.Kind == metadata.ValueKind;
    }

    private static bool TryGetUniqueProperty(IReadOnlyList<VirtualNodeProperty> properties, VirtualPropertyKey key, out bool found, out VirtualNodeProperty property)
    {
        found = false;
        property = default;
        foreach (var candidate in properties)
        {
            if (candidate.Key != key)
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            found = true;
            property = candidate;
        }

        return true;
    }

    private static bool PropertiesEqual(IReadOnlyList<VirtualNodeProperty> left, IReadOnlyList<VirtualNodeProperty> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
