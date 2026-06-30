using System.Buffers;

namespace Irix;

internal enum PatchBatchKind
{
    /// <summary>Normal diff batch: contains VirtualNode patches from the runtime.</summary>
    Diff,

    /// <summary>Explicit render request: no patches, signals compositor to re-render
    /// (e.g., after viewport/resize change). Coalesced by CompositorLoop.</summary>
    RenderRequest
}

internal sealed class PatchBatch : IDisposable
{
    private readonly IMemoryOwner<VirtualNodePatch> _owner;
    private readonly VirtualNodeTree _tree;

    public PatchBatch(VirtualNode root, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0, TextBufferSnapshot textSnapshot = default, bool hasCanonicalRoot = false)
        : this(new VirtualNodeTree(root, textSnapshot), owner, count, screenId, PatchBatchKind.Diff, hasCanonicalRoot)
    {
    }

    public PatchBatch(VirtualNode root, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId, PatchBatchKind kind, TextBufferSnapshot textSnapshot = default, bool hasCanonicalRoot = false)
        : this(new VirtualNodeTree(root, textSnapshot), owner, count, screenId, kind, hasCanonicalRoot)
    {
    }

    public PatchBatch(VirtualNodeTree tree, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0, bool hasCanonicalRoot = false)
        : this(tree, owner, count, screenId, PatchBatchKind.Diff, hasCanonicalRoot)
    {
    }

    public PatchBatch(VirtualNodeTree tree, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId, PatchBatchKind kind, bool hasCanonicalRoot = false)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (count < 0 || count > owner.Memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _tree = tree;
        _owner = owner;
        Count = count;
        ScreenId = screenId;
        Kind = kind;
        HasCanonicalRoot = hasCanonicalRoot;
    }

    public PatchBatch(IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0)
        : this(default(VirtualNode), owner, count, screenId)
    {
    }

    public static PatchBatch CreateRenderRequest(int screenId = 0)
    {
        return new PatchBatch(default(VirtualNode), new EmptyMemoryOwner(), 0, screenId, PatchBatchKind.RenderRequest);
    }

    public VirtualNode Root => _tree.Root;

    public VirtualNodeTree Tree => _tree;

    public int Count { get; }

    public int ScreenId { get; }

    public PatchBatchKind Kind { get; }

    public TextBufferSnapshot TextSnapshot => _tree.TextSnapshot;

    /// <summary>
    /// True when <see cref="Root"/> is the canonical next retained root for this batch.
    /// Differ-created batches set this even when <see cref="Count"/> is zero so retained
    /// metadata can advance to the next tree and text snapshot atomically.
    /// Hand-authored patch batches default to false; retained application rejects
    /// non-empty non-canonical batches.
    /// </summary>
    public bool HasCanonicalRoot { get; }

    public Memory<VirtualNodePatch> Memory => _owner.Memory[..Count];

    public void Dispose()
    {
        _owner.Dispose();
    }

    private sealed class EmptyMemoryOwner : IMemoryOwner<VirtualNodePatch>
    {
        public Memory<VirtualNodePatch> Memory => Memory<VirtualNodePatch>.Empty;
        public void Dispose() { }
    }
}
