using System.Buffers;

namespace Irix.Drawing;

public sealed class ArrayMemoryOwner<T>(T[] array) : IMemoryOwner<T>
{
    private T[]? _array = array;

    public Memory<T> Memory => _array ?? Memory<T>.Empty;

    public void Dispose()
    {
        _array = null;
    }
}
