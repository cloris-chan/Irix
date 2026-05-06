using System.Text;

namespace Irix.Drawing;

public interface ITextResolver
{
    ReadOnlySpan<char> Resolve(TextSlice slice);
}

public sealed class FrameTextArena : ITextResolver
{
    private const int BufferId = 1;

    public static ITextResolver Empty { get; } = new EmptyTextResolver();

    private readonly StringBuilder _builder = new();
    private string _buffer = string.Empty;
    private bool _sealed;

    public TextSlice Add(string? text)
    {
        return string.IsNullOrEmpty(text)
            ? Add(ReadOnlySpan<char>.Empty)
            : Add(text.AsSpan());
    }

    public TextSlice Add(ReadOnlySpan<char> text)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("Cannot add text after the arena has been sealed.");
        }

        var start = _builder.Length;
        _builder.Append(text);
        return new TextSlice(BufferId, start, text.Length);
    }

    public void Seal()
    {
        if (_sealed)
        {
            return;
        }

        _buffer = _builder.ToString();
        _sealed = true;
    }

    public ReadOnlySpan<char> Resolve(TextSlice slice)
    {
        if (!slice.IsValid || slice.BufferId != BufferId)
        {
            return default;
        }

        if (!_sealed)
        {
            Seal();
        }

        if ((uint)slice.Start > (uint)_buffer.Length
            || (uint)slice.Length > (uint)(_buffer.Length - slice.Start))
        {
            return default;
        }

        return _buffer.AsSpan(slice.Start, slice.Length);
    }

    private sealed class EmptyTextResolver : ITextResolver
    {
        public ReadOnlySpan<char> Resolve(TextSlice slice) => default;
    }
}
