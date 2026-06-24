namespace Irix;

internal readonly struct TextBufferId(uint value) : IEquatable<TextBufferId>
{
    public static readonly TextBufferId None = default;

    public uint Value { get; } = value;

    public bool IsNone => Value == 0;

    public bool Equals(TextBufferId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is TextBufferId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(TextBufferId left, TextBufferId right) => left.Value == right.Value;

    public static bool operator !=(TextBufferId left, TextBufferId right) => left.Value != right.Value;
}

internal readonly struct TextRange : IEquatable<TextRange>
{
    public TextRange(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (start > int.MaxValue - length) throw new ArgumentOutOfRangeException(nameof(length), "start + length overflows");
        Start = start;
        Length = length;
    }

    public int Start { get; }
    public int Length { get; }

    public int End => Start + Length;

    public bool IsEmpty => Length == 0;

    public bool Equals(TextRange other) => Start == other.Start && Length == other.Length;

    public override bool Equals(object? obj) => obj is TextRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, Length);

    public static bool operator ==(TextRange left, TextRange right) => left.Equals(right);

    public static bool operator !=(TextRange left, TextRange right) => !left.Equals(right);
}

internal readonly struct TextContentResource(TextBufferId bufferId, TextRange range) : IEquatable<TextContentResource>
{
    public TextBufferId BufferId { get; } = bufferId;
    public TextRange Range { get; } = range;

    public bool IsNone => BufferId.IsNone;

    public bool Equals(TextContentResource other) => BufferId == other.BufferId && Range == other.Range;

    public override bool Equals(object? obj) => obj is TextContentResource other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BufferId, Range);

    public static bool operator ==(TextContentResource left, TextContentResource right) => left.Equals(right);

    public static bool operator !=(TextContentResource left, TextContentResource right) => !left.Equals(right);
}
