using Irix.Drawing;

namespace Irix.Rendering;

internal readonly struct CompositionLayerId(int Value) : IEquatable<CompositionLayerId>
{
    public int Value { get; } = Value;

    public bool IsValid => Value > 0;

    public bool Equals(CompositionLayerId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is CompositionLayerId other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator ==(CompositionLayerId left, CompositionLayerId right) => left.Equals(right);

    public static bool operator !=(CompositionLayerId left, CompositionLayerId right) => !left.Equals(right);
}

internal readonly struct CompositionTransform(float TranslateX, float TranslateY) : IEquatable<CompositionTransform>
{
    public float TranslateX { get; } = TranslateX;
    public float TranslateY { get; } = TranslateY;

    public static CompositionTransform Identity => default;

    public bool IsIdentity => TranslateX == 0f && TranslateY == 0f;

    public CompositionTransform Scale(DisplayScale scale)
    {
        scale = scale.Normalize();
        return scale.IsIdentity ? this : new CompositionTransform(TranslateX * scale.ScaleX, TranslateY * scale.ScaleY);
    }

    public bool Equals(CompositionTransform other) => TranslateX.Equals(other.TranslateX) && TranslateY.Equals(other.TranslateY);

    public override bool Equals(object? obj) => obj is CompositionTransform other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TranslateX, TranslateY);

    public static bool operator ==(CompositionTransform left, CompositionTransform right) => left.Equals(right);

    public static bool operator !=(CompositionTransform left, CompositionTransform right) => !left.Equals(right);
}

internal readonly struct CompositionOpacity(float Value) : IEquatable<CompositionOpacity>
{
    public float Value { get; } = Value;

    public static CompositionOpacity Opaque => new(1f);

    public float Normalized => float.IsFinite(Value) ? Math.Clamp(Value, 0f, 1f) : 1f;

    public bool IsOpaque => Normalized == 1f;

    public bool Equals(CompositionOpacity other) => Value.Equals(other.Value);

    public override bool Equals(object? obj) => obj is CompositionOpacity other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(CompositionOpacity left, CompositionOpacity right) => left.Equals(right);

    public static bool operator !=(CompositionOpacity left, CompositionOpacity right) => !left.Equals(right);
}

internal readonly struct CompositionLayer(
    CompositionLayerId Id,
    int CommandStart,
    int CommandCount,
    CompositionTransform Transform,
    CompositionOpacity Opacity) : IEquatable<CompositionLayer>
{
    public CompositionLayerId Id { get; } = Id;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public CompositionTransform Transform { get; } = Transform;
    public CompositionOpacity Opacity { get; } = Opacity;

    public bool IsValidForCommandCount(int commandCount)
    {
        return Id.IsValid
            && CommandStart >= 0
            && CommandCount > 0
            && CommandStart <= commandCount
            && CommandStart + CommandCount <= commandCount;
    }

    public bool Equals(CompositionLayer other)
    {
        return Id == other.Id
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && Transform == other.Transform
            && Opacity == other.Opacity;
    }

    public override bool Equals(object? obj) => obj is CompositionLayer other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, CommandStart, CommandCount, Transform, Opacity);

    public static bool operator ==(CompositionLayer left, CompositionLayer right) => left.Equals(right);

    public static bool operator !=(CompositionLayer left, CompositionLayer right) => !left.Equals(right);
}

internal readonly struct CompositionFrame(CompositionLayer Layer) : IEquatable<CompositionFrame>
{
    public CompositionLayer Layer { get; } = Layer;

    public bool IsValidForCommandCount(int commandCount) => Layer.IsValidForCommandCount(commandCount);

    public bool Equals(CompositionFrame other) => Layer == other.Layer;

    public override bool Equals(object? obj) => obj is CompositionFrame other && Equals(other);

    public override int GetHashCode() => Layer.GetHashCode();

    public static bool operator ==(CompositionFrame left, CompositionFrame right) => left.Equals(right);

    public static bool operator !=(CompositionFrame left, CompositionFrame right) => !left.Equals(right);
}
