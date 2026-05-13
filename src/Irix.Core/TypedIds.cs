namespace Irix;

public readonly record struct ActionId(uint Value)
{
    public static readonly ActionId None = default;

    public bool IsNone => Value == 0;

    public override string ToString() => Value == 0 ? "None" : Value.ToString();
}

public readonly record struct NodeKey(uint Value)
{
    public static readonly NodeKey None = default;

    public static implicit operator NodeKey(uint value) => new(value);

    public override string ToString() => Value == 0 ? "None" : Value.ToString();
}

public readonly record struct ElementId(uint Value)
{
    public static readonly ElementId None = default;

    public bool IsNone => Value == 0;

    public override string ToString() => Value == 0 ? "None" : Value.ToString();
}

public readonly record struct TargetId(uint Value)
{
    public static readonly TargetId None = default;

    public bool IsNone => Value == 0;

    public override string ToString() => Value == 0 ? "None" : Value.ToString();
}
