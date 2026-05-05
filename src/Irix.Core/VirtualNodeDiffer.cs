namespace Irix;

public static class VirtualNodeDiffer
{
    public static PatchBatch CreatePatchBatch(VirtualNodeTree previousTree, VirtualNodeTree nextTree, int screenId = 0)
    {
        if (NodesEqual(previousTree.Root, nextTree.Root))
        {
            return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, screenId);
        }

        var patches = new[]
        {
            new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, 0, nextTree.Root, screenId)
        };

        return new PatchBatch(nextTree.Root, new PatchMemoryOwner<VirtualNodePatch>(patches), patches.Length, screenId);
    }

    internal static bool NodesEqual(VirtualNode a, VirtualNode b)
    {
        if (a.Kind != b.Kind || a.Key != b.Key || a.Content != b.Content)
        {
            return false;
        }

        var aAttrs = a.Attributes ?? [];
        var bAttrs = b.Attributes ?? [];
        if (aAttrs.Length != bAttrs.Length)
        {
            return false;
        }

        for (var i = 0; i < aAttrs.Length; i++)
        {
            if (aAttrs[i] != bAttrs[i])
            {
                return false;
            }
        }

        var aChildren = a.Children ?? [];
        var bChildren = b.Children ?? [];
        if (aChildren.Length != bChildren.Length)
        {
            return false;
        }

        for (var i = 0; i < aChildren.Length; i++)
        {
            if (!NodesEqual(aChildren[i], bChildren[i]))
            {
                return false;
            }
        }

        return true;
    }
}
