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

internal readonly struct RetainedRootMetadataValidation : IEquatable<RetainedRootMetadataValidation>
{
    private RetainedRootMetadataValidation(bool succeeded, RetainedPartialApplyFallbackReason fallbackReason)
    {
        Succeeded = succeeded;
        FallbackReason = fallbackReason;
    }

    public bool Succeeded { get; }
    public RetainedPartialApplyFallbackReason FallbackReason { get; }

    public static RetainedRootMetadataValidation CreateSucceeded()
    {
        return new RetainedRootMetadataValidation(true, RetainedPartialApplyFallbackReason.None);
    }

    public static RetainedRootMetadataValidation CreateFallback(RetainedPartialApplyFallbackReason reason)
    {
        return new RetainedRootMetadataValidation(false, reason);
    }

    public bool Equals(RetainedRootMetadataValidation other)
    {
        return Succeeded == other.Succeeded
            && FallbackReason == other.FallbackReason;
    }

    public override bool Equals(object? obj) => obj is RetainedRootMetadataValidation other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Succeeded, FallbackReason);

    public static bool operator ==(RetainedRootMetadataValidation left, RetainedRootMetadataValidation right) => left.Equals(right);

    public static bool operator !=(RetainedRootMetadataValidation left, RetainedRootMetadataValidation right) => !left.Equals(right);
}

internal static class RetainedRootMetadataPatcher
{
    private const int InlineDirtyIndexCapacity = 32;

    public static RetainedRootMetadataValidation ValidateControlMetadata(
        VirtualNode retainedRoot,
        VirtualNode nextRoot,
        LayoutDirtyClassificationList dirtyClassifications,
        TextBufferSnapshot? retainedSnapshot = null,
        TextBufferSnapshot? nextSnapshot = null)
    {
        retainedSnapshot ??= nextSnapshot;
        nextSnapshot ??= retainedSnapshot;

        var scratch = new RenderScratchBuffer();
        Span<int> dirtyStorage = stackalloc int[InlineDirtyIndexCapacity];
        scoped var sortedDirty = scratch.CreateDirtyIndexList(dirtyStorage);
        try
        {
            foreach (var classification in dirtyClassifications)
            {
                if (classification.Reason != LayoutRebuildReason.StyleOnly)
                {
                    return RetainedRootMetadataValidation.CreateFallback(RetainedPartialApplyFallbackReason.NotStyleOnly);
                }

                sortedDirty.Add(classification.DfsIndex);
            }

            if (sortedDirty.Count == 0)
            {
                return RetainedRootMetadataValidation.CreateFallback(RetainedPartialApplyFallbackReason.HitTargetPatchFailed);
            }

            sortedDirty.Sort();
            var dfsIndex = 0;
            var dirtyCursor = 0;
            return TryValidate(retainedRoot, nextRoot, sortedDirty.Written, ref dirtyCursor, ref dfsIndex, out var reason, retainedSnapshot, nextSnapshot)
                && DirtyDfsIndexCursor.IsComplete(sortedDirty.Written, dirtyCursor)
                ? RetainedRootMetadataValidation.CreateSucceeded()
                : RetainedRootMetadataValidation.CreateFallback(reason);
        }
        finally
        {
            sortedDirty.Dispose();
        }
    }

    public static RetainedRootMetadataPatch ProjectControlMetadata(
        VirtualNode retainedRoot,
        VirtualNode nextRoot,
        LayoutDirtyClassificationList dirtyClassifications,
        TextBufferSnapshot? retainedSnapshot = null,
        TextBufferSnapshot? nextSnapshot = null)
    {
        retainedSnapshot ??= nextSnapshot;
        nextSnapshot ??= retainedSnapshot;

        var scratch = new RenderScratchBuffer();
        Span<int> dirtyStorage = stackalloc int[InlineDirtyIndexCapacity];
        scoped var sortedDirty = scratch.CreateDirtyIndexList(dirtyStorage);
        try
        {
            foreach (var classification in dirtyClassifications)
            {
                if (classification.Reason != LayoutRebuildReason.StyleOnly)
                {
                    return RetainedRootMetadataPatch.CreateFallback(RetainedPartialApplyFallbackReason.NotStyleOnly);
                }

                sortedDirty.Add(classification.DfsIndex);
            }

            if (sortedDirty.Count == 0)
            {
                return RetainedRootMetadataPatch.CreateFallback(RetainedPartialApplyFallbackReason.HitTargetPatchFailed);
            }

            sortedDirty.Sort();
            var dfsIndex = 0;
            var dirtyCursor = 0;
            return TryProject(retainedRoot, nextRoot, sortedDirty.Written, ref dirtyCursor, ref dfsIndex, out var root, out var reason, retainedSnapshot, nextSnapshot)
                && DirtyDfsIndexCursor.IsComplete(sortedDirty.Written, dirtyCursor)
                ? RetainedRootMetadataPatch.CreateSucceeded(root)
                : RetainedRootMetadataPatch.CreateFallback(reason);
        }
        finally
        {
            sortedDirty.Dispose();
        }
    }

    private static bool TryValidate(
        VirtualNode retainedNode,
        VirtualNode nextNode,
        ReadOnlySpan<int> sortedDirty,
        ref int dirtyCursor,
        ref int dfsIndex,
        out RetainedPartialApplyFallbackReason reason,
        TextBufferSnapshot? retainedSnapshot,
        TextBufferSnapshot? nextSnapshot)
    {
        var currentIndex = dfsIndex;
        reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
        if (!DirtyDfsIndexCursor.TryRead(sortedDirty, ref dirtyCursor, currentIndex, out var isDirty))
        {
            return false;
        }

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
        for (var i = 0; i < retainedChildren.Length; i++)
        {
            if (!TryValidate(retainedChildren[i], nextChildren[i], sortedDirty, ref dirtyCursor, ref dfsIndex, out reason, retainedSnapshot, nextSnapshot))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryProject(
        VirtualNode retainedNode,
        VirtualNode nextNode,
        ReadOnlySpan<int> sortedDirty,
        ref int dirtyCursor,
        ref int dfsIndex,
        out VirtualNode patchedNode,
        out RetainedPartialApplyFallbackReason reason,
        TextBufferSnapshot? retainedSnapshot,
        TextBufferSnapshot? nextSnapshot)
    {
        var currentIndex = dfsIndex;
        patchedNode = default;
        reason = RetainedPartialApplyFallbackReason.HitTargetPatchFailed;
        if (!DirtyDfsIndexCursor.TryRead(sortedDirty, ref dirtyCursor, currentIndex, out var isDirty))
        {
            return false;
        }

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
        VirtualNode[]? patchedChildrenStorage = null;
        for (var i = 0; i < retainedChildren.Length; i++)
        {
            if (!TryProject(retainedChildren[i], nextChildren[i], sortedDirty, ref dirtyCursor, ref dfsIndex, out var patchedChild, out reason, retainedSnapshot, nextSnapshot))
            {
                return false;
            }

            if (patchedChildrenStorage is null && IsShallowNodeReplacement(patchedChild, retainedChildren[i]))
            {
                patchedChildrenStorage = retainedChildren.ToArray();
            }

            if (patchedChildrenStorage is not null)
            {
                patchedChildrenStorage[i] = patchedChild;
            }
        }

        if (isDirty)
        {
            patchedNode = patchedChildrenStorage is null
                ? VirtualNode.CreateFromOwnedChildrenUnsafe(retainedNode.Kind, retainedNode.Key, retainedNode.Content, nextNode.Properties, patchedChildren)
                : VirtualNode.CreateFromOwnedChildrenUnsafe(retainedNode.Kind, retainedNode.Key, retainedNode.Content, nextNode.Properties, patchedChildrenStorage);
            return true;
        }

        patchedNode = patchedChildrenStorage is null
            ? retainedNode
            : VirtualNode.CreateFromOwnedChildrenUnsafe(retainedNode.Kind, retainedNode.Key, retainedNode.Content, retainedNode.Properties, patchedChildrenStorage);
        return true;
    }

    private static bool TryValidateDirtyProperties(
        VirtualNodePropertyList retainedProperties,
        VirtualNodePropertyList nextProperties,
        out RetainedPartialApplyFallbackReason reason)
    {
        reason = RetainedPartialApplyFallbackReason.None;
        var keyCapacity = retainedProperties.Count + nextProperties.Count;
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
        VirtualNodePropertyList retainedProperties,
        VirtualNodePropertyList nextProperties,
        scoped Span<VirtualPropertyKey> changedKeys,
        out int changedKeyCount)
    {
        changedKeyCount = 0;
        for (var i = 0; i < retainedProperties.Count; i++)
        {
            var property = retainedProperties[i];
            if (property.Key != default(VirtualPropertyKey) && !Contains(changedKeys[..changedKeyCount], property.Key))
            {
                changedKeys[changedKeyCount++] = property.Key;
            }
        }

        for (var i = 0; i < nextProperties.Count; i++)
        {
            var property = nextProperties[i];
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

    private static bool TryValidateNextMetadataProperty(VirtualNodePropertyList properties, VirtualPropertyKey key)
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

    private static bool TryGetUniqueProperty(VirtualNodePropertyList properties, VirtualPropertyKey key, out bool found, out VirtualNodeProperty property)
    {
        found = false;
        property = default;
        for (var i = 0; i < properties.Count; i++)
        {
            var candidate = properties[i];
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

    private static bool PropertiesEqual(VirtualNodePropertyList left, VirtualNodePropertyList right)
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

    private static bool IsShallowNodeReplacement(VirtualNode left, VirtualNode right) =>
        left.Kind != right.Kind
        || left.Key != right.Key
        || left.Content != right.Content
        || !PropertiesEqual(left.Properties, right.Properties)
        || left.Children.Length != right.Children.Length;

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
