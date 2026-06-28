using System.Buffers;
using System.Runtime.CompilerServices;

namespace Irix.Drawing;

internal sealed class PooledArrayMemoryOwner<T> : IMemoryOwner<T>
{
    private const int MaxOwnerPoolSize = 64;

    private static readonly Lock OwnerPoolLock = new();
    private static readonly Queue<PooledArrayMemoryOwner<T>> OwnerPool = new();

    private int _length;
    private T[]? _array;
    private ulong _generation;
    private bool _active;

    private PooledArrayMemoryOwner()
    {
    }

    public Memory<T> Memory => _active && _array is not null ? _array.AsMemory(0, _length) : Memory<T>.Empty;

    internal ulong Generation => _generation;

    public static PooledArrayMemoryOwner<T> Rent(int minimumLength)
    {
        var owner = RentOwner();
        owner.Activate(minimumLength);
        return owner;
    }

    internal Memory<T> GetMemory(ulong generation) =>
        _active && _generation == generation && _array is not null
            ? _array.AsMemory(0, _length)
            : Memory<T>.Empty;

    internal void Dispose(ulong generation)
    {
        if (!_active || _generation != generation)
        {
            return;
        }

        DisposeCore();
    }

    public void Dispose()
    {
        if (!_active)
        {
            return;
        }

        DisposeCore();
    }

    private static PooledArrayMemoryOwner<T> RentOwner()
    {
        lock (OwnerPoolLock)
        {
            if (OwnerPool.Count > 0)
            {
                return OwnerPool.Dequeue();
            }
        }

        return new PooledArrayMemoryOwner<T>();
    }

    private static void ReturnOwner(PooledArrayMemoryOwner<T> owner)
    {
        lock (OwnerPoolLock)
        {
            if (OwnerPool.Count < MaxOwnerPoolSize)
            {
                OwnerPool.Enqueue(owner);
            }
        }
    }

    private void Activate(int minimumLength)
    {
        _generation++;
        if (_generation == 0)
        {
            _generation = 1;
        }

        if (minimumLength <= 0)
        {
            _array = [];
            _length = 0;
            _active = true;
            return;
        }

        _array = ArrayPool<T>.Shared.Rent(minimumLength);
        _length = minimumLength;
        _active = true;
    }

    private void DisposeCore()
    {
        var array = _array;
        _array = null;
        _length = 0;
        _active = false;
        if (array is not null && array.Length > 0)
        {
            ArrayPool<T>.Shared.Return(array, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        ReturnOwner(this);
    }
}
