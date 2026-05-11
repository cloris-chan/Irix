using Irix.Drawing;

namespace Irix.Rendering;

internal sealed class SegmentedRetainedFrameOwner : IDisposable
{
    private readonly RetainedCommandBuffer _commandBuffer = new();
    private readonly RetainedResourceSegmentTable _resourceSegments = new();
    private VirtualNode _retainedRoot;
    private HitTestTarget[] _hitTargets = [];
    private bool _disposed;

    public int CommandCount => _commandBuffer.Count;

    public VirtualNode RetainedRoot => _retainedRoot;

    public IReadOnlyList<HitTestTarget> HitTargets => _hitTargets;

    public IReadOnlyList<RetainedResourceSegment> ResourceSegments => _resourceSegments.Segments;

    public void ApplyFull(DrawCommandBatch commands, RetainedResourceSnapshot snapshot, VirtualNode retainedRoot, IReadOnlyList<HitTestTarget>? hitTargets = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _commandBuffer.ApplyFull(commands);
        if (_commandBuffer.Count == 0)
        {
            _resourceSegments.Invalidate();
        }
        else
        {
            _resourceSegments.ApplyFull(_commandBuffer.Count, snapshot);
        }

        _retainedRoot = retainedRoot;
        _hitTargets = hitTargets is null ? [] : hitTargets.ToArray();
    }

    public void ApplyFull(RenderFrameBatch batch, RetainedResourceSnapshot snapshot, VirtualNode retainedRoot)
    {
        ApplyFull(batch.Commands, snapshot, retainedRoot, batch.HitTargets);
    }

    public bool TryAcceptPartial(RenderFrameBatch batch, RetainedResourceSnapshot replacementSnapshot, RetainedRootMetadataPatch rootPatch)
    {
        return TryAcceptPartial(batch, replacementSnapshot, rootPatch, _hitTargets);
    }

    public bool TryAcceptPartial(RenderFrameBatch batch, RetainedResourceSnapshot replacementSnapshot, RetainedRootMetadataPatch rootPatch, IReadOnlyList<HitTestTarget> hitTargets)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!rootPatch.Succeeded || _commandBuffer.Count == 0 || batch.Commands.Count != _commandBuffer.Count || batch.DirtyCommandRanges.Count == 0)
        {
            return false;
        }

        if (!_resourceSegments.TryAcceptPartial(batch.DirtyCommandRanges, replacementSnapshot))
        {
            return false;
        }

        _commandBuffer.ApplyPartial(batch.Commands, batch.DirtyCommandRanges);
        _retainedRoot = rootPatch.Root;
        _hitTargets = hitTargets.ToArray();
        return true;
    }

    public IReadOnlyList<SegmentedFrameRead> ReadSegments()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SegmentedRetainedFrameReader(_commandBuffer, _resourceSegments).ReadSegments();
    }

    public void Invalidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _commandBuffer.Reset();
        _resourceSegments.Invalidate();
        _retainedRoot = default;
        _hitTargets = [];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _commandBuffer.Dispose();
        _resourceSegments.Dispose();
        _retainedRoot = default;
        _hitTargets = [];
        _disposed = true;
    }
}