namespace Irix;

/// <summary>
/// Synchronous per-frame scratch rentals for hot paths. Returned spans/lists must
/// never be stored in retained state or cross an async boundary.
/// </summary>
internal readonly struct FrameScratchArena
{
    public ScratchSpan<int> RentIntSpan(int length) => ScratchSpan<int>.Rent(length);

    public ScratchSpan<NodeIndexEntry> RentNodeIndexSpan(int length) => ScratchSpan<NodeIndexEntry>.Rent(length);

    public ScratchList<int> CreateIntList(Span<int> initialBuffer) => ScratchList<int>.Create(initialBuffer);

    public ScratchList<T> CreateList<T>(Span<T> initialBuffer) => ScratchList<T>.Create(initialBuffer);

    public ScratchIntSet CreateIntSet(Span<int> initialBuffer) => ScratchIntSet.Create(initialBuffer);

    public ScratchNodeKeyIndexMap CreateNodeKeyIndexMap(Span<ScratchNodeKeyIndexMap.Entry> initialBuffer, int itemCapacity) =>
        ScratchNodeKeyIndexMap.Create(initialBuffer, itemCapacity);

    public ScratchList<int> RentIntList(int capacity = 0) => ScratchList<int>.Rent(capacity);

    public ScratchList<NodeIndexEntry> RentNodeIndexList(int capacity = 0) => ScratchList<NodeIndexEntry>.Rent(capacity);

    public ScratchList<VirtualNodePatch> RentVirtualNodePatchList(int capacity = 0) => ScratchList<VirtualNodePatch>.Rent(capacity);

    public ScratchNodeKeyIndexMap RentNodeKeyIndexMap(int itemCapacity) => ScratchNodeKeyIndexMap.Rent(itemCapacity);

    public ScratchList<T> RentList<T>(int capacity = 0) => ScratchList<T>.Rent(capacity);
}
