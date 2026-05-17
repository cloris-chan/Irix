using System.Buffers;

namespace Irix;

public enum PatchBatchKind
{
    /// <summary>Normal diff batch: contains VirtualNode patches from the runtime.</summary>
    Diff,

    /// <summary>Explicit render request: no patches, signals compositor to re-render
    /// (e.g., after viewport/resize change). Coalesced by CompositorLoop.</summary>
    RenderRequest
}

public sealed class PatchBatch : IDisposable
{
    private readonly IMemoryOwner<VirtualNodePatch> _owner;

    public PatchBatch(VirtualNode root, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0, TextBufferSnapshot textSnapshot = default, bool hasCanonicalRoot = false)
        : this(root, owner, count, screenId, PatchBatchKind.Diff, textSnapshot, hasCanonicalRoot)
    {
    }

    public PatchBatch(VirtualNode root, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId, PatchBatchKind kind, TextBufferSnapshot textSnapshot = default, bool hasCanonicalRoot = false)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (count < 0 || count > owner.Memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Root = root;
        _owner = owner;
        Count = count;
        ScreenId = screenId;
        Kind = kind;
        TextSnapshot = textSnapshot;
        HasCanonicalRoot = hasCanonicalRoot;
    }

    public PatchBatch(IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0)
        : this(default, owner, count, screenId)
    {
    }

    public static PatchBatch CreateRenderRequest(int screenId = 0)
    {
        return new PatchBatch(default, new EmptyMemoryOwner(), 0, screenId, PatchBatchKind.RenderRequest);
    }

    public VirtualNode Root { get; }

    public int Count { get; }

    public int ScreenId { get; }

    public PatchBatchKind Kind { get; }

    public TextBufferSnapshot TextSnapshot { get; }

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
