namespace Irix;

public readonly struct ActionId(uint value) : IEquatable<ActionId>
{
    public static readonly ActionId None = default;

    public uint Value { get; } = value;

    public bool IsNone => Value == 0;

    public bool Equals(ActionId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is ActionId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(ActionId left, ActionId right) => left.Value == right.Value;

    public static bool operator !=(ActionId left, ActionId right) => left.Value != right.Value;

    public override string ToString() => Value.ToString();
}

public readonly struct NodeKey(uint value) : IEquatable<NodeKey>
{
    public static readonly NodeKey None = default;

    public uint Value { get; } = value;

    public static implicit operator NodeKey(uint value) => new(value);

    public bool Equals(NodeKey other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is NodeKey other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(NodeKey left, NodeKey right) => left.Value == right.Value;

    public static bool operator !=(NodeKey left, NodeKey right) => left.Value != right.Value;
}

public readonly struct ElementId(uint value) : IEquatable<ElementId>
{
    public static readonly ElementId None = default;

    public uint Value { get; } = value;

    public bool IsNone => Value == 0;

    public bool Equals(ElementId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is ElementId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(ElementId left, ElementId right) => left.Value == right.Value;

    public static bool operator !=(ElementId left, ElementId right) => left.Value != right.Value;
}

public readonly struct TargetId(uint value) : IEquatable<TargetId>
{
    public static readonly TargetId None = default;

    public uint Value { get; } = value;

    public bool IsNone => Value == 0;

    public bool Equals(TargetId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is TargetId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(TargetId left, TargetId right) => left.Value == right.Value;

    public static bool operator !=(TargetId left, TargetId right) => left.Value != right.Value;
}
