using System.Runtime.InteropServices;

namespace Irix;

public sealed class VirtualTextArena
{
    private readonly List<char> _buffer = [];
    private uint _nextBufferId = 1;

    public TextBufferId CurrentBufferId { get; private set; }

    public TextNodeContent AddText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return default;

        if (CurrentBufferId.IsNone)
            CurrentBufferId = new TextBufferId(_nextBufferId++);

        var start = _buffer.Count;
        _buffer.AddRange(text);
        return new TextNodeContent(CurrentBufferId, new TextRange(start, text.Length));
    }

    public ReadOnlySpan<char> Resolve(TextNodeContent content)
    {
        if (content.IsNone || content.BufferId != CurrentBufferId)
            return [];

        var start = content.Range.Start;
        var length = content.Range.Length;
        if (start < 0 || length <= 0 || start + length > _buffer.Count)
            return [];

        return CollectionsMarshal.AsSpan(_buffer).Slice(start, length);
    }

    public string ResolveString(TextNodeContent content)
    {
        var span = Resolve(content);
        return span.IsEmpty ? "" : new string(span);
    }

    public TextBufferSnapshot Snapshot()
    {
        return new TextBufferSnapshot(CurrentBufferId, _buffer.ToArray());
    }

    public void Clear()
    {
        _buffer.Clear();
        _nextBufferId = 1;
        CurrentBufferId = default;
    }
}

public readonly struct TextBufferSnapshot : IEquatable<TextBufferSnapshot>
{
    public TextBufferId BufferId { get; }
    public char[] Buffer { get; }

    public TextBufferSnapshot(TextBufferId bufferId, char[] buffer)
    {
        BufferId = bufferId;
        Buffer = buffer;
    }

    public ReadOnlySpan<char> Resolve(TextNodeContent content)
    {
        if (content.IsNone || content.BufferId != BufferId || Buffer is null)
            return [];

        var start = content.Range.Start;
        var length = content.Range.Length;
        if (start < 0 || length <= 0 || start + length > Buffer.Length)
            return [];

        return Buffer.AsSpan(start, length);
    }

    public string ResolveString(TextNodeContent content)
    {
        var span = Resolve(content);
        return span.IsEmpty ? "" : new string(span);
    }

    public bool Equals(TextBufferSnapshot other) => BufferId == other.BufferId && ReferenceEquals(Buffer, other.Buffer);

    public override bool Equals(object? obj) => obj is TextBufferSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BufferId, Buffer?.Length ?? 0);
}
