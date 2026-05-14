using System.Runtime.InteropServices;

namespace Irix;

public sealed class VirtualTextArena
{
    private readonly List<char> _buffer = [];
    private uint _nextBufferId = 1;
    private TextBufferSnapshot? _cachedSnapshot;

    public TextBufferId CurrentBufferId { get; private set; }

    /// <summary>
    /// Snapshot of the previous frame's buffer, retained for diff comparison.
    /// Null before the first <see cref="BeginFrame"/> call.
    /// </summary>
    public TextBufferSnapshot? PreviousSnapshot { get; private set; }

    public TextNodeContent AddText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return default;

        if (CurrentBufferId.IsNone)
            CurrentBufferId = new TextBufferId(_nextBufferId++);

        var start = _buffer.Count;
        _buffer.AddRange(text);
        _cachedSnapshot = null;
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

    [Obsolete("Use Resolve() with ReadOnlySpan<char> instead. Intended for diagnostics only.")]
    public string ResolveString(TextNodeContent content)
    {
        var span = Resolve(content);
        return span.IsEmpty ? "" : new string(span);
    }

    public TextBufferSnapshot Snapshot()
    {
        return new TextBufferSnapshot(CurrentBufferId, [.. _buffer]);
    }

    /// <summary>
    /// Returns a cached snapshot of the current buffer, creating one if needed.
    /// The cache is invalidated when text is added. Use this instead of <see cref="Snapshot"/>
    /// when the snapshot will not be mutated and may be called multiple times per frame.
    /// </summary>
    public TextBufferSnapshot GetOrCreateSnapshot()
    {
        return _cachedSnapshot ??= new TextBufferSnapshot(CurrentBufferId, [.. _buffer]);
    }

    /// <summary>
    /// Begins a new frame: captures the current buffer as <see cref="PreviousSnapshot"/>,
    /// then clears the buffer for fresh text. Call this before <c>BuildView</c>.
    /// </summary>
    public void BeginFrame()
    {
        PreviousSnapshot = _buffer.Count > 0 ? GetOrCreateSnapshot() : null;
        _buffer.Clear();
        CurrentBufferId = default;
        _cachedSnapshot = null;
    }

    public void Clear()
    {
        _buffer.Clear();
        _nextBufferId = 1;
        CurrentBufferId = default;
        _cachedSnapshot = null;
        PreviousSnapshot = null;
    }
}

public readonly struct TextBufferSnapshot(TextBufferId bufferId, char[] buffer) : IEquatable<TextBufferSnapshot>
{
    public TextBufferId BufferId { get; } = bufferId;
    public char[] Buffer { get; } = buffer;

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

    [Obsolete("Use Resolve() with ReadOnlySpan<char> instead. Intended for diagnostics only.")]
    public string ResolveString(TextNodeContent content)
    {
        var span = Resolve(content);
        return span.IsEmpty ? "" : new string(span);
    }

    public bool Equals(TextBufferSnapshot other) => BufferId == other.BufferId && ReferenceEquals(Buffer, other.Buffer);

    public override bool Equals(object? obj) => obj is TextBufferSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BufferId, Buffer?.Length ?? 0);

    public static bool operator ==(TextBufferSnapshot left, TextBufferSnapshot right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TextBufferSnapshot left, TextBufferSnapshot right)
    {
        return !(left == right);
    }
}
