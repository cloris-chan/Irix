using System.Buffers;

namespace Irix;

public sealed class PatchBatch : IDisposable
{
    private readonly IMemoryOwner<VirtualNodePatch> _owner;

    public PatchBatch(VirtualNode root, IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0)
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
    }

    public PatchBatch(IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0)
        : this(default, owner, count, screenId)
    {
    }

    public VirtualNode Root { get; }

    public int Count { get; }

    public int ScreenId { get; }

    public Memory<VirtualNodePatch> Memory => _owner.Memory[..Count];

    public void Dispose()
    {
        _owner.Dispose();
    }
}
