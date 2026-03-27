using System.Buffers;

namespace Irix.Drawing;

public readonly record struct DrawCommandBatch(
    IMemoryOwner<DrawCommand> Owner,
    int Count) : IDisposable
{
    public Memory<DrawCommand> Memory => Owner.Memory;

    public void Dispose()
    {
        Owner.Dispose();
    }
}
