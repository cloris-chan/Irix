using System.Runtime.InteropServices;

namespace Irix;

public enum PaintKind : byte
{
    None,
    SolidColor,
    LinearGradient
}

public enum LinearGradientDirection : byte
{
    None,
    LeftToRight,
    TopToBottom,
    TopLeftToBottomRight,
    TopRightToBottomLeft
}

[StructLayout(LayoutKind.Explicit, Size = 36)]
public readonly struct Paint : IEquatable<Paint>
{
    [FieldOffset(0)] private readonly PaintKind _kind;
    [FieldOffset(1)] private readonly LinearGradientDirection _direction;
    [FieldOffset(2)] private readonly ushort _padding;
    [FieldOffset(4)] private readonly Color _startColor;
    [FieldOffset(20)] private readonly Color _endColor;

    private Paint(
        PaintKind kind,
        Color startColor,
        Color endColor,
        LinearGradientDirection direction)
    {
        _kind = kind;
        _direction = direction;
        _padding = 0;
        _startColor = startColor;
        _endColor = endColor;
    }

    public PaintKind Kind => _kind;

    public Color StartColor => _startColor;

    public Color EndColor => _endColor;

    public LinearGradientDirection Direction => _direction;

    public static Paint None => default;

    public static Paint Solid(Color color) =>
        new(PaintKind.SolidColor, color, color, LinearGradientDirection.None);

    public static Paint LinearGradient(
        Color startColor,
        Color endColor,
        LinearGradientDirection direction = LinearGradientDirection.LeftToRight)
    {
        if (direction is < LinearGradientDirection.LeftToRight or > LinearGradientDirection.TopRightToBottomLeft)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown linear-gradient direction.");
        }

        return new Paint(PaintKind.LinearGradient, startColor, endColor, direction);
    }

    public bool TryGetSolidColor(out Color color)
    {
        if (_kind != PaintKind.SolidColor)
        {
            color = default;
            return false;
        }

        color = _startColor;
        return true;
    }

    public bool TryGetLinearGradient(
        out Color startColor,
        out Color endColor,
        out LinearGradientDirection direction)
    {
        if (_kind != PaintKind.LinearGradient)
        {
            startColor = default;
            endColor = default;
            direction = LinearGradientDirection.None;
            return false;
        }

        startColor = _startColor;
        endColor = _endColor;
        direction = _direction;
        return true;
    }

    public bool Equals(Paint other) =>
        _kind == other._kind
        && _direction == other._direction
        && _startColor == other._startColor
        && _endColor == other._endColor;

    public override bool Equals(object? obj) => obj is Paint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((byte)_kind, (byte)_direction, _startColor, _endColor);

    public static bool operator ==(Paint left, Paint right) => left.Equals(right);

    public static bool operator !=(Paint left, Paint right) => !left.Equals(right);
}
