using System.Buffers;

namespace Irix;

internal sealed class PatchMemoryOwner<T> : IMemoryOwner<T>
{
    private T[]? _buffer;

    public PatchMemoryOwner(T[] buffer)
    {
        _buffer = buffer;
    }

    public Memory<T> Memory => _buffer ?? Memory<T>.Empty;

    public void Dispose()
    {
        _buffer = null;
    }
}
