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
        if (!TryGetChangedAttributeNames(retainedAttributes, nextAttributes, out var changedNames))
        {
            reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
            return false;
        }

        foreach (var name in changedNames)
        {
            if (!IsControlMetadataAttribute(name))
            {
                reason = RetainedPartialApplyFallbackReason.NotStyleOnly;
                return false;
            }

            if (!TryValidateNextMetadataAttribute(nextAttributes, name))
            {
                reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
                return false;
            }
        }

        return true;
    }

    private static bool TryGetChangedAttributeNames(
        VirtualNodeAttribute[] retainedAttributes,
        VirtualNodeAttribute[] nextAttributes,
        out string[] changedNames)
    {
        changedNames = [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in retainedAttributes)
        {
            names.Add(attribute.Name);
        }

        foreach (var attribute in nextAttributes)
        {
            names.Add(attribute.Name);
        }

        var changed = new List<string>();
        foreach (var name in names)
        {
            if (!TryGetUniqueAttribute(retainedAttributes, name, out var retainedFound, out var retainedAttribute)
                || !TryGetUniqueAttribute(nextAttributes, name, out var nextFound, out var nextAttribute))
            {
                return false;
            }

            if (retainedFound != nextFound || retainedAttribute.Value != nextAttribute.Value)
            {
                changed.Add(name);
            }
        }

        changedNames = [.. changed];
        return true;
    }

    private static bool TryValidateNextMetadataAttribute(VirtualNodeAttribute[] attributes, string name)
    {
        if (!TryGetUniqueAttribute(attributes, name, out var found, out var attribute))
        {
            return false;
        }

        if (!found)
        {
            return name != "ActionId";
        }

        return name switch
        {
            "ActionId" => attribute.Value.Kind == AttributeValueKind.Text && !string.IsNullOrWhiteSpace(attribute.Value.Text),
            "IsHovered" or "IsPressed" or "IsFocused" => attribute.Value.Kind == AttributeValueKind.Boolean,
            _ => false
        };
    }

    private static bool TryGetUniqueAttribute(VirtualNodeAttribute[] attributes, string name, out bool found, out VirtualNodeAttribute attribute)
    {
        found = false;
        attribute = default;
        foreach (var candidate in attributes)
        {
            if (candidate.Name != name)
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

    private static bool IsControlMetadataAttribute(string name)
    {
        return name is "ActionId" or "IsHovered" or "IsPressed" or "IsFocused";
    }
}