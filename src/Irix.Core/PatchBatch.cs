using System.Buffers;

namespace Irix;

public sealed class PatchBatch : IDisposable
{
    private readonly IMemoryOwner<VirtualNodePatch> _owner;

    public PatchBatch(IMemoryOwner<VirtualNodePatch> owner, int count, int screenId = 0)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (count < 0 || count > owner.Memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _owner = owner;
        Count = count;
        ScreenId = screenId;
    }

    public int Count { get; }

    public int ScreenId { get; }

    public Memory<VirtualNodePatch> Memory => _owner.Memory[..Count];

    public void Dispose()
    {
        _owner.Dispose();
    }
}
