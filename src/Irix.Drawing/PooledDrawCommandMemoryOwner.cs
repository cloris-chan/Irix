using System.Buffers;
using System.Runtime.CompilerServices;

namespace Irix.Drawing;

internal sealed class PooledDrawCommandMemoryOwner : IMemoryOwner<DrawCommand>
{
    private const int MaxOwnerPoolSize = 64;

    private static readonly Lock OwnerPoolLock = new();
    private static readonly Queue<PooledDrawCommandMemoryOwner> OwnerPool = new();

    private DrawCommand[]? _array;
    private int _length;
    private ulong _generation;
    private bool _active;

    private PooledDrawCommandMemoryOwner()
    {
    }

    public Memory<DrawCommand> Memory => _active && _array is not null ? _array.AsMemory(0, _length) : Memory<DrawCommand>.Empty;

    internal ulong Generation => _generation;

    public static PooledDrawCommandMemoryOwner Rent(int minimumLength)
    {
        var owner = RentOwner();
        owner.Activate(minimumLength);
        return owner;
    }

    internal Memory<DrawCommand> GetMemory(ulong generation) =>
        _active && _generation == generation && _array is not null
            ? _array.AsMemory(0, _length)
            : Memory<DrawCommand>.Empty;

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

    private static PooledDrawCommandMemoryOwner RentOwner()
    {
        lock (OwnerPoolLock)
        {
            if (OwnerPool.Count > 0)
            {
                return OwnerPool.Dequeue();
            }
        }

        return new PooledDrawCommandMemoryOwner();
    }

    private static void ReturnOwner(PooledDrawCommandMemoryOwner owner)
    {
        lock (OwnerPoolLock)
        {
            if (OwnerPool.Count < MaxOwnerPoolSize)
            {
                OwnerPool.Enqueue(owner);
                return;
            }
        }

        owner.ReleaseStorage();
    }

    private void Activate(int minimumLength)
    {
        _generation++;
        if (_generation == 0)
        {
            _generation = 1;
        }

        if (minimumLength > 0 && (_array is null || _array.Length < minimumLength))
        {
            ReleaseStorage();
            _array = ArrayPool<DrawCommand>.Shared.Rent(minimumLength);
        }

        _length = Math.Max(minimumLength, 0);
        _active = true;
    }

    private void DisposeCore()
    {
        _length = 0;
        _active = false;
        ReturnOwner(this);
    }

    private void ReleaseStorage()
    {
        var array = _array;
        _array = null;
        _length = 0;
        _active = false;
        if (array is not null && array.Length > 0)
        {
            ArrayPool<DrawCommand>.Shared.Return(array, RuntimeHelpers.IsReferenceOrContainsReferences<DrawCommand>());
        }
    }
}
