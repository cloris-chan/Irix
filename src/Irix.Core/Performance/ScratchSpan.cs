using System.Buffers;
using System.Runtime.CompilerServices;

namespace Irix;

internal ref struct ScratchSpan<T>
{
    private T[]? _buffer;
    private readonly int _length;

    private ScratchSpan(T[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    public readonly Span<T> Span => _buffer is null ? Span<T>.Empty : _buffer.AsSpan(0, _length);

    public static ScratchSpan<T> Rent(int length)
    {
        if (length <= 0)
        {
            return default;
        }

        return new ScratchSpan<T>(ArrayPool<T>.Shared.Rent(length), length);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null)
        {
            return;
        }

        _buffer = null;
        ArrayPool<T>.Shared.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }
}
