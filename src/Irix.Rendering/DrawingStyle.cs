using Irix.Drawing;

namespace Irix.Rendering;

internal readonly struct DrawingStyle(
    DrawColor TextColor,
    DrawColor RectangleFillColor,
    DrawColor ButtonFillColor,
    DrawColor ButtonHoverFillColor,
    DrawColor ButtonPressedFillColor,
    DrawColor ButtonFocusedFillColor,
    DrawColor ButtonTextColor,
    TextStyle TextStyle,
    TextStyle ButtonTextStyle) : IEquatable<DrawingStyle>
{

    public DrawColor TextColor { get; } = TextColor;
    public DrawColor RectangleFillColor { get; } = RectangleFillColor;
    public DrawColor ButtonFillColor { get; } = ButtonFillColor;
    public DrawColor ButtonHoverFillColor { get; } = ButtonHoverFillColor;
    public DrawColor ButtonPressedFillColor { get; } = ButtonPressedFillColor;
    public DrawColor ButtonFocusedFillColor { get; } = ButtonFocusedFillColor;
    public DrawColor ButtonTextColor { get; } = ButtonTextColor;
    public TextStyle TextStyle { get; } = TextStyle;
    public TextStyle ButtonTextStyle { get; } = ButtonTextStyle;

    public static DrawingStyle Default => RenderStylePreset.Default.Drawing;

    public bool Equals(DrawingStyle other)
    {
        return TextColor == other.TextColor
            && RectangleFillColor == other.RectangleFillColor
            && ButtonFillColor == other.ButtonFillColor
            && ButtonHoverFillColor == other.ButtonHoverFillColor
            && ButtonPressedFillColor == other.ButtonPressedFillColor
            && ButtonFocusedFillColor == other.ButtonFocusedFillColor
            && ButtonTextColor == other.ButtonTextColor
            && TextStyle == other.TextStyle
            && ButtonTextStyle == other.ButtonTextStyle;
    }

    public override bool Equals(object? obj) => obj is DrawingStyle other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TextColor);
        hash.Add(RectangleFillColor);
        hash.Add(ButtonFillColor);
        hash.Add(ButtonHoverFillColor);
        hash.Add(ButtonPressedFillColor);
        hash.Add(ButtonFocusedFillColor);
        hash.Add(ButtonTextColor);
        hash.Add(TextStyle);
        hash.Add(ButtonTextStyle);
        return hash.ToHashCode();
    }

    public static bool operator ==(DrawingStyle left, DrawingStyle right) => left.Equals(right);

    public static bool operator !=(DrawingStyle left, DrawingStyle right) => !left.Equals(right);
}
