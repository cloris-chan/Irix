using System.Buffers;

namespace Irix.Drawing;

public readonly record struct DrawCommandBatch(
    IMemoryOwner<DrawCommand> Owner,
    int Count) : IDisposable
{
    public Memory<DrawCommand> Memory
    {
        get
        {
            var memory = Owner.Memory;
            return memory.Length < Count ? memory : memory[..Count];
        }
    }

    public void Dispose()
    {
        Owner.Dispose();
    }
}
