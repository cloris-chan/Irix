namespace Irix;

public static class VirtualNodeDiffer
{
    public static PatchBatch CreatePatchBatch(VirtualNodeTree previousTree, VirtualNodeTree nextTree, int screenId = 0)
    {
        var patches = new[]
        {
            new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, 0, nextTree.Root, screenId)
        };

        return new PatchBatch(new PatchMemoryOwner<VirtualNodePatch>(patches), patches.Length, screenId);
    }
}
