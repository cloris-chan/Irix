using Irix.Drawing;

namespace Irix.Rendering;

internal sealed class SegmentedRetainedFrameRuntimeOwner(Func<IFrameResourceResolver, RetainedResourceSnapshot>? captureSnapshot = null) : IDisposable
{
    private readonly Func<IFrameResourceResolver, RetainedResourceSnapshot> _captureSnapshot = captureSnapshot ?? (resolver => RetainedResourceSnapshot.Capture(resolver));
    private readonly SegmentedRetainedFrameOwner _owner = new();
    private bool _disposed;

    public int CommandCount => _owner.CommandCount;

    public VirtualNode RetainedRoot => _owner.RetainedRoot;

    public IReadOnlyList<HitTestTarget> HitTargets => _owner.HitTargets;

    public IReadOnlyList<RetainedResourceSegment> ResourceSegments => _owner.ResourceSegments;

    public SegmentedRetainedFrameShadowResult ApplyFull(RenderFrameBatch batch, VirtualNode retainedRoot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _owner.ApplyFull(batch, _captureSnapshot(batch.Resources), retainedRoot);
        return new SegmentedRetainedFrameShadowResult(
            SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull,
            RetainedPartialApplyFallbackReason.None,
            RetainedPartialApplyResultKind.FallbackFull,
            _owner.ReadSegments());
    }

    public bool TryAcceptPartial(RenderFrameBatch batch, RetainedRootMetadataPatch rootPatch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _owner.TryAcceptPartial(batch, _captureSnapshot(batch.Resources), rootPatch);
    }

    public bool TryAcceptPartial(RenderFrameBatch batch, RetainedRootMetadataPatch rootPatch, IReadOnlyList<HitTestTarget> hitTargets)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _owner.TryAcceptPartial(batch, _captureSnapshot(batch.Resources), rootPatch, hitTargets);
    }

    public SegmentedRetainedFrameShadowResult ApplyFallbackFull(RenderFrameBatch batch, VirtualNode retainedRoot, RetainedPartialApplyFallbackReason reason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _owner.ApplyFull(batch, _captureSnapshot(batch.Resources), retainedRoot);
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

    public void Invalidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _owner.Invalidate();
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