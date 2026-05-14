namespace Irix;

public readonly struct TextBufferId(uint value) : IEquatable<TextBufferId>
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

public readonly struct TextRange(int start, int length) : IEquatable<TextRange>
{
    public int Start { get; } = start;
    public int Length { get; } = length;

    public int End => Start + Length;

    public bool IsEmpty => Length == 0;

    public bool Equals(TextRange other) => Start == other.Start && Length == other.Length;

    public override bool Equals(object? obj) => obj is TextRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, Length);

    public static bool operator ==(TextRange left, TextRange right) => left.Equals(right);

    public static bool operator !=(TextRange left, TextRange right) => !left.Equals(right);
}

public readonly struct TextNodeContent(TextBufferId bufferId, TextRange range) : IEquatable<TextNodeContent>
{
    public TextBufferId BufferId { get; } = bufferId;
    public TextRange Range { get; } = range;

    public bool IsNone => BufferId.IsNone;

    public bool Equals(TextNodeContent other) => BufferId == other.BufferId && Range == other.Range;

    public override bool Equals(object? obj) => obj is TextNodeContent other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BufferId, Range);

    public static bool operator ==(TextNodeContent left, TextNodeContent right) => left.Equals(right);

    public static bool operator !=(TextNodeContent left, TextNodeContent right) => !left.Equals(right);
}
