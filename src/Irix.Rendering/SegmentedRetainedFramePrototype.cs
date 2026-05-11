using Irix.Drawing;

namespace Irix.Rendering;

internal sealed class SegmentedRetainedFramePrototype : IDisposable
{
    private readonly SegmentedRetainedFrameOwner _owner = new();

    public int CommandCount => _owner.CommandCount;

    public VirtualNode RetainedRoot => _owner.RetainedRoot;

    public IReadOnlyList<RetainedResourceSegment> ResourceSegments => _owner.ResourceSegments;

    public void ApplyFull(DrawCommandBatch commands, RetainedResourceSnapshot snapshot, VirtualNode retainedRoot)
    {
        _owner.ApplyFull(commands, snapshot, retainedRoot);
    }

    public void ApplyFull(RenderFrameBatch batch, RetainedResourceSnapshot snapshot, VirtualNode retainedRoot)
    {
        _owner.ApplyFull(batch, snapshot, retainedRoot);
    }

    public bool TryAcceptPartial(RenderFrameBatch batch, RetainedResourceSnapshot replacementSnapshot, RetainedRootMetadataPatch rootPatch)
    {
        return _owner.TryAcceptPartial(batch, replacementSnapshot, rootPatch);
    }

    public IReadOnlyList<SegmentedFrameRead> ReadSegments()
    {
        return _owner.ReadSegments();
    }

    public void Invalidate()
    {
        _owner.Invalidate();
    }

    public void Dispose()
    {
        _owner.Dispose();
    }
}