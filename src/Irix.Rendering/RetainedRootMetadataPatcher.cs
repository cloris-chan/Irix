namespace Irix.Rendering;

internal readonly struct RetainedRootMetadataPatch : IEquatable<RetainedRootMetadataPatch>
{
    private RetainedRootMetadataPatch(bool succeeded, RetainedPartialApplyFallbackReason fallbackReason, VirtualNode root)
    {
        Succeeded = succeeded;
        FallbackReason = fallbackReason;
        Root = root;
    }

    public bool Succeeded { get; }
    public RetainedPartialApplyFallbackReason FallbackReason { get; }
    public VirtualNode Root { get; }

    public static RetainedRootMetadataPatch CreateSucceeded(VirtualNode root)
    {
        return new RetainedRootMetadataPatch(true, RetainedPartialApplyFallbackReason.None, root);
    }

    public static RetainedRootMetadataPatch CreateFallback(RetainedPartialApplyFallbackReason reason)
    {
        return new RetainedRootMetadataPatch(false, reason, default);
    }

    public bool Equals(RetainedRootMetadataPatch other)
    {
        return Succeeded == other.Succeeded
            && FallbackReason == other.FallbackReason
            && Root.Equals(other.Root);
    }

    public override bool Equals(object? obj) => obj is RetainedRootMetadataPatch other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Succeeded, FallbackReason, Root);

    public static bool operator ==(RetainedRootMetadataPatch left, RetainedRootMetadataPatch right) => left.Equals(right);

    public static bool operator !=(RetainedRootMetadataPatch left, RetainedRootMetadataPatch right) => !left.Equals(right);
}

internal static class RetainedRootMetadataPatcher
{
    public static RetainedRootMetadataPatch ProjectControlMetadata(
        VirtualNode retainedRoot,
        VirtualNode nextRoot,
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications,
        TextBufferSnapshot? retainedSnapshot = null,
        TextBufferSnapshot? nextSnapshot = null)
    {
        retainedSnapshot ??= nextSnapshot;
        nextSnapshot ??= retainedSnapshot;

        Span<int> inlineDirty = stackalloc int[8];
        var dirtyArray = dirtyClassifications.Count > inlineDirty.Length
            ? new int[dirtyClassifications.Count]
            : null;
        var dirtySet = dirtyArray is null ? inlineDirty : dirtyArray.AsSpan();
        var dirtyCount = 0;
        foreach (var classification in dirtyClassifications)
        {
            if (classification.Reason != LayoutRebuildReason.StyleOnly)
            {
                return RetainedRootMetadataPatch.CreateFallback(RetainedPartialApplyFallbackReason.NotStyleOnly);
            }

            if (!Contains(dirtySet[..dirtyCount], classification.DfsIndex))
            {
                dirtySet[dirtyCount++] = classification.DfsIndex;
            }
        }

        if (dirtyCount == 0)
        {
            return RetainedRootMetadataPatch.CreateFallback(RetainedPartialApplyFallbackReason.HitTargetPatchFailed);
        }

        var dfsIndex = 0;
        var patchedDirtyCount = 0;
        return TryProject(retainedRoot, nextRoot, dirtySet[..dirtyCount], ref dfsIndex, ref patchedDirtyCount, out var root, out var reason, retainedSnapshot, nextSnapshot)
            && patchedDirtyCount == dirtyCount
            ? RetainedRootMetadataPatch.CreateSucceeded(root)
            : RetainedRootMetadataPatch.CreateFallback(reason);
    }

    private static bool TryProject(
        VirtualNode retainedNode,
        VirtualNode nextNode,
        ReadOnlySpan<int> dirtySet,
        ref int dfsIndex,
        ref int patchedDirtyCount,
        out VirtualNode patchedNode,
        out RetainedPartialApplyFallbackReason reason,
        TextBufferSnapshot? retainedSnapshot,
        TextBufferSnapshot? nextSnapshot)
    {
        var currentIndex = dfsIndex;
        var isDirty = Contains(dirtySet, currentIndex);
        patchedNode = default;
        reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;

        var retainedChildren = retainedNode.Children;
        var nextChildren = nextNode.Children;
        if (retainedNode.Kind != nextNode.Kind || retainedNode.Key != nextNode.Key || retainedChildren.Length != nextChildren.Length)
        {
            return false;
        }

        if (!VirtualNodeDiffer.ContentEqual(retainedNode.Content, nextNode.Content, retainedSnapshot, nextSnapshot))
        {
            reason = RetainedPartialApplyFallbackReason.NotStyleOnly;
            return false;
        }

        if (isDirty)
        {
            if (!TryValidateDirtyProperties(retainedNode.Properties, nextNode.Properties, out reason))
            {
                return false;
            }
        }
        else if (!PropertiesEqual(retainedNode.Properties, nextNode.Properties))
        {
            return false;
        }

        dfsIndex++;
        var patchedChildren = retainedChildren;
        VirtualNode[]? patchedChildrenArray = null;
        for (var i = 0; i < retainedChildren.Length; i++)
        {
            if (!TryProject(retainedChildren[i], nextChildren[i], dirtySet, ref dfsIndex, ref patchedDirtyCount, out var patchedChild, out reason, retainedSnapshot, nextSnapshot))
            {
                return false;
            }

            if (patchedChildrenArray is null && IsShallowNodeReplacement(patchedChild, retainedChildren[i]))
            {
                patchedChildrenArray = retainedChildren.ToArray();
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
        ReadOnlySpan<VirtualNodeProperty> retainedProperties,
        ReadOnlySpan<VirtualNodeProperty> nextProperties,
        out RetainedPartialApplyFallbackReason reason)
    {
        reason = RetainedPartialApplyFallbackReason.None;
        var keyCapacity = retainedProperties.Length + nextProperties.Length;
        Span<VirtualPropertyKey> inlineKeys = stackalloc VirtualPropertyKey[8];
        var keyArray = keyCapacity > inlineKeys.Length
            ? new VirtualPropertyKey[keyCapacity]
            : null;
        var changedKeys = keyArray is null ? inlineKeys : keyArray.AsSpan();

        if (!TryGetChangedPropertyKeys(retainedProperties, nextProperties, changedKeys, out var changedKeyCount))
        {
            reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
            return false;
        }

        for (var i = 0; i < changedKeyCount; i++)
        {
            var key = changedKeys[i];
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
        ReadOnlySpan<VirtualNodeProperty> retainedProperties,
        ReadOnlySpan<VirtualNodeProperty> nextProperties,
        scoped Span<VirtualPropertyKey> changedKeys,
        out int changedKeyCount)
    {
        changedKeyCount = 0;
        foreach (var property in retainedProperties)
        {
            if (property.Key != default(VirtualPropertyKey) && !Contains(changedKeys[..changedKeyCount], property.Key))
            {
                changedKeys[changedKeyCount++] = property.Key;
            }
        }

        foreach (var property in nextProperties)
        {
            if (property.Key != default(VirtualPropertyKey) && !Contains(changedKeys[..changedKeyCount], property.Key))
            {
                changedKeys[changedKeyCount++] = property.Key;
            }
        }

        var write = 0;
        for (var i = 0; i < changedKeyCount; i++)
        {
            var key = changedKeys[i];
            if (!TryGetUniqueProperty(retainedProperties, key, out var retainedFound, out var retainedProperty)
                || !TryGetUniqueProperty(nextProperties, key, out var nextFound, out var nextProperty))
            {
                changedKeyCount = 0;
                return false;
            }

            if (retainedFound != nextFound || retainedProperty.Value != nextProperty.Value)
            {
                changedKeys[write++] = key;
            }
        }

        changedKeyCount = write;
        return true;
    }

    private static bool TryValidateNextMetadataProperty(ReadOnlySpan<VirtualNodeProperty> properties, VirtualPropertyKey key)
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

    private static bool TryGetUniqueProperty(ReadOnlySpan<VirtualNodeProperty> properties, VirtualPropertyKey key, out bool found, out VirtualNodeProperty property)
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

    private static bool PropertiesEqual(ReadOnlySpan<VirtualNodeProperty> left, ReadOnlySpan<VirtualNodeProperty> right)
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

    private static bool IsShallowNodeReplacement(VirtualNode left, VirtualNode right) =>
        left.Kind != right.Kind
        || left.Key != right.Key
        || left.Content != right.Content
        || !PropertiesEqual(left.Properties, right.Properties)
        || left.Children.Length != right.Children.Length;

    private static bool Contains(ReadOnlySpan<int> values, int value)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(ReadOnlySpan<VirtualPropertyKey> values, VirtualPropertyKey value)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] == value)
            {
                return true;
            }
        }

        return false;
    }
}
