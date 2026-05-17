namespace Irix.Rendering;

internal readonly struct RenderScratchBuffer
{
    public ScratchList<LayoutElement> RentLayoutElementList(int capacity = 0) =>
        new FrameScratchArena().RentList<LayoutElement>(capacity);

    public ScratchList<LayoutTreeNode> RentLayoutTreeNodeList(int capacity = 0) =>
        new FrameScratchArena().RentList<LayoutTreeNode>(capacity);

    public ScratchList<ScrollContainerDiag> RentScrollContainerDiagList(int capacity = 0) =>
        new FrameScratchArena().RentList<ScrollContainerDiag>(capacity);

    public ScratchList<(int Start, int Count)> RentRangeList(int capacity = 0) =>
        new FrameScratchArena().RentList<(int Start, int Count)>(capacity);

    public ScratchList<LayoutDirtyClassification> RentLayoutDirtyClassificationList(int capacity = 0) =>
        new FrameScratchArena().RentList<LayoutDirtyClassification>(capacity);

    public ScratchList<int> RentIntList(int capacity = 0) => new FrameScratchArena().RentIntList(capacity);
}
