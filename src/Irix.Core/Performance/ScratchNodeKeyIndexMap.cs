using System.Buffers;

namespace Irix;

internal ref struct ScratchNodeKeyIndexMap
{
    private Span<Entry> _entries;
    private Entry[]? _pooled;
    private int _mask;

    private ScratchNodeKeyIndexMap(Span<Entry> entries, int capacity)
    {
        _entries = entries[..capacity];
        _pooled = null;
        _mask = capacity - 1;
        _entries.Clear();
    }

    private ScratchNodeKeyIndexMap(Entry[] pooled, int capacity)
    {
        _entries = pooled.AsSpan(0, capacity);
        _pooled = pooled;
        _mask = capacity - 1;
        _entries.Clear();
    }

    public static ScratchNodeKeyIndexMap Create(Span<Entry> initialBuffer, int itemCapacity)
    {
        var capacity = RequiredCapacity(itemCapacity);
        if (initialBuffer.Length >= capacity)
        {
            return new ScratchNodeKeyIndexMap(initialBuffer, capacity);
        }

        var pooled = ArrayPool<Entry>.Shared.Rent(capacity);
        return new ScratchNodeKeyIndexMap(pooled, capacity);
    }

    public static ScratchNodeKeyIndexMap Rent(int itemCapacity)
    {
        var capacity = RequiredCapacity(itemCapacity);
        var pooled = ArrayPool<Entry>.Shared.Rent(capacity);
        return new ScratchNodeKeyIndexMap(pooled, capacity);
    }

    public void Set(NodeKey key, int value)
    {
        if (key == NodeKey.None)
        {
            return;
        }

        ref var entry = ref _entries[Probe(key)];
        entry = new Entry(key, value, occupied: true);
    }

    public readonly bool TryGet(NodeKey key, out int value)
    {
        if (key == NodeKey.None || _entries.IsEmpty)
        {
            value = -1;
            return false;
        }

        var slot = Hash(key) & _mask;
        while (_entries[slot].Occupied)
        {
            if (_entries[slot].Key == key)
            {
                value = _entries[slot].Value;
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
        var pooled = _pooled;
        if (pooled is not null)
        {
            _pooled = null;
            // Entry is pure value state. Occupied controls validity and rentals clear
            // the active capacity, so returning without clear does not retain refs.
            ArrayPool<Entry>.Shared.Return(pooled);
        }

        _entries = default;
        _mask = 0;
    }

    private int Probe(NodeKey key)
    {
        var slot = Hash(key) & _mask;
        while (_entries[slot].Occupied && _entries[slot].Key != key)
        {
            slot = (slot + 1) & _mask;
        }

        return slot;
    }

    private static int RequiredCapacity(int itemCapacity) => NextPowerOfTwo(Math.Max(itemCapacity * 2, 4));

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

    internal readonly struct Entry(NodeKey key, int value, bool occupied)
    {
        public NodeKey Key { get; } = key;
        public int Value { get; } = value;
        public bool Occupied { get; } = occupied;
    }
}
