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

    public PatchBatch(VirtualNode root, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0, TextBufferSnapshot textSnapshot = default)
        : this(root, owner, count, screenId, PatchBatchKind.Diff, textSnapshot)
    {
    }

    public PatchBatch(VirtualNode root, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId, PatchBatchKind kind, TextBufferSnapshot textSnapshot = default)
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
