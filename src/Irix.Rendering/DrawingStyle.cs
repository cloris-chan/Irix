using Irix.Drawing;

namespace Irix.Rendering;

internal readonly record struct DrawingStyle(
    DrawColor TextColor,
    DrawColor RectangleFillColor,
    DrawColor ButtonFillColor,
    DrawColor ButtonTextColor,
    TextStyle TextStyle,
    TextStyle ButtonTextStyle)
{
    public static DrawingStyle Default => new(
        TextColor: DrawColor.Opaque(255, 255, 255),
        RectangleFillColor: DrawColor.Opaque(72, 72, 72),
        ButtonFillColor: DrawColor.Opaque(52, 120, 246),
        ButtonTextColor: DrawColor.Opaque(255, 255, 255),
        TextStyle: TextStyle.Default,
        ButtonTextStyle: TextStyle.Default);
}
