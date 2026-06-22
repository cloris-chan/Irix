namespace Irix.Rendering;

internal readonly struct HitTargetMetadataProjection
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

}

internal static class HitTargetMetadataProjector
{
    private const int InlineDirtyIndexCapacity = 32;

    public static HitTargetMetadataProjection ProjectActionIds(
        VirtualNode retainedRoot,
        VirtualNode nextRoot,
        IReadOnlyList<int> dirtyDfsIndices,
        IReadOnlyList<HitTestTarget> retainedHitTargets)
    {
        var scratch = new RenderScratchBuffer();
        Span<int> dirtyStorage = stackalloc int[InlineDirtyIndexCapacity];
        scoped var sortedDirty = scratch.CreateDirtyIndexList(dirtyStorage);
        try
        {
            for (var i = 0; i < dirtyDfsIndices.Count; i++)
            {
                sortedDirty.Add(dirtyDfsIndices[i]);
            }

            if (sortedDirty.Count == 0)
            {
                return HitTargetMetadataProjection.CreateFallback();
            }

            sortedDirty.Sort();
            var actionNodes = retainedHitTargets.Count == 0 ? [] : new ActionNodeMetadata[retainedHitTargets.Count];
            var actionNodeCount = 0;
            var dfsIndex = 0;
            if (!TryCollectActionNodes(retainedRoot, nextRoot, ref dfsIndex, actionNodes, ref actionNodeCount)
                || actionNodeCount != retainedHitTargets.Count)
            {
                return HitTargetMetadataProjection.CreateFallback();
            }

            var dirtyCursor = 0;
            var patched = retainedHitTargets.Count == 0 ? [] : retainedHitTargets.ToArray();
            for (var i = 0; i < actionNodeCount; i++)
            {
                var actionNode = actionNodes[i];
                var retainedHitTarget = retainedHitTargets[i];
                if (!DirtyDfsIndexCursor.TryRead(sortedDirty.Written, ref dirtyCursor, actionNode.DfsIndex, out var isDirty))
                {
                    return HitTargetMetadataProjection.CreateFallback();
                }

                if (isDirty)
                {
                    patched[i] = new HitTestTarget(
                        retainedHitTarget.Bounds,
                        actionNode.ActionId,
                        retainedHitTarget.ClipBounds,
                        retainedHitTarget.CommandStart,
                        retainedHitTarget.CommandCount);
                    continue;
                }

                if (retainedHitTarget.ActionId != actionNode.ActionId)
                {
                    return HitTargetMetadataProjection.CreateFallback();
                }
            }

            if (!DirtyDfsIndexCursor.IsComplete(sortedDirty.Written, dirtyCursor))
            {
                return HitTargetMetadataProjection.CreateFallback();
            }

            return HitTargetMetadataProjection.CreateSucceeded(patched);
        }
        finally
        {
            sortedDirty.Dispose();
        }
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
}
