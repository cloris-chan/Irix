namespace Irix;

internal ref struct ScratchIntSet
{
    private const int LinearThreshold = 8;

    private ScratchList<int> _items;
    private bool _sorted;

    private ScratchIntSet(Span<int> initialBuffer)
    {
        _items = ScratchList<int>.Create(initialBuffer);
        _sorted = false;
    }

    public readonly int Count => _items.Count;

    public readonly ReadOnlySpan<int> Written => _items.Written;

    public static ScratchIntSet Create(Span<int> initialBuffer) => new(initialBuffer);

    public bool Add(int value)
    {
        if (Contains(value))
        {
            return false;
        }

        _items.Add(value);
        _sorted = false;
        return true;
    }

    public bool Contains(int value)
    {
        if (_items.Count <= LinearThreshold)
        {
            foreach (var item in _items.Written)
            {
                if (item == value)
                {
                    return true;
                }
            }

            return false;
        }

        EnsureSorted();
        return _items.Written.BinarySearch(value) >= 0;
    }

    public int[] ToSortedArray()
    {
        if (_items.Count == 0)
        {
            return [];
        }

        EnsureSorted();
        return _items.ToArray();
    }

    public void Dispose()
    {
        _items.Dispose();
        _sorted = false;
    }

    private void EnsureSorted()
    {
        if (_sorted)
        {
            return;
        }

        _items.Sort();
        _sorted = true;
    }
}
