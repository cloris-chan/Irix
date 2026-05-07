using System.Buffers;

namespace Irix.Drawing;

/// <summary>
/// A reusable growable span-backed list for frame-local data.
/// Uses ArrayPool for backing storage; Dispose returns memory to pool.
/// </summary>
public sealed class FrameRenderList<T> : IDisposable where T : struct
{
    private T[]? _buffer;
    private int _count;

    public int Count => _count;

    public ReadOnlySpan<T> Span => _buffer is null ? default : _buffer.AsSpan(0, _count);

    public void Add(T item)
    {
        EnsureCapacity(_count + 1);
        _buffer![_count++] = item;
    }

    public void Reset()
    {
        _count = 0;
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null)
        {
            return;
        }

        _buffer = null;
        _count = 0;
        ArrayPool<T>.Shared.Return(buffer);
    }

    private void EnsureCapacity(int required)
    {
        if (_buffer is not null && _buffer.Length >= required)
        {
            return;
        }

        var newBuffer = ArrayPool<T>.Shared.Rent(required);
        if (_buffer is not null)
        {
            _buffer.AsSpan(0, _count).CopyTo(newBuffer);
            ArrayPool<T>.Shared.Return(_buffer);
        }

        _buffer = newBuffer;
    }
}
