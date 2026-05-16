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

        if (retainedNode.Kind != nextNode.Kind || retainedNode.Key != nextNode.Key || retainedNode.Children.Length != nextNode.Children.Length)
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
        for (var i = 0; i < retainedNode.Children.Length; i++)
        {
            if (!TryProject(retainedNode.Children[i], nextNode.Children[i], dirtySet, ref dfsIndex, ref patchedDirtyCount, out var patchedChild, out reason, snapshot))
            {
                return false;
            }

            if (patchedChildren == retainedNode.Children && patchedChild != retainedNode.Children[i])
            {
                patchedChildren = retainedNode.Children.ToArray();
            }

            if (patchedChildren != retainedNode.Children)
            {
                patchedChildren[i] = patchedChild;
            }
        }

        if (isDirty)
        {
            patchedDirtyCount++;
            patchedNode = new VirtualNode(retainedNode.Kind, retainedNode.Key, retainedNode.Content, nextNode.Properties.ToArray(), patchedChildren);
            return true;
        }

        patchedNode = patchedChildren == retainedNode.Children
            ? retainedNode
            : new VirtualNode(retainedNode.Kind, retainedNode.Key, retainedNode.Content, retainedNode.Properties, patchedChildren);
        return true;
    }

    private static bool TryValidateDirtyProperties(
        VirtualNodeProperty[] retainedProperties,
        VirtualNodeProperty[] nextProperties,
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
        VirtualNodeProperty[] retainedProperties,
        VirtualNodeProperty[] nextProperties,
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

    private static bool TryValidateNextMetadataProperty(VirtualNodeProperty[] properties, VirtualPropertyKey key)
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
            return property.Value.Kind == PropertyValueKind.ActionId && !property.Value.ActionIdValue.IsNone;
        if (key == VirtualPropertyKey.IsHovered || key == VirtualPropertyKey.IsPressed || key == VirtualPropertyKey.IsFocused)
            return property.Value.Kind == PropertyValueKind.Boolean;
        return VirtualPropertyMetadata.TryGet(key, out var metadata)
            && (metadata.Effects & StyleEffect.Layout) == 0
            && property.Value.Kind == metadata.ValueKind;
    }

    private static bool TryGetUniqueProperty(VirtualNodeProperty[] properties, VirtualPropertyKey key, out bool found, out VirtualNodeProperty property)
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

    private static bool PropertiesEqual(VirtualNodeProperty[] left, VirtualNodeProperty[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
