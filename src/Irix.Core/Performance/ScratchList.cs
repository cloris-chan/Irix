using System.Buffers;
using System.Runtime.CompilerServices;

namespace Irix;

internal ref struct ScratchList<T>
{
    private const int MinimumPoolCapacity = 4;

    private Span<T> _initialBuffer;
    private T[]? _pooled;
    private int _count;

    private ScratchList(Span<T> initialBuffer)
    {
        _initialBuffer = initialBuffer;
        _pooled = null;
        _count = 0;
    }

    private ScratchList(T[] pooled)
    {
        _initialBuffer = default;
        _pooled = pooled;
        _count = 0;
    }

    public readonly int Count => _count;

    public int Capacity => Active.Length;

    public ReadOnlySpan<T> Written => Active[.._count];

    public Span<T> WrittenMutable => Active[.._count];

    private Span<T> Active => _pooled is not null
        ? _pooled
        : _initialBuffer;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Active[index];
        }
        set
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Active[index] = value;
        }
    }

    public static ScratchList<T> Create(Span<T> initialBuffer) => new(initialBuffer);

    public static ScratchList<T> Rent(int capacity = 0)
    {
        var effectiveCapacity = Math.Max(capacity, MinimumPoolCapacity);
        return new ScratchList<T>(ArrayPool<T>.Shared.Rent(effectiveCapacity));
    }

    public void Add(T item)
    {
        EnsureCapacity(_count + 1);
        Active[_count++] = item;
    }

    public void AddRange(ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
        {
            return;
        }

        EnsureCapacity(_count + items.Length);
        items.CopyTo(Active[_count..]);
        _count += items.Length;
    }

    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Active[.._count].Clear();
        }

        _count = 0;
    }

    public void Sort()
    {
        if (_count > 1)
        {
            WrittenMutable.Sort();
        }
    }

    public void Sort(IComparer<T> comparer)
    {
        if (_count > 1)
        {
            WrittenMutable.Sort(comparer);
        }
    }

    public T[] ToArray()
    {
        if (_count == 0)
        {
            return [];
        }

        return Written.ToArray();
    }

    public void Dispose()
    {
        var pooled = _pooled;
        if (pooled is not null)
        {
            _pooled = null;
            ArrayPool<T>.Shared.Return(pooled, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
        else if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _initialBuffer[.._count].Clear();
        }

        _initialBuffer = default;
        _count = 0;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= Active.Length)
        {
            return;
        }

        var active = Active;
        var newCapacity = Math.Max(requiredCapacity, Math.Max(active.Length * 2, MinimumPoolCapacity));
        var next = ArrayPool<T>.Shared.Rent(newCapacity);
        active[.._count].CopyTo(next);

        var previous = _pooled;
        if (previous is not null)
        {
            ArrayPool<T>.Shared.Return(previous, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
        else if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            active[.._count].Clear();
        }

        _pooled = next;
    }
}
