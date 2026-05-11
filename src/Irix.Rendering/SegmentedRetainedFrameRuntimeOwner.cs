namespace Irix.Rendering;

internal sealed class SegmentedRetainedFrameRuntimeOwner : IDisposable
{
    private readonly SegmentedRetainedFrameOwner _owner = new();
    private bool _disposed;

    public int CommandCount => _owner.CommandCount;

    public VirtualNode RetainedRoot => _owner.RetainedRoot;

    public IReadOnlyList<RetainedResourceSegment> ResourceSegments => _owner.ResourceSegments;

    public SegmentedRetainedFrameShadowResult ApplyFull(RenderFrameBatch batch, VirtualNode retainedRoot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _owner.ApplyFull(batch, RetainedResourceSnapshot.Capture(batch.Resources), retainedRoot);
        return new SegmentedRetainedFrameShadowResult(
            SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull,
            RetainedPartialApplyFallbackReason.None,
            RetainedPartialApplyResultKind.FallbackFull,
            _owner.ReadSegments());
    }

    public bool TryAcceptPartial(RenderFrameBatch batch, RetainedRootMetadataPatch rootPatch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _owner.TryAcceptPartial(batch, RetainedResourceSnapshot.Capture(batch.Resources), rootPatch);
    }

    public SegmentedRetainedFrameShadowResult ApplyFallbackFull(RenderFrameBatch batch, VirtualNode retainedRoot, RetainedPartialApplyFallbackReason reason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _owner.ApplyFull(batch, RetainedResourceSnapshot.Capture(batch.Resources), retainedRoot);
        return new SegmentedRetainedFrameShadowResult(
            SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull,
            reason,
            RetainedPartialApplyResultKind.FallbackFull,
            _owner.ReadSegments());
    }

    public SegmentedRetainedFrameShadowResult Rebuild(RenderFrameBatch batch, VirtualNode retainedRoot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _owner.Invalidate();
        return ApplyFull(batch, retainedRoot);
    }

    public IReadOnlyList<SegmentedFrameRead> ReadSegments()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _owner.ReadSegments();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _owner.Dispose();
        _disposed = true;
    }
}