using System.Buffers;
using System.Runtime.CompilerServices;

namespace Irix;

/// <summary>
/// Synchronous per-frame scratch rentals for hot paths. Returned spans/lists must
/// never be stored in retained state or cross an async boundary.
/// </summary>
internal readonly struct FrameScratchArena
{
    public ScratchSpan<int> RentIntSpan(int length) => ScratchSpan<int>.Rent(length);

    public ScratchSpan<NodeIndexEntry> RentNodeIndexSpan(int length) => ScratchSpan<NodeIndexEntry>.Rent(length);

    public ScratchList<int> RentIntList(int capacity = 0) => ScratchList<int>.Rent(capacity);

    public ScratchList<NodeIndexEntry> RentNodeIndexList(int capacity = 0) => ScratchList<NodeIndexEntry>.Rent(capacity);

    public ScratchList<VirtualNodePatch> RentVirtualNodePatchList(int capacity = 0) => ScratchList<VirtualNodePatch>.Rent(capacity);

    public ScratchNodeKeyIndexMap RentNodeKeyIndexMap(int itemCapacity) => ScratchNodeKeyIndexMap.Rent(itemCapacity);

    public ScratchList<T> RentList<T>(int capacity = 0) => ScratchList<T>.Rent(capacity);
}

internal readonly struct NodeIndexEntry(int dfsIndex, int parentDfsIndex) : IEquatable<NodeIndexEntry>
{
    public int DfsIndex { get; } = dfsIndex;
    public int ParentDfsIndex { get; } = parentDfsIndex;

    public bool Equals(NodeIndexEntry other) => DfsIndex == other.DfsIndex && ParentDfsIndex == other.ParentDfsIndex;

    public override bool Equals(object? obj) => obj is NodeIndexEntry other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DfsIndex, ParentDfsIndex);

    public static bool operator ==(NodeIndexEntry left, NodeIndexEntry right) => left.Equals(right);

    public static bool operator !=(NodeIndexEntry left, NodeIndexEntry right) => !left.Equals(right);
}

internal ref struct ScratchSpan<T>
{
    private T[]? _buffer;
    private readonly int _length;

    private ScratchSpan(T[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    public readonly Span<T> Span => _buffer is null ? Span<T>.Empty : _buffer.AsSpan(0, _length);

    public static ScratchSpan<T> Rent(int length)
    {
        if (length <= 0)
        {
            return default;
        }

        return new ScratchSpan<T>(ArrayPool<T>.Shared.Rent(length), length);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null)
        {
            return;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(buffer, 0, _length);
        }

        _buffer = null;
        ArrayPool<T>.Shared.Return(buffer);
    }
}

internal ref struct ScratchList<T>
{
    private const int MinimumCapacity = 4;

    private T[]? _items;
    private int _count;

    private ScratchList(T[] items)
    {
        _items = items;
        _count = 0;
    }

    public readonly int Count => _count;

    public readonly int Capacity => _items?.Length ?? 0;

    public readonly ReadOnlySpan<T> Written => _items is null ? ReadOnlySpan<T>.Empty : _items.AsSpan(0, _count);

    public readonly Span<T> WrittenMutable => _items is null ? Span<T>.Empty : _items.AsSpan(0, _count);

    public T this[int index]
    {
        readonly get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _items![index];
        }
        set
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            _items![index] = value;
        }
    }

    public static ScratchList<T> Rent(int capacity = 0)
    {
        var effectiveCapacity = Math.Max(capacity, MinimumCapacity);
        return new ScratchList<T>(ArrayPool<T>.Shared.Rent(effectiveCapacity));
    }

    public void Add(T item)
    {
        EnsureCapacity(_count + 1);
        _items![_count++] = item;
    }

    public void AddRange(ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
        {
            return;
        }

        EnsureCapacity(_count + items.Length);
        items.CopyTo(_items!.AsSpan(_count));
        _count += items.Length;
    }

    public void Clear()
    {
        if (_items is not null && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(_items, 0, _count);
        }

        _count = 0;
    }

    public void Sort()
    {
        if (_count > 1)
        {
            Array.Sort(_items!, 0, _count);
        }
    }

    public void Sort(IComparer<T> comparer)
    {
        if (_count > 1)
        {
            Array.Sort(_items!, 0, _count, comparer);
        }
    }

    public T[] ToArray()
    {
        if (_count == 0)
        {
            return [];
        }

        var result = new T[_count];
        _items!.AsSpan(0, _count).CopyTo(result);
        return result;
    }

    public void Dispose()
    {
        var items = _items;
        if (items is null)
        {
            return;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(items, 0, _count);
        }

        _items = null;
        _count = 0;
        ArrayPool<T>.Shared.Return(items);
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (_items is null)
        {
            _items = ArrayPool<T>.Shared.Rent(Math.Max(requiredCapacity, MinimumCapacity));
            return;
        }

        if (requiredCapacity <= _items.Length)
        {
            return;
        }

        var newCapacity = Math.Max(requiredCapacity, _items.Length * 2);
        var next = ArrayPool<T>.Shared.Rent(newCapacity);
        _items.AsSpan(0, _count).CopyTo(next);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(_items, 0, _count);
        }

        ArrayPool<T>.Shared.Return(_items);
        _items = next;
    }
}

internal ref struct ScratchNodeKeyIndexMap
{
    private NodeKey[]? _keys;
    private int[]? _values;
    private byte[]? _occupied;
    private int _mask;

    private ScratchNodeKeyIndexMap(NodeKey[] keys, int[] values, byte[] occupied, int capacity)
    {
        _keys = keys;
        _values = values;
        _occupied = occupied;
        _mask = capacity - 1;
        Array.Clear(occupied, 0, capacity);
    }

    public static ScratchNodeKeyIndexMap Rent(int itemCapacity)
    {
        var capacity = NextPowerOfTwo(Math.Max(itemCapacity * 2, 4));
        return new ScratchNodeKeyIndexMap(
            ArrayPool<NodeKey>.Shared.Rent(capacity),
            ArrayPool<int>.Shared.Rent(capacity),
            ArrayPool<byte>.Shared.Rent(capacity),
            capacity);
    }

    public void Set(NodeKey key, int value)
    {
        if (key == NodeKey.None)
        {
            return;
        }

        var slot = Probe(key);
        _keys![slot] = key;
        _values![slot] = value;
        _occupied![slot] = 1;
    }

    public readonly bool TryGet(NodeKey key, out int value)
    {
        if (key == NodeKey.None || _keys is null || _values is null || _occupied is null)
        {
            value = -1;
            return false;
        }

        var slot = Hash(key) & _mask;
        while (_occupied[slot] != 0)
        {
            if (_keys[slot] == key)
            {
                value = _values[slot];
                return true;
            }

            slot = (slot + 1) & _mask;
        }

        value = -1;
        return false;
    }

    public readonly bool Contains(NodeKey key) => TryGet(key, out _);

    public void Dispose()
    {
        var keys = _keys;
        var values = _values;
        var occupied = _occupied;
        if (keys is not null)
        {
            ArrayPool<NodeKey>.Shared.Return(keys);
        }

        if (values is not null)
        {
            ArrayPool<int>.Shared.Return(values);
        }

        if (occupied is not null)
        {
            ArrayPool<byte>.Shared.Return(occupied);
        }

        _keys = null;
        _values = null;
        _occupied = null;
        _mask = 0;
    }

    private int Probe(NodeKey key)
    {
        var slot = Hash(key) & _mask;
        while (_occupied![slot] != 0 && _keys![slot] != key)
        {
            slot = (slot + 1) & _mask;
        }

        return slot;
    }

    private static int Hash(NodeKey key)
    {
        var value = key.Value;
        value ^= value >> 16;
        value *= 0x7feb352d;
        value ^= value >> 15;
        value *= 0x846ca68b;
        value ^= value >> 16;
        return unchecked((int)value);
    }

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
