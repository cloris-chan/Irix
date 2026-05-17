namespace Irix.Rendering;

internal readonly struct HitTargetMetadataProjection : IEquatable<HitTargetMetadataProjection>
{
    private HitTargetMetadataProjection(
        bool succeeded,
        RetainedPartialApplyFallbackReason fallbackReason,
        HitTestTarget[] hitTargets)
    {
        Succeeded = succeeded;
        FallbackReason = fallbackReason;
        HitTargets = hitTargets;
    }

    public bool Succeeded { get; }
    public RetainedPartialApplyFallbackReason FallbackReason { get; }
    public HitTestTarget[] HitTargets { get; }

    public static HitTargetMetadataProjection CreateSucceeded(HitTestTarget[] hitTargets)
    {
        return new HitTargetMetadataProjection(true, RetainedPartialApplyFallbackReason.None, hitTargets);
    }

    public static HitTargetMetadataProjection CreateFallback()
    {
        return new HitTargetMetadataProjection(false, RetainedPartialApplyFallbackReason.HitTargetPatchFailed, []);
    }

    public bool Equals(HitTargetMetadataProjection other)
    {
        return Succeeded == other.Succeeded
            && FallbackReason == other.FallbackReason
            && EqualityComparer<HitTestTarget[]>.Default.Equals(HitTargets, other.HitTargets);
    }

    public override bool Equals(object? obj) => obj is HitTargetMetadataProjection other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Succeeded, FallbackReason, HitTargets);

    public static bool operator ==(HitTargetMetadataProjection left, HitTargetMetadataProjection right) => left.Equals(right);

    public static bool operator !=(HitTargetMetadataProjection left, HitTargetMetadataProjection right) => !left.Equals(right);
}

internal static class HitTargetMetadataProjector
{
    public static HitTargetMetadataProjection ProjectActionIds(
        VirtualNode retainedRoot,
        VirtualNode nextRoot,
        IReadOnlyList<int> dirtyDfsIndices,
        IReadOnlyList<HitTestTarget> retainedHitTargets)
    {
        Span<int> inlineDirty = stackalloc int[8];
        var dirtyArray = dirtyDfsIndices.Count > inlineDirty.Length
            ? new int[dirtyDfsIndices.Count]
            : null;
        var dirtySet = dirtyArray is null ? inlineDirty : dirtyArray.AsSpan();
        var dirtyCount = 0;
        foreach (var dirtyDfsIndex in dirtyDfsIndices)
        {
            if (!Contains(dirtySet[..dirtyCount], dirtyDfsIndex))
            {
                dirtySet[dirtyCount++] = dirtyDfsIndex;
            }
        }

        var actionNodes = retainedHitTargets.Count == 0 ? [] : new ActionNodeMetadata[retainedHitTargets.Count];
        var actionNodeCount = 0;
        var dfsIndex = 0;
        if (dirtyCount == 0 || !TryCollectActionNodes(retainedRoot, nextRoot, ref dfsIndex, actionNodes, ref actionNodeCount))
        {
            return HitTargetMetadataProjection.CreateFallback();
        }

        if (actionNodeCount != retainedHitTargets.Count)
        {
            return HitTargetMetadataProjection.CreateFallback();
        }

        var projectedDirtyCount = 0;
        var patched = retainedHitTargets.Count == 0 ? [] : retainedHitTargets.ToArray();
        for (var i = 0; i < actionNodeCount; i++)
        {
            var actionNode = actionNodes[i];
            var retainedHitTarget = retainedHitTargets[i];
            if (Contains(dirtySet[..dirtyCount], actionNode.DfsIndex))
            {
                projectedDirtyCount++;
                patched[i] = new HitTestTarget(retainedHitTarget.Bounds, actionNode.ActionId, retainedHitTarget.ClipBounds);
                continue;
            }

            if (retainedHitTarget.ActionId != actionNode.ActionId)
            {
                return HitTargetMetadataProjection.CreateFallback();
            }
        }

        if (projectedDirtyCount != dirtyCount)
        {
            return HitTargetMetadataProjection.CreateFallback();
        }

        return HitTargetMetadataProjection.CreateSucceeded(patched);
    }

    private static bool TryCollectActionNodes(
        VirtualNode retainedNode,
        VirtualNode nextNode,
        ref int dfsIndex,
        Span<ActionNodeMetadata> actionNodes,
        ref int actionNodeCount)
    {
        var currentIndex = dfsIndex;
        var retainedChildren = retainedNode.Children;
        var nextChildren = nextNode.Children;
        if (retainedNode.Kind != nextNode.Kind || retainedNode.Key != nextNode.Key || retainedChildren.Length != nextChildren.Length)
        {
            return false;
        }

        var reader = new PropertyReader(nextNode.Properties);
        var actionId = reader.GetActionId(VirtualPropertyKey.ActionId);
        if (nextNode.Kind == VirtualNodeKind.Button && !actionId.IsNone)
        {
            if (actionNodeCount >= actionNodes.Length)
            {
                return false;
            }

            actionNodes[actionNodeCount++] = new ActionNodeMetadata(currentIndex, actionId);
        }

        dfsIndex++;
        for (var i = 0; i < retainedChildren.Length; i++)
        {
            if (!TryCollectActionNodes(retainedChildren[i], nextChildren[i], ref dfsIndex, actionNodes, ref actionNodeCount))
            {
                return false;
            }
        }

        return true;
    }

    private readonly struct ActionNodeMetadata(int DfsIndex, ActionId ActionId) : IEquatable<ActionNodeMetadata>
    {
        public int DfsIndex { get; } = DfsIndex;
        public ActionId ActionId { get; } = ActionId;

        public bool Equals(ActionNodeMetadata other) => DfsIndex == other.DfsIndex && ActionId.Equals(other.ActionId);

        public override bool Equals(object? obj) => obj is ActionNodeMetadata other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(DfsIndex, ActionId);

        public static bool operator ==(ActionNodeMetadata left, ActionNodeMetadata right) => left.Equals(right);

        public static bool operator !=(ActionNodeMetadata left, ActionNodeMetadata right) => !left.Equals(right);
    }

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
}
