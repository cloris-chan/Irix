using System.Runtime.InteropServices;

namespace Irix;

[StructLayout(LayoutKind.Explicit, Size = 40)]
public readonly struct BorderStroke : IEquatable<BorderStroke>
{
    [FieldOffset(0)] private readonly float _thickness;
    [FieldOffset(4)] private readonly Paint _paint;

    public BorderStroke(Paint paint, float thickness = 1f)
    {
        if (paint.Kind == PaintKind.None)
        {
            throw new ArgumentException("Border paint must be explicit.", nameof(paint));
        }

        if (!float.IsFinite(thickness) || thickness <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "Border thickness must be finite and positive.");
        }

        _thickness = thickness;
        _paint = paint;
    }

    public Paint Paint => _paint;

    public float Thickness => _thickness;

    public bool IsNone => _paint.Kind == PaintKind.None;

    public static BorderStroke None => default;

    public static BorderStroke Solid(Color color, float thickness = 1f) =>
        new(Paint.Solid(color), thickness);

    public bool Equals(BorderStroke other) =>
        _thickness.Equals(other._thickness)
        && _paint == other._paint;

    public override bool Equals(object? obj) => obj is BorderStroke other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_thickness, _paint);

    public static bool operator ==(BorderStroke left, BorderStroke right) => left.Equals(right);

    public static bool operator !=(BorderStroke left, BorderStroke right) => !left.Equals(right);
}
