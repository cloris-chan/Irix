using System.Buffers;
using System.Text;

namespace Irix.Drawing;

public interface ITextResolver
{
    ReadOnlySpan<char> Resolve(TextSlice slice);
}

public sealed class FrameTextArena : ITextResolver, IDisposable
{
    private int _bufferId = 1;

    public static ITextResolver Empty { get; } = new EmptyTextResolver();

    private readonly StringBuilder _builder = new();
    private char[]? _charBuffer;
    private int _charLength;
    private bool _sealed;

    public TextSlice Add(string? text)
    {
        return string.IsNullOrEmpty(text)
            ? Add([])
            : Add(text.AsSpan());
    }

    public TextSlice Add(ReadOnlySpan<char> text)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("Cannot add text after the arena has been sealed.");
        }

        var start = _charLength;
        if (_charBuffer is not null)
        {
            AppendToCharBuffer(text, start);
        }
        else
        {
            _builder.Append(text);
        }

        _charLength += text.Length;
        return new TextSlice(_bufferId, start, text.Length);
    }

    public void Seal()
    {
        if (_sealed)
        {
            return;
        }

        if (_charBuffer is null)
        {
            var length = _builder.Length;
            if (length > 0)
            {
                _charBuffer = ArrayPool<char>.Shared.Rent(length);
                _builder.CopyTo(0, _charBuffer, 0, length);
            }

            _charLength = length;
            _builder.Clear();
        }

        _sealed = true;
    }

    public void Reset()
    {
        _builder.Clear();
        _charLength = 0;
        _sealed = false;
        _bufferId = unchecked(_bufferId + 1);
        if (_bufferId <= 0) _bufferId = 1;
    }

    public void Dispose()
    {
        var buffer = _charBuffer;
        if (buffer is null)
        {
            return;
        }

        _charBuffer = null;
        _charLength = 0;
        _builder.Clear();
        _sealed = false;
        _bufferId = unchecked(_bufferId + 1);
        if (_bufferId <= 0) _bufferId = 1;
        ArrayPool<char>.Shared.Return(buffer);
    }

    public ReadOnlySpan<char> Resolve(TextSlice slice)
    {
        if (!slice.IsValid || slice.BufferId != _bufferId)
        {
            return default;
        }

        if (!_sealed)
        {
            Seal();
        }

        if (_charBuffer is null || (uint)slice.Start > (uint)_charLength
            || (uint)slice.Length > (uint)(_charLength - slice.Start))
        {
            return default;
        }

        return _charBuffer.AsSpan(slice.Start, slice.Length);
    }

    private void AppendToCharBuffer(ReadOnlySpan<char> text, int start)
    {
        if (text.IsEmpty)
        {
            return;
        }

        var required = start + text.Length;
        if (required > _charBuffer!.Length)
        {
            var newBuffer = ArrayPool<char>.Shared.Rent(required);
            _charBuffer.AsSpan(0, start).CopyTo(newBuffer);
            ArrayPool<char>.Shared.Return(_charBuffer);
            _charBuffer = newBuffer;
        }

        text.CopyTo(_charBuffer.AsSpan(start));
    }

    private sealed class EmptyTextResolver : ITextResolver
    {
        public ReadOnlySpan<char> Resolve(TextSlice slice) => default;
    }
}
