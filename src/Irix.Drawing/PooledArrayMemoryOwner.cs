using System.Buffers;
using System.Runtime.CompilerServices;

namespace Irix.Drawing;

public sealed class PooledArrayMemoryOwner<T> : IMemoryOwner<T>
{
    private readonly int _length;
    private T[]? _array;

    private PooledArrayMemoryOwner(T[] array, int length)
    {
        _array = array;
        _length = length;
    }

    public Memory<T> Memory => _array is null ? Memory<T>.Empty : _array.AsMemory(0, _length);

    public static PooledArrayMemoryOwner<T> Rent(int minimumLength)
    {
        if (minimumLength <= 0)
        {
            return new PooledArrayMemoryOwner<T>([], 0);
        }

        return new PooledArrayMemoryOwner<T>(ArrayPool<T>.Shared.Rent(minimumLength), minimumLength);
    }

    public void Dispose()
    {
        var array = _array;
        if (array is null)
        {
            return;
        }

        _array = null;
        if (array.Length > 0)
        {
            ArrayPool<T>.Shared.Return(array, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }
}