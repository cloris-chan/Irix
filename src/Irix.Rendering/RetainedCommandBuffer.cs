using Irix.Drawing;

namespace Irix.Rendering;

/// <summary>
/// A retained draw command buffer that supports full and partial replacement.
/// Holds the most recent full command batch in memory. When dirty command ranges
/// are available, only those ranges are replaced from a new batch, avoiding
/// a full copy of unchanged commands.
///
/// <para><b>Resource lifecycle constraint:</b></para>
/// <para>
/// <see cref="DrawCommand"/>s may contain <see cref="TextSlice"/> references into
/// a <see cref="FrameDrawingResources"/> arena. These references are only valid while
/// the resources are alive (not returned to the pool). Therefore:
/// </para>
/// <list type="bullet">
///   <item>The buffer is <b>frame-scoped</b>: it must be reset when resources are returned.</item>
///   <item><see cref="ApplyPartial"/> is only valid within the same frame/resource scope.</item>
///   <item>Cross-frame retention requires either full resource snapshots or no TextSlice references.</item>
/// </list>
///
/// <para><b>v0 status:</b> Memory-level validation only. Not wired into D3D12 backend.</para>
/// </summary>
internal sealed class RetainedCommandBuffer : IDisposable
{
    private DrawCommand[] _buffer = [];
    private int _count;

    /// <summary>The current retained commands.</summary>
    public ReadOnlySpan<DrawCommand> Commands => _buffer.AsSpan(0, _count);

    /// <summary>Number of commands in the buffer.</summary>
    public int Count => _count;

    /// <summary>
    /// Replace the entire buffer with the contents of <paramref name="batch"/>.
    /// </summary>
    public void ApplyFull(DrawCommandBatch batch)
    {
        var span = batch.Memory.Span;
        EnsureCapacity(span.Length);
        span.CopyTo(_buffer);
        _count = span.Length;
    }

    /// <summary>
    /// Apply a new batch, replacing only the commands in the specified dirty ranges.
    /// Commands outside the dirty ranges are preserved from the current buffer.
    /// If the buffer is empty or the new batch has a different total count, falls back to full replacement.
    /// </summary>
    public void ApplyPartial(DrawCommandBatch newBatch, IReadOnlyList<(int Start, int Count)> dirtyRanges)
    {
        var newSpan = newBatch.Memory.Span;

        if (_count == 0 || _count != newSpan.Length || dirtyRanges.Count == 0)
        {
            ApplyFull(newBatch);
            return;
        }

        // Copy dirty ranges from new batch into retained buffer
        foreach (var (start, count) in dirtyRanges)
        {
            if (start < 0 || start + count > _count || start + count > newSpan.Length)
            {
                continue; // skip out-of-range
            }

            newSpan.Slice(start, count).CopyTo(_buffer.AsSpan(start, count));
        }
    }

    /// <summary>
    /// Clear the buffer without releasing the underlying array.
    /// </summary>
    public void Reset()
    {
        _count = 0;
    }

    public void Dispose()
    {
        _buffer = [];
        _count = 0;
    }

    private void EnsureCapacity(int required)
    {
        if (_buffer.Length >= required)
        {
            return;
        }

        var newCapacity = Math.Max(required, Math.Max(_buffer.Length * 2, 16));
        var newBuffer = new DrawCommand[newCapacity];
        if (_count > 0)
        {
            _buffer.AsSpan(0, _count).CopyTo(newBuffer);
        }

        _buffer = newBuffer;
    }
}
