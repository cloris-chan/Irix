using Irix.Drawing;

namespace Irix.Rendering;

internal sealed class SegmentedRetainedFramePrototype : IDisposable
{
    private readonly RetainedCommandBuffer _commandBuffer = new();
    private readonly RetainedResourceSegmentTable _resourceSegments = new();
    private VirtualNode _retainedRoot;
    private bool _disposed;

    public int CommandCount => _commandBuffer.Count;

    public VirtualNode RetainedRoot => _retainedRoot;

    public IReadOnlyList<RetainedResourceSegment> ResourceSegments => _resourceSegments.Segments;

    public void ApplyFull(DrawCommandBatch commands, RetainedResourceSnapshot snapshot, VirtualNode retainedRoot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _commandBuffer.ApplyFull(commands);
        _resourceSegments.ApplyFull(_commandBuffer.Count, snapshot);
        _retainedRoot = retainedRoot;
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
        _disposed = true;
    }
}