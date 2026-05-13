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
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications)
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
        return TryProject(retainedRoot, nextRoot, dirtySet, ref dfsIndex, ref patchedDirtyCount, out var root, out var reason)
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
        out RetainedPartialApplyFallbackReason reason)
    {
        var currentIndex = dfsIndex;
        var isDirty = dirtySet.Contains(currentIndex);
        patchedNode = default;
        reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;

        if (retainedNode.Kind != nextNode.Kind || retainedNode.Key != nextNode.Key || retainedNode.Children.Length != nextNode.Children.Length)
        {
            return false;
        }

        if (retainedNode.Content != nextNode.Content)
        {
            reason = RetainedPartialApplyFallbackReason.NotStyleOnly;
            return false;
        }

        if (isDirty)
        {
            if (retainedNode.Kind != VirtualNodeKind.Button || !TryValidateDirtyAttributes(retainedNode.Attributes, nextNode.Attributes, out reason))
            {
                return false;
            }
        }
        else if (!AttributesEqual(retainedNode.Attributes, nextNode.Attributes))
        {
            return false;
        }

        dfsIndex++;
        var patchedChildren = retainedNode.Children;
        for (var i = 0; i < retainedNode.Children.Length; i++)
        {
            if (!TryProject(retainedNode.Children[i], nextNode.Children[i], dirtySet, ref dfsIndex, ref patchedDirtyCount, out var patchedChild, out reason))
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
            patchedNode = new VirtualNode(retainedNode.Kind, retainedNode.Key, retainedNode.Content, nextNode.Attributes.ToArray(), patchedChildren);
            return true;
        }

        patchedNode = patchedChildren == retainedNode.Children
            ? retainedNode
            : new VirtualNode(retainedNode.Kind, retainedNode.Key, retainedNode.Content, retainedNode.Attributes, patchedChildren);
        return true;
    }

    private static bool TryValidateDirtyAttributes(
        VirtualNodeAttribute[] retainedAttributes,
        VirtualNodeAttribute[] nextAttributes,
        out RetainedPartialApplyFallbackReason reason)
    {
        reason = RetainedPartialApplyFallbackReason.None;
        if (!TryGetChangedAttributeKeys(retainedAttributes, nextAttributes, out var changedKeys))
        {
            reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
            return false;
        }

        foreach (var key in changedKeys)
        {
            if (!key.IsControlMetadataKey())
            {
                reason = RetainedPartialApplyFallbackReason.NotStyleOnly;
                return false;
            }

            if (!TryValidateNextMetadataAttribute(nextAttributes, key))
            {
                reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
                return false;
            }
        }

        return true;
    }

    private static bool TryGetChangedAttributeKeys(
        VirtualNodeAttribute[] retainedAttributes,
        VirtualNodeAttribute[] nextAttributes,
        out VirtualAttributeKey[] changedKeys)
    {
        changedKeys = [];
        var keys = new HashSet<VirtualAttributeKey>();
        foreach (var attribute in retainedAttributes)
        {
            keys.Add(attribute.Key);
        }

        foreach (var attribute in nextAttributes)
        {
            keys.Add(attribute.Key);
        }

        var changed = new List<VirtualAttributeKey>();
        foreach (var key in keys)
        {
            if (key == VirtualAttributeKey.Unknown) continue;
            if (!TryGetUniqueAttribute(retainedAttributes, key, out var retainedFound, out var retainedAttribute)
                || !TryGetUniqueAttribute(nextAttributes, key, out var nextFound, out var nextAttribute))
            {
                return false;
            }

            if (retainedFound != nextFound || retainedAttribute.Value != nextAttribute.Value)
            {
                changed.Add(key);
            }
        }

        changedKeys = [.. changed];
        return true;
    }

    private static bool TryValidateNextMetadataAttribute(VirtualNodeAttribute[] attributes, VirtualAttributeKey key)
    {
        if (!TryGetUniqueAttribute(attributes, key, out var found, out var attribute))
        {
            return false;
        }

        if (!found)
        {
            return key != VirtualAttributeKey.ActionId;
        }

        return key switch
        {
            VirtualAttributeKey.ActionId => attribute.Value.Kind == AttributeValueKind.ActionId && !attribute.Value.ActionIdValue.IsNone,
            VirtualAttributeKey.IsHovered or VirtualAttributeKey.IsPressed or VirtualAttributeKey.IsFocused => attribute.Value.Kind == AttributeValueKind.Boolean,
            _ => false
        };
    }

    private static bool TryGetUniqueAttribute(VirtualNodeAttribute[] attributes, VirtualAttributeKey key, out bool found, out VirtualNodeAttribute attribute)
    {
        found = false;
        attribute = default;
        foreach (var candidate in attributes)
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
            attribute = candidate;
        }

        return true;
    }

    private static bool AttributesEqual(VirtualNodeAttribute[] left, VirtualNodeAttribute[] right)
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

    private static bool IsControlMetadataAttribute(VirtualAttributeKey key)
    {
        return key.IsControlMetadataKey();
    }
}