namespace Irix.Rendering;

internal readonly struct RenderScratchBuffer
{
    private readonly FrameScratchArena _arena = new();

    public RenderScratchBuffer()
    {
    }

    public ScratchList<LayoutElement> CreateLayoutElementList(Span<LayoutElement> initialBuffer) =>
        _arena.CreateList(initialBuffer);

    public ScratchList<LayoutTreeNode> CreateLayoutTreeNodeList(Span<LayoutTreeNode> initialBuffer) =>
        _arena.CreateList(initialBuffer);

    public ScratchList<LayoutElementRange> CreateLayoutElementRangeList(Span<LayoutElementRange> initialBuffer) =>
        _arena.CreateList(initialBuffer);

    public ScratchList<ScrollContainerDiag> CreateScrollContainerDiagList(Span<ScrollContainerDiag> initialBuffer) =>
        _arena.CreateList(initialBuffer);

    public ScratchList<(int Start, int Count)> CreateRangeList(Span<(int Start, int Count)> initialBuffer) =>
        _arena.CreateList(initialBuffer);

    public ScratchList<LayoutDirtyClassification> CreateLayoutDirtyClassificationList(Span<LayoutDirtyClassification> initialBuffer) =>
        _arena.CreateList(initialBuffer);

    public ScratchIntSet CreateDirtyIndexSet(Span<int> initialBuffer) => _arena.CreateIntSet(initialBuffer);

    public ScratchList<int> CreateDirtyIndexList(Span<int> initialBuffer) => _arena.CreateIntList(initialBuffer);

    public ScratchList<LayoutElement> RentLayoutElementList(int capacity = 0) =>
        _arena.RentList<LayoutElement>(capacity);

    public ScratchList<ScrollContainerDiag> RentScrollContainerDiagList(int capacity = 0) =>
        _arena.RentList<ScrollContainerDiag>(capacity);

    public ScratchList<(int Start, int Count)> RentRangeList(int capacity = 0) =>
        _arena.RentList<(int Start, int Count)>(capacity);

    public ScratchList<LayoutDirtyClassification> RentLayoutDirtyClassificationList(int capacity = 0) =>
        _arena.RentList<LayoutDirtyClassification>(capacity);

}
