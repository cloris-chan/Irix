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

    public ReadOnlySpan<char> ResolveRequired(TextNodeContent content)
    {
        if (content.IsNone)
            return [];

        if (content.BufferId != CurrentBufferId)
        {
            throw new InvalidOperationException(
                $"Text content buffer id {content.BufferId.Value} does not match current text buffer id {CurrentBufferId.Value}.");
        }

        var start = content.Range.Start;
        var length = content.Range.Length;
        if ((uint)start > (uint)_buffer.Count || (uint)length > (uint)(_buffer.Count - start))
        {
            throw new InvalidOperationException(
                $"Text content range [{start}..{start + length}) is outside the current text buffer length {_buffer.Count}.");
        }

        return CollectionsMarshal.AsSpan(_buffer).Slice(start, length);
    }

    internal bool TryResolve(TextNodeContent content, out ReadOnlySpan<char> span)
    {
        if (content.IsNone)
        {
            span = [];
            return true;
        }

        if (content.BufferId != CurrentBufferId)
        {
            span = [];
            return false;
        }

        var start = content.Range.Start;
        var length = content.Range.Length;
        if ((uint)start > (uint)_buffer.Count || (uint)length > (uint)(_buffer.Count - start))
        {
            span = [];
            return false;
        }

        span = CollectionsMarshal.AsSpan(_buffer).Slice(start, length);
        return true;
    }

    /// <summary>
    /// Returns a cached snapshot of the current buffer, creating one if needed.
    /// The cache is invalidated when text is added.
    /// </summary>
    public TextBufferSnapshot GetOrCreateSnapshot()
    {
        return _cachedSnapshot ??= new TextBufferSnapshot(CurrentBufferId, [.. _buffer]);
    }

    internal TextBufferSnapshot GetOrCreateSnapshot(bool measureAllocation, out TextBufferSnapshotAllocationAttribution attribution)
    {
        attribution = default;
        if (_cachedSnapshot is { } cached)
        {
            return cached;
        }

        var beforeTotal = GetAllocatedBytes(measureAllocation);
        var beforeCharBuffer = GetAllocatedBytes(measureAllocation);
        char[] buffer = [.. _buffer];
        attribution = attribution.WithCharBuffer(AllocatedDelta(measureAllocation, beforeCharBuffer));

        var beforeSnapshotShell = GetAllocatedBytes(measureAllocation);
        var snapshot = new TextBufferSnapshot(CurrentBufferId, buffer);
        _cachedSnapshot = snapshot;
        attribution = attribution.WithSnapshotShell(AllocatedDelta(measureAllocation, beforeSnapshotShell));
        attribution = attribution.WithMeasured(AllocatedDelta(measureAllocation, beforeTotal));
        return snapshot;
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

    private static long GetAllocatedBytes(bool enabled) => enabled ? GC.GetTotalAllocatedBytes(false) : 0;

    private static long AllocatedDelta(bool enabled, long before) => enabled ? GC.GetTotalAllocatedBytes(false) - before : 0;
}

internal readonly struct TextBufferSnapshotAllocationAttribution(
    long CharBufferBytes,
    long SnapshotShellBytes,
    long MeasuredBytes) : IEquatable<TextBufferSnapshotAllocationAttribution>
{
    public long CharBufferBytes { get; } = CharBufferBytes;
    public long SnapshotShellBytes { get; } = SnapshotShellBytes;
    public long MeasuredBytes { get; } = MeasuredBytes;
    public long DetailBytes => CharBufferBytes + SnapshotShellBytes;
    public long DetailGapBytes => MeasuredBytes - DetailBytes;

    public TextBufferSnapshotAllocationAttribution Add(TextBufferSnapshotAllocationAttribution other) =>
        new(
            CharBufferBytes + other.CharBufferBytes,
            SnapshotShellBytes + other.SnapshotShellBytes,
            MeasuredBytes + other.MeasuredBytes);

    public TextBufferSnapshotAllocationAttribution WithCharBuffer(long bytes) =>
        new(CharBufferBytes + bytes, SnapshotShellBytes, MeasuredBytes);

    public TextBufferSnapshotAllocationAttribution WithSnapshotShell(long bytes) =>
        new(CharBufferBytes, SnapshotShellBytes + bytes, MeasuredBytes);

    public TextBufferSnapshotAllocationAttribution WithMeasured(long bytes) =>
        new(CharBufferBytes, SnapshotShellBytes, MeasuredBytes + bytes);

    public bool Equals(TextBufferSnapshotAllocationAttribution other)
    {
        return CharBufferBytes == other.CharBufferBytes
            && SnapshotShellBytes == other.SnapshotShellBytes
            && MeasuredBytes == other.MeasuredBytes;
    }

    public override bool Equals(object? obj) => obj is TextBufferSnapshotAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CharBufferBytes, SnapshotShellBytes, MeasuredBytes);
}

public readonly struct TextBufferSnapshot(TextBufferId bufferId, char[] buffer) : IEquatable<TextBufferSnapshot>
{
    public TextBufferId BufferId { get; } = bufferId;
    public char[] Buffer { get; } = buffer;

    public ReadOnlySpan<char> ResolveRequired(TextNodeContent content)
    {
        if (content.IsNone)
            return [];

        if (Buffer is null)
        {
            throw new InvalidOperationException("Text content requires a text buffer snapshot, but the snapshot is default.");
        }

        if (BufferId.IsNone)
        {
            throw new InvalidOperationException("Text content requires a text buffer snapshot with a buffer id.");
        }

        if (content.BufferId != BufferId)
        {
            throw new InvalidOperationException(
                $"Text content buffer id {content.BufferId.Value} does not match snapshot buffer id {BufferId.Value}.");
        }

        var start = content.Range.Start;
        var length = content.Range.Length;
        if ((uint)start > (uint)Buffer.Length || (uint)length > (uint)(Buffer.Length - start))
        {
            throw new InvalidOperationException(
                $"Text content range [{start}..{start + length}) is outside snapshot buffer length {Buffer.Length}.");
        }

        return Buffer.AsSpan(start, length);
    }

    internal bool TryResolve(TextNodeContent content, out ReadOnlySpan<char> span)
    {
        if (content.IsNone)
        {
            span = [];
            return true;
        }

        if (Buffer is null || BufferId.IsNone || content.BufferId != BufferId)
        {
            span = [];
            return false;
        }

        var start = content.Range.Start;
        var length = content.Range.Length;
        if ((uint)start > (uint)Buffer.Length || (uint)length > (uint)(Buffer.Length - start))
        {
            span = [];
            return false;
        }

        span = Buffer.AsSpan(start, length);
        return true;
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
