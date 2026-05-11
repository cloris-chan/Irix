namespace Irix.Rendering;

internal sealed record HitTargetMetadataProjection(
    bool Succeeded,
    RetainedPartialApplyFallbackReason FallbackReason,
    IReadOnlyList<HitTestTarget> HitTargets)
{
    public static HitTargetMetadataProjection CreateSucceeded(IReadOnlyList<HitTestTarget> hitTargets)
    {
        return new HitTargetMetadataProjection(true, RetainedPartialApplyFallbackReason.None, hitTargets.ToArray());
    }

    public static HitTargetMetadataProjection CreateFallback()
    {
        return new HitTargetMetadataProjection(false, RetainedPartialApplyFallbackReason.HitTargetPatchFailed, []);
    }
}

internal static class HitTargetMetadataProjector
{
    public static HitTargetMetadataProjection ProjectActionIds(
        VirtualNode retainedRoot,
        VirtualNode nextRoot,
        IReadOnlyList<int> dirtyDfsIndices,
        IReadOnlyList<HitTestTarget> retainedHitTargets)
    {
        var dirtySet = new HashSet<int>(dirtyDfsIndices);
        var actionNodes = new List<ActionNodeMetadata>();
        var dfsIndex = 0;
        if (dirtySet.Count == 0 || !TryCollectActionNodes(retainedRoot, nextRoot, ref dfsIndex, actionNodes))
        {
            return HitTargetMetadataProjection.CreateFallback();
        }

        if (actionNodes.Count != retainedHitTargets.Count)
        {
            return HitTargetMetadataProjection.CreateFallback();
        }

        var patched = retainedHitTargets.Count == 0 ? [] : retainedHitTargets.ToArray();
        for (var i = 0; i < actionNodes.Count; i++)
        {
            var actionNode = actionNodes[i];
            var retainedHitTarget = retainedHitTargets[i];
            if (dirtySet.Contains(actionNode.DfsIndex))
            {
                patched[i] = new HitTestTarget(retainedHitTarget.Bounds, actionNode.ActionId, retainedHitTarget.ClipBounds);
                continue;
            }

            if (retainedHitTarget.ActionId != actionNode.ActionId)
            {
                return HitTargetMetadataProjection.CreateFallback();
            }
        }

        return HitTargetMetadataProjection.CreateSucceeded(patched);
    }

    private static bool TryCollectActionNodes(VirtualNode retainedNode, VirtualNode nextNode, ref int dfsIndex, List<ActionNodeMetadata> actionNodes)
    {
        var currentIndex = dfsIndex;
        if (retainedNode.Kind != nextNode.Kind || retainedNode.Key != nextNode.Key || retainedNode.Children.Length != nextNode.Children.Length)
        {
            return false;
        }

        if (nextNode.Kind == VirtualNodeKind.Button && TryGetActionId(nextNode.Attributes, out var actionId))
        {
            actionNodes.Add(new ActionNodeMetadata(currentIndex, actionId));
        }

        dfsIndex++;
        for (var i = 0; i < retainedNode.Children.Length; i++)
        {
            if (!TryCollectActionNodes(retainedNode.Children[i], nextNode.Children[i], ref dfsIndex, actionNodes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetActionId(VirtualNodeAttribute[] attributes, out string actionId)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Name == "ActionId" && attribute.Value.Kind == AttributeValueKind.Text && !string.IsNullOrWhiteSpace(attribute.Value.Text))
            {
                actionId = attribute.Value.Text;
                return true;
            }
        }

        actionId = string.Empty;
        return false;
    }

    private readonly record struct ActionNodeMetadata(int DfsIndex, string ActionId);
}